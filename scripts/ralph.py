#!/usr/bin/env python3
"""Ralph loop - runs `claude -p` with a fresh context per iteration until the spec's PRD is done.

Flow:
  1. Resolves Docs/Specs/<YY_MM_DD_HH>_<name>/ and switches to branch ralph/<spec-id> (creates it if needed)
  2. Pre-run:  claude "/create-prd <SpecId>"  - builds .ralph/prd.md from the spec's plan.md
  3. Loop:     claude .ralph/PROMPT.md           - one task per fresh-context iteration
  4. Post-run: claude "/complete-prd <SpecId>" - commits leftovers and opens a PR (only if all tasks passed)

Normally the Unity Editor should be running (Unity MCP is used to verify Unity-side tasks).
For unattended runs with no Editor available, pass --env full-env-headless (or --env
code-only if the plan never touches Unity assets/scenes) - see .claude/commands/create-prd.md
for what each marker changes about task planning.

Usage (from project root):
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --max-iterations 10
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --skip-create-prd          # reuse the existing .ralph/prd.md
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --skip-pull-request        # stop after the loop, no commit/PR phase
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --dangerously-skip-permissions
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --stall-limit 5
  python scripts/ralph.py --spec 26_07_11_10_province-ownership --env full-env-headless --model claude-sonnet-5 --effort medium --auto-adjust-iterations --skip-pull-request --dangerously-skip-permissions

The loop stops early (before -MaxIterations) if --stall-limit consecutive iterations commit
nothing but .ralph/activity.md - a sign the loop is blocked on an unavailable prerequisite
(e.g. Unity Editor MCP bridge not connected, a missing tool like Node.js) rather than making
real progress.

--model/--effort apply to every claude -p invocation this run (create-prd, loop, complete-prd);
omit either to use the CLI's own defaults. --auto-adjust-iterations raises --max-iterations to
the recommended minimum instead of failing when the PRD ends up with more tasks than expected -
meant for automation with nobody watching to retry by hand.

Metrics per phase/iteration are appended to .ralph/metrics_<SpecId>.csv (gitignored).
"""

import argparse
import csv
import json
import math
import re
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path


def find_claude_executable():
    exe = shutil.which("claude")
    if exe:
        return exe
    return "claude"


def run_git(args, check=True):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip(), result.returncode


JOURNAL_ONLY_FILES = {".ralph/activity.md"}


def get_head():
    head, code = run_git(["rev-parse", "HEAD"], check=False)
    return head if code == 0 else None


def changed_files_since(prev_head, new_head):
    if prev_head is None or new_head is None or prev_head == new_head:
        return []
    diff, code = run_git(["diff", "--name-only", prev_head, new_head], check=False)
    if code != 0 or not diff:
        return []
    return diff.splitlines()


def resolve_spec_dir(spec_id):
    specs_root = Path("Docs/Specs")
    if not re.fullmatch(r"\d{2}_\d{2}_\d{2}_\d{2}_[a-z0-9]+(?:-[a-z0-9]+)*", spec_id):
        raise RuntimeError(
            "Spec id must use YY_MM_DD_HH_name format "
            "(for example, 26_07_11_10_province-ownership)."
        )
    spec_dir = specs_root / spec_id
    if not spec_dir.is_dir():
        raise RuntimeError(f"No spec folder named {spec_id} exists under Docs/Specs.")
    if not (spec_dir / "plan.md").exists():
        raise RuntimeError(f"Spec folder {spec_dir.name} has no plan.md - run /plan first.")
    return spec_dir


def resolve_branch(ralph_branch):
    branch, _ = run_git(["rev-parse", "--abbrev-ref", "HEAD"])
    if branch in ("main", "master"):
        dirty, _ = run_git(["status", "--porcelain"])
        if dirty:
            raise RuntimeError("Working tree is not clean - commit or stash before starting a Ralph run.")
        _, exists_code = run_git(["rev-parse", "--verify", "--quiet", f"refs/heads/{ralph_branch}"], check=False)
        if exists_code == 0:
            _, code = run_git(["checkout", ralph_branch], check=False)
        else:
            _, code = run_git(["checkout", "-b", ralph_branch], check=False)
        if code != 0:
            raise RuntimeError(f"Failed to switch to branch {ralph_branch}.")
        branch = ralph_branch
    elif branch != ralph_branch:
        print(f"Already on non-main branch '{branch}' - staying here instead of switching to {ralph_branch}.")
    return branch, ralph_branch


LOOP_TOOLS = [
    "mcp__UnityMCP",
    "Bash(dotnet build:*)", "Bash(dotnet test:*)", "Bash(dotnet run:*)",
    "Bash(git add:*)", "Bash(git commit:*)", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)",
    "Bash(.venv/Scripts/python.exe:*)",
    "PowerShell(dotnet build *)", "PowerShell(dotnet test *)", "PowerShell(dotnet run *)",
    "PowerShell(git add *)", "PowerShell(git commit *)", "PowerShell(git status *)", "PowerShell(git diff *)", "PowerShell(git log *)",
    "PowerShell(.venv\\Scripts\\python.exe *)",
]

PR_EXTRA_TOOLS = [
    "Bash(git push:*)", "Bash(gh pr create:*)", "Bash(gh pr view:*)",
    "PowerShell(git push *)", "PowerShell(gh pr create *)", "PowerShell(gh pr view *)",
]


def task_progress(prd_text):
    total = len(re.findall(r'"passes":\s*(?:true|false)', prd_text))
    passed = len(re.findall(r'"passes":\s*true', prd_text))
    percent = (passed / total * 100) if total else 0.0
    return passed, total, percent


def invoke_claude_step(claude_exe, phase, iteration, prompt, allowed_tools, dangerously_skip_permissions,
                        csv_file, log_dir, activity_file, model=None, effort=None):
    claude_args = [claude_exe, "-p", prompt, "--output-format", "json"]
    if model:
        claude_args += ["--model", model]
    if effort:
        claude_args += ["--effort", effort]
    if dangerously_skip_permissions:
        claude_args.append("--dangerously-skip-permissions")
    else:
        claude_args += ["--permission-mode", "acceptEdits", "--allowedTools", ",".join(allowed_tools)]

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    err_file = log_dir / f"{phase}_{iteration}_{stamp}.stderr.log"

    proc = subprocess.run(claude_args, capture_output=True, text=True)
    exit_code = proc.returncode
    stdout_text = proc.stdout
    stderr_text = proc.stderr

    if exit_code != 0:
        err_file.write_text(stderr_text, encoding="utf-8")

        log_file = log_dir / f"{phase}_{iteration}_{stamp}.log"
        log_content = "\n".join([
            f"phase: {phase}",
            f"iteration: {iteration}",
            f"exit_code: {exit_code}",
            f"prompt: {prompt}",
            "",
            "--- stdout ---",
            stdout_text,
            "",
            "--- stderr ---",
            stderr_text,
        ])
        log_file.write_text(log_content, encoding="utf-8")

        print(f"claude exited with code {exit_code} in phase '{phase}'. Details: {log_file}")
        if stderr_text:
            print(stderr_text)
        if stdout_text:
            print(stdout_text[:2000])

        with csv_file.open("a", encoding="utf-8", newline="") as f:
            f.write(f"{phase},{iteration},,,,,,,,claude_error\n")

        summary_source = stderr_text if stderr_text else stdout_text
        summary = " ".join(summary_source.splitlines()[:5])
        activity_content = "\n".join([
            "",
            f"## {datetime.now().strftime('%Y-%m-%d')} - Ralph loop error (phase: {phase}, iteration: {iteration})",
            "",
            f"claude exited with code {exit_code}. See `{log_file}` for full stdout/stderr.",
            "",
            f"Summary: {summary}",
            "",
            "---",
        ])
        with activity_file.open("a", encoding="utf-8") as f:
            f.write(activity_content + "\n")

        return None

    if err_file.exists():
        err_file.unlink()

    result = json.loads(stdout_text)
    usage = result.get("usage", {})
    with csv_file.open("a", encoding="utf-8", newline="") as f:
        f.write(
            f"{phase},{iteration},{result.get('total_cost_usd', '')},{result.get('num_turns', '')},"
            f"{usage.get('input_tokens', '')},{usage.get('output_tokens', '')},"
            f"{usage.get('cache_read_input_tokens', '')},{usage.get('cache_creation_input_tokens', '')},"
            f"{result.get('duration_ms', '')},\n"
        )
    duration_s = (result.get("duration_ms") or 0) / 1000
    print(f"{phase}: cost ${result.get('total_cost_usd')}  turns {result.get('num_turns')}  duration {duration_s:.0f}s")
    return result


def main():
    parser = argparse.ArgumentParser(description="Ralph loop runner")
    parser.add_argument("--spec", type=str, default=None, help="Dated spec folder id (YY_MM_DD_HH_name)")
    parser.add_argument("--bot-feature", type=str, default=None)
    parser.add_argument("--perf-target", type=str, default=None)
    parser.add_argument("--max-iterations", type=int, default=10)
    parser.add_argument(
        "--stall-limit", type=int, default=3,
        help="Stop the loop after this many consecutive iterations that commit nothing but "
             "%s (e.g. blocked on Unity MCP / other unavailable prerequisites)." % ", ".join(sorted(JOURNAL_ONLY_FILES)),
    )
    parser.add_argument("--skip-create-prd", action="store_true")
    parser.add_argument("--skip-pull-request", action="store_true")
    parser.add_argument("--dangerously-skip-permissions", action="store_true")
    parser.add_argument("--model", type=str, default=None,
                         help="Model id passed to every claude -p invocation this run (e.g. "
                              "claude-sonnet-5). Omit to use the CLI's own default.")
    parser.add_argument("--effort", type=str, default=None,
                         help="Reasoning effort passed to every claude -p invocation this run "
                              "(e.g. medium). Omit to use the CLI's own default.")
    parser.add_argument("--env", type=str, default=None, choices=["code-only", "full-env-headless"],
                         help="Environment marker forwarded to /create-prd (spec mode only) so it "
                              "can plan tasks appropriately when no Unity Editor/MCP is available "
                              "(full-env-headless) or confirm no adjustment is needed (code-only). "
                              "Omit for normal interactive runs where a Unity Editor is expected.")
    parser.add_argument("--auto-adjust-iterations", action="store_true",
                         help="If --max-iterations is below the recommended minimum (task count "
                              "x 1.5), raise it to that minimum automatically instead of failing. "
                              "Intended for unattended automation, where there's no one to notice "
                              "a RuntimeError and re-invoke by hand with a higher value.")
    args = parser.parse_args()

    mode_count = sum(1 for m in (args.spec, args.bot_feature, args.perf_target) if m is not None)
    if mode_count != 1:
        raise RuntimeError("Exactly one of --spec, --bot-feature, or --perf-target must be given.")
    bot_mode = args.bot_feature is not None
    perf_mode = args.perf_target is not None

    prompt_file = Path(".ralph/PROMPT.md")
    if not prompt_file.exists():
        raise RuntimeError(f"Missing {prompt_file} - run from the project root.")
    prd_file = Path(".ralph/prd.md")
    activity_file = Path(".ralph/activity.md")
    log_dir = Path(".ralph/logs")
    log_dir.mkdir(parents=True, exist_ok=True)

    claude_exe = find_claude_executable()

    if bot_mode:
        feature_id = args.bot_feature
        ralph_branch = f"ralph/bot_{feature_id}"
        csv_file = Path(f".ralph/metrics_bot_{feature_id}.csv")
        complete_prd_arg = f"bot:{feature_id}"
        print(f"Bot feature: {feature_id}")
    elif perf_mode:
        perf_target = args.perf_target
        ralph_branch = f"ralph/perf_{perf_target}"
        csv_file = Path(f".ralph/metrics_perf_{perf_target}.csv")
        complete_prd_arg = f"perf:{perf_target}"
        print(f"Perf target: {perf_target}")
    else:
        spec_dir = resolve_spec_dir(args.spec)
        print(f"Spec: Docs/Specs/{spec_dir.name}")
        ralph_branch = f"ralph/{spec_dir.name}"
        csv_file = Path(f".ralph/metrics_{args.spec}.csv")
        complete_prd_arg = args.spec

    branch, ralph_branch = resolve_branch(ralph_branch)
    print(f"Branch: {branch}")

    if not csv_file.exists():
        csv_file.write_text(
            "phase,iteration,cost_usd,num_turns,input_tokens,output_tokens,cache_read,cache_create,duration_ms,stop_reason\n",
            encoding="utf-8",
        )

    loop_tools = LOOP_TOOLS
    pr_tools = LOOP_TOOLS + PR_EXTRA_TOOLS

    # --- Phase 1: create PRD from the spec's plan ---
    if bot_mode:
        # Bot-feature PRDs are written directly by /implement-bot-feature - no spec-folder
        # resolution and no /create-prd phase.
        if not prd_file.exists():
            raise RuntimeError(f"{prd_file} does not exist - run /implement-bot-feature first.")
        prd_text = prd_file.read_text(encoding="utf-8")
        if not re.search(r'"passes":\s*false', prd_text):
            raise RuntimeError(f"{prd_file} has no open tasks - nothing to loop.")
        print(f"Skipping /create-prd (bot-feature mode), reusing existing {prd_file}")
    elif perf_mode:
        # Perf-optimization PRDs are written directly by /optimize-performance - no spec-folder
        # resolution and no /create-prd phase, same as bot-feature mode.
        if not prd_file.exists():
            raise RuntimeError(f"{prd_file} does not exist - run /optimize-performance first.")
        prd_text = prd_file.read_text(encoding="utf-8")
        if not re.search(r'"passes":\s*false', prd_text):
            raise RuntimeError(f"{prd_file} has no open tasks - nothing to loop.")
        print(f"Skipping /create-prd (perf-target mode), reusing existing {prd_file}")
    elif args.skip_create_prd:
        if not prd_file.exists():
            raise RuntimeError(f"--skip-create-prd was passed but {prd_file} does not exist.")
        print(f"Skipping /create-prd, reusing existing {prd_file}")
    else:
        print()
        create_prd_prompt = f"/create-prd {args.spec}"
        if args.env:
            create_prd_prompt += f" {args.env}"
        print(f"=== Phase: {create_prd_prompt} ===")
        r = invoke_claude_step(
            claude_exe, "create-prd", "", create_prd_prompt, loop_tools,
            args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
            model=args.model, effort=args.effort,
        )
        if r is None or r.get("is_error"):
            raise RuntimeError("/create-prd failed - aborting before the loop.")
        prd_text = prd_file.read_text(encoding="utf-8")
        if not re.search(r'"passes":\s*false', prd_text):
            raise RuntimeError(f"/create-prd produced no open tasks in {prd_file} - check its output before looping.")

    # --- Guard: MaxIterations should cover at least 1.5x the task count ---
    prd_text = prd_file.read_text(encoding="utf-8")
    task_count = len(re.findall(r'"passes":\s*(?:true|false)', prd_text))
    min_recommended = math.ceil(task_count * 1.5)
    if args.max_iterations < min_recommended:
        if args.auto_adjust_iterations:
            print(
                f"--max-iterations ({args.max_iterations}) is below the recommended minimum "
                f"({min_recommended} = {task_count} tasks x 1.5) for {prd_file} - "
                f"raising it to {min_recommended} (--auto-adjust-iterations)."
            )
            args.max_iterations = min_recommended
        else:
            raise RuntimeError(
                f"MaxIterations ({args.max_iterations}) is below the recommended minimum "
                f"({min_recommended} = {task_count} tasks x 1.5) for {prd_file}. "
                "Increase --max-iterations (or pass --auto-adjust-iterations), or split the PRD, before looping."
            )
    print(f"PRD has {task_count} tasks; MaxIterations={args.max_iterations} (recommended >= {min_recommended}).")

    # --- Phase 2: the loop ---
    stop_reason = "max_iterations"
    stall_count = 0
    for i in range(1, args.max_iterations + 1):
        print()
        print(f"=== Ralph iteration {i} / {args.max_iterations} ({ralph_branch}) ===")

        prev_head = get_head()
        prompt = prompt_file.read_text(encoding="utf-8")
        r = invoke_claude_step(
            claude_exe, "loop", str(i), prompt, loop_tools,
            args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
            model=args.model, effort=args.effort,
        )
        if r is None:
            stop_reason = "claude_error"
            break

        new_head = get_head()
        changed = changed_files_since(prev_head, new_head)
        meaningful_change = new_head != prev_head and any(f not in JOURNAL_ONLY_FILES for f in changed)
        if meaningful_change:
            stall_count = 0
        else:
            stall_count += 1
            reason = "no commit made" if new_head == prev_head else f"only {', '.join(changed)} changed"
            print(f"No meaningful progress this iteration ({reason}) - stall {stall_count}/{args.stall_limit}.")

        prd_text = prd_file.read_text(encoding="utf-8")
        passed, total, percent = task_progress(prd_text)
        print(f"Progress: {passed}/{total} tasks passed ({percent:.0f}%)")

        if r.get("is_error"):
            stop_reason = "result_error"
        elif "<promise>COMPLETE</promise>" in (r.get("result") or ""):
            stop_reason = "complete_promise"
        elif not re.search(r'"passes":\s*false', prd_text):
            stop_reason = "all_tasks_passed"
        elif stall_count >= args.stall_limit:
            stop_reason = "stalled_no_progress"
            print(
                f"Stopping: {stall_count} consecutive iterations made no change beyond "
                f"{', '.join(sorted(JOURNAL_ONLY_FILES))}. This usually means a required "
                "prerequisite is unavailable (e.g. Unity Editor MCP bridge not connected, or a "
                "missing tool like Node.js) - check the latest .ralph/activity.md entries before "
                "resuming."
            )
        if stop_reason != "max_iterations":
            break

    print()
    print(f"=== Loop finished: {stop_reason} ===")

    # --- Phase 3: commit + pull request ---
    loop_succeeded = stop_reason in ("complete_promise", "all_tasks_passed")
    if args.skip_pull_request:
        print("Skipping /complete-prd (--skip-pull-request).")
    elif not loop_succeeded:
        print("Loop did not finish all tasks - skipping PR. To create one anyway, run:")
        print(f'  claude -p "/complete-prd {complete_prd_arg}"')
        if bot_mode:
            print(
                f"Failure report: Docs/BotFeatures/{feature_id}/eval_summary.md, "
                f"Docs/BotFeatures/{feature_id}/eval_history.json, and .ralph/activity.md."
            )
        elif perf_mode:
            print(
                "Failure report: Docs/Benchmarks/history.json, Docs/Benchmarks/summary.md, "
                "and .ralph/activity.md."
            )
    else:
        print()
        print(f"=== Phase: /complete-prd {complete_prd_arg} ===")
        r = invoke_claude_step(
            claude_exe, "complete-prd", "", f"/complete-prd {complete_prd_arg}", pr_tools,
            args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
            model=args.model, effort=args.effort,
        )
        if r is None or r.get("is_error"):
            print("/complete-prd failed - commit/PR may need manual attention.")
        elif r.get("result"):
            print(r["result"])

    # --- Totals ---
    with csv_file.open("r", encoding="utf-8", newline="") as f:
        rows = [row for row in csv.DictReader(f) if row.get("cost_usd")]
    if rows:
        def total(field):
            return sum(float(row[field]) for row in rows if row.get(field))

        total_cost = total("cost_usd")
        total_turns = total("num_turns")
        total_input = total("input_tokens")
        total_output = total("output_tokens")
        total_cache_r = total("cache_read")
        total_cache_c = total("cache_create")
        print()
        print(f"TOTAL (all rows in {csv_file}): cost ${total_cost}  turns {total_turns:.0f}")
        print(f"tokens: input {total_input:.0f}  output {total_output:.0f}  cache_read {total_cache_r:.0f}  cache_create {total_cache_c:.0f}")


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
