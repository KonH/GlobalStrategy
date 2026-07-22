#!/usr/bin/env python3
"""Ralph loop - runs `codex exec` with a fresh context per iteration until the spec's PRD is done.

Flow:
  1. Resolves Docs/Specs/<YY_MM_DD_HH>_<name>/ and switches to branch ralph/<spec-id> (creates it if needed)
  2. Pre-run:  Codex follows .claude/commands/create-prd.md to build .ralph/prd.md
  3. Loop:     Codex follows .ralph/PROMPT.md one task per fresh-context iteration
  4. Post-run: Codex follows .claude/commands/complete-prd.md to commit leftovers and open a PR

Normally the Unity Editor should be running (Unity MCP is used to verify Unity-side tasks).
For unattended runs with no Editor available, pass --env full-env-headless (or --env
code-only if the plan never touches Unity assets/scenes) - see .claude/commands/create-prd.md
for what each marker changes about task planning.

Usage (from project root):
  python scripts/codex_ralph.py --spec 26_07_11_10_province-ownership --max-iterations 10
  python scripts/codex_ralph.py --spec 26_07_11_10_province-ownership --env full-env-headless --model gpt-5.6-sol --effort medium --auto-adjust-iterations --skip-pull-request

The loop stops early (before -MaxIterations) if --stall-limit consecutive iterations commit
nothing but .ralph/activity.md and the mandatory ProjectSettings.asset version bump - a sign
the loop is blocked on an unavailable prerequisite (e.g. Unity Editor MCP bridge not connected,
a missing tool like Node.js) rather than making real progress.

--model/--effort apply to every Codex invocation this run (create-prd, loop, complete-prd);
omit either to use the CLI's own defaults. --auto-adjust-iterations raises --max-iterations to
the recommended minimum instead of failing when the PRD ends up with more tasks than expected -
meant for automation with nobody watching to retry by hand.

Metrics per phase/iteration are appended to .ralph/metrics_<SpecId>.csv (gitignored).
"""

import argparse
import csv
import math
import re
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path


def find_codex_executable():
    exe = shutil.which("codex")
    if exe:
        return exe
    return "codex"


def run_git(args, check=True):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip(), result.returncode


# Files that change on every iteration regardless of real progress: the activity journal
# (always appended per PROMPT.md step 6) and ProjectSettings.asset (bundleVersion bump is
# mandatory on every commit per .claude/commands/commit.md). A "blocked, made no real change"
# iteration still touches both of these, so excluding only activity.md let a stuck loop commit
# its way past --stall-limit forever (see the GlobalStrategy#41 incident, where 12
# verification-only bundleVersion-bump commits masked a fully stalled loop). A genuine progress
# commit always touches at least one other file, so it still counts as meaningful.
NON_PROGRESS_FILES = {".ralph/activity.md", "ProjectSettings/ProjectSettings.asset"}
DEFAULT_SANDBOX = "danger-full-access"
SANDBOX_CHOICES = ["read-only", "workspace-write", "danger-full-access"]


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


def task_progress(prd_text):
    total = len(re.findall(r'"passes":\s*(?:true|false)', prd_text))
    passed = len(re.findall(r'"passes":\s*true', prd_text))
    percent = (passed / total * 100) if total else 0.0
    return passed, total, percent


def invoke_codex_step(codex_exe, phase, iteration, prompt, sandbox, dangerously_skip_permissions,
                      csv_file, log_dir, activity_file, model=None, effort=None):
    codex_args = [
        codex_exe, "exec", "--json", "--sandbox", sandbox,
        "--config", "approval_policy=\"never\"",
        "--ignore-user-config",
    ]
    if sandbox == "workspace-write":
        codex_args += ["--config", "sandbox_workspace_write.network_access=true"]
    if model:
        codex_args += ["--model", model]
    if effort:
        codex_args += ["--config", f'model_reasoning_effort=\"{effort}\"']
    if dangerously_skip_permissions:
        codex_args.append("--yolo")
    # Read the prompt from standard input.  This avoids Windows command-line
    # length limits and ensures the full iteration instructions reach Codex.
    codex_args.append("-")

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    err_file = log_dir / f"{phase}_{iteration}_{stamp}.stderr.log"

    # Codex emits UTF-8 JSON/text.  On Windows, ``text=True`` otherwise uses
    # the active ANSI code page (often cp1252), which can fail while decoding
    # valid Codex output before the runner has a chance to record the error.
    proc = subprocess.run(
        codex_args,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        input=prompt,
    )
    exit_code = proc.returncode
    # A subprocess failure can leave either stream unset.  Error reporting
    # must still produce the Ralph activity and diagnostic logs in that case.
    stdout_text = proc.stdout or ""
    stderr_text = proc.stderr or ""

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

        print(f"codex exited with code {exit_code} in phase '{phase}'. Details: {log_file}")
        if stderr_text:
            print(stderr_text)
        if stdout_text:
            print(stdout_text[:2000])

        with csv_file.open("a", encoding="utf-8", newline="") as f:
            f.write(f"{phase},{iteration},,,,,,,,codex_error\n")

        summary_source = stderr_text if stderr_text else stdout_text
        summary = " ".join(summary_source.splitlines()[:5])
        activity_content = "\n".join([
            "",
            f"## {datetime.now().strftime('%Y-%m-%d')} - Ralph loop error (phase: {phase}, iteration: {iteration})",
            "",
            f"codex exited with code {exit_code}. See `{log_file}` for full stdout/stderr.",
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

    log_file = log_dir / f"{phase}_{iteration}_{stamp}.log"
    log_file.write_text("\n".join([
        f"phase: {phase}",
        f"iteration: {iteration}",
        "exit_code: 0",
        f"prompt: {prompt}",
        "",
        "--- stdout ---",
        stdout_text,
        "",
        "--- stderr ---",
        stderr_text,
    ]), encoding="utf-8")

    with csv_file.open("a", encoding="utf-8", newline="") as f:
        f.write(f"{phase},{iteration},,,,,,,,,\n")
    print(f"{phase}: Codex completed successfully.")
    return {"is_error": False, "result": stdout_text}


def main():
    parser = argparse.ArgumentParser(description="Ralph loop runner")
    parser.add_argument("--spec", type=str, default=None, help="Dated spec folder id (YY_MM_DD_HH_name)")
    parser.add_argument("--bot-feature", type=str, default=None)
    parser.add_argument("--perf-target", type=str, default=None)
    parser.add_argument("--max-iterations", type=int, default=10)
    parser.add_argument(
        "--stall-limit", type=int, default=3,
        help="Stop the loop after this many consecutive iterations that commit nothing but "
             "%s (e.g. blocked on Unity MCP / other unavailable prerequisites)." % ", ".join(sorted(NON_PROGRESS_FILES)),
    )
    parser.add_argument("--skip-create-prd", action="store_true")
    parser.add_argument("--skip-pull-request", action="store_true")
    parser.add_argument(
        "--sandbox", default=DEFAULT_SANDBOX, choices=SANDBOX_CHOICES,
        help="Codex sandbox mode (defaults to danger-full-access for this dedicated automation clone).",
    )
    parser.add_argument("--dangerously-skip-permissions", action="store_true")
    parser.add_argument("--model", type=str, default=None,
                         help="Model id passed to every Codex invocation (e.g. gpt-5.6-sol). "
                              "Omit to use the CLI's own default.")
    parser.add_argument("--effort", type=str, default=None,
                         help="Reasoning effort passed to every Codex invocation "
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

    codex_exe = find_codex_executable()

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
        create_prd_prompt = (
            "Read and follow .claude/commands/create-prd.md as a Codex procedure. "
            f"The dated spec identifier is {args.spec}."
        )
        if args.env:
            create_prd_prompt += f" The environment marker is {args.env}."
        print(f"=== Phase: {create_prd_prompt} ===")
        r = invoke_codex_step(
            codex_exe, "create-prd", "", create_prd_prompt,
            args.sandbox, args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
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
        prompt = "Read AGENTS.md first, then follow these iteration instructions exactly:\n\n" + prompt_file.read_text(encoding="utf-8")
        r = invoke_codex_step(
            codex_exe, "loop", str(i), prompt,
            args.sandbox, args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
            model=args.model, effort=args.effort,
        )
        if r is None:
            stop_reason = "codex_error"
            break

        new_head = get_head()
        changed = changed_files_since(prev_head, new_head)
        meaningful_change = new_head != prev_head and any(f not in NON_PROGRESS_FILES for f in changed)
        if meaningful_change:
            stall_count = 0
        else:
            stall_count += 1
            reason = "no commit made" if new_head == prev_head else f"only {', '.join(changed)} changed"
            print(f"No meaningful progress this iteration ({reason}) - stall {stall_count}/{args.stall_limit}.")

        prd_text = prd_file.read_text(encoding="utf-8")
        passed, total, percent = task_progress(prd_text)
        print(f"Progress: {passed}/{total} tasks passed ({percent:.0f}%)")

        has_open_tasks = bool(re.search(r'"passes":\s*false', prd_text))
        if r.get("is_error"):
            stop_reason = "result_error"
        elif not has_open_tasks:
            stop_reason = "all_tasks_passed"
        else:
            if "<promise>COMPLETE</promise>" in (r.get("result") or ""):
                print("Ignoring COMPLETE promise because the PRD still has open tasks.")
            if stall_count >= args.stall_limit:
                stop_reason = "stalled_no_progress"
                print(
                    f"Stopping: {stall_count} consecutive iterations made no change beyond "
                    f"{', '.join(sorted(NON_PROGRESS_FILES))}. This usually means a required "
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
        print(f'  codex exec "Read and follow .claude/commands/complete-prd.md for {complete_prd_arg}."')
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
        print(f"=== Phase: complete-prd {complete_prd_arg} ===")
        complete_prompt = (
            "Read and follow .claude/commands/complete-prd.md as a Codex procedure. "
            f"The argument is {complete_prd_arg}."
        )
        r = invoke_codex_step(
            codex_exe, "complete-prd", "", complete_prompt,
            args.sandbox, args.dangerously_skip_permissions, csv_file, log_dir, activity_file,
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
