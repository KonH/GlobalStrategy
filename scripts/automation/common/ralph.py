"""Shared Ralph loop orchestration - the parts that don't depend on which agent CLI (Claude Code
or Codex) drives each iteration. Provider-specific wrappers (scripts/automation/claude/ralph.py,
scripts/automation/codex/ralph.py) supply the actual subprocess invocation and prompt text via a
small set of hooks, then call run_ralph() here for everything else: branch/spec resolution, the
create-prd/loop/complete-prd phase flow, stall detection, PRD progress tracking, and totals
reporting.

One thing deliberately stays a per-provider hook rather than shared logic: determine_stop_reason.
Claude's and Codex's loops disagree on whether a self-reported "<promise>COMPLETE</promise>"
should end the loop even while the PRD still has open tasks (Claude trusts it unconditionally,
Codex only accepts "all tasks passed" and logs+ignores a premature COMPLETE claim). That is a
genuine, currently-existing behavioral difference between the two pipelines, not an accident of
duplication - unifying it is a separate decision from this structural refactor, so each provider
keeps its own policy here.
"""

import argparse
import csv
import json
import math
import re
import subprocess
from datetime import datetime
from pathlib import Path


# Files that change on every iteration regardless of real progress: the activity journal
# (always appended per PROMPT.md step 6) and ProjectSettings.asset (bundleVersion bump is
# mandatory on every commit per .claude/commands/commit.md). A "blocked, made no real change"
# iteration still touches both of these, so excluding only activity.md let a stuck loop commit
# its way past --stall-limit forever (see the GlobalStrategy#41 incident, where 12
# verification-only bundleVersion-bump commits masked a fully stalled loop). A genuine progress
# commit always touches at least one other file, so it still counts as meaningful.
NON_PROGRESS_FILES = {".ralph/activity.md", "ProjectSettings/ProjectSettings.asset"}


def run_git(args, check=True):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip(), result.returncode


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


def parse_prd_tasks(prd_text):
    match = re.search(
        r"^## Tasks[ \t]*\r?\n+```json[ \t]*\r?\n(?P<tasks>.*?)\r?\n```[ \t]*(?:\r?\n|$)",
        prd_text,
        re.MULTILINE | re.DOTALL,
    )
    if match is None:
        raise RuntimeError("PRD has no fenced JSON task array under the '## Tasks' heading.")

    try:
        tasks = json.loads(match.group("tasks"))
    except json.JSONDecodeError as error:
        raise RuntimeError(f"PRD task array is invalid JSON: {error}") from error

    if not isinstance(tasks, list):
        raise RuntimeError("PRD task JSON must be an array.")
    for index, task in enumerate(tasks, start=1):
        if not isinstance(task, dict) or not isinstance(task.get("passes"), bool):
            raise RuntimeError(f"PRD task {index} must be an object with a boolean 'passes' field.")
    return tasks


def has_open_tasks(prd_text):
    return any(not task["passes"] for task in parse_prd_tasks(prd_text))


def task_progress(prd_text):
    tasks = parse_prd_tasks(prd_text)
    total = len(tasks)
    passed = sum(task["passes"] for task in tasks)
    percent = (passed / total * 100) if total else 0.0
    return passed, total, percent


def should_complete_prd(skip_pull_request, passed_tasks):
    return not skip_pull_request and passed_tasks > 0


def log_step_failure(tool_name, phase, iteration, prompt, exit_code, stdout_text, stderr_text,
                      err_file, log_file, csv_file, activity_file):
    """Writes the standard diagnostics for a failed CLI invocation: the raw stderr file, a
    combined stdout/stderr log, a csv error row, and an activity.md journal entry. Shared by
    every provider's invoke-step function since the shape is identical - only the tool name
    (used in messages and the csv stop_reason suffix, e.g. "claude_error"/"codex_error") differs.
    """
    err_file.write_text(stderr_text, encoding="utf-8")

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

    print(f"{tool_name} exited with code {exit_code} in phase '{phase}'. Details: {log_file}")
    if stderr_text:
        print(stderr_text)
    if stdout_text:
        print(stdout_text[:2000])

    with csv_file.open("a", encoding="utf-8", newline="") as f:
        f.write(f"{phase},{iteration},,,,,,,,{tool_name}_error\n")

    summary_source = stderr_text if stderr_text else stdout_text
    summary = " ".join(summary_source.splitlines()[:5])
    activity_content = "\n".join([
        "",
        f"## {datetime.now().strftime('%Y-%m-%d')} - Ralph loop error (phase: {phase}, iteration: {iteration})",
        "",
        f"{tool_name} exited with code {exit_code}. See `{log_file}` for full stdout/stderr.",
        "",
        f"Summary: {summary}",
        "",
        "---",
    ])
    with activity_file.open("a", encoding="utf-8") as f:
        f.write(activity_content + "\n")


def build_arg_parser(description="Ralph loop runner"):
    """Common CLI arguments shared by every provider wrapper. Callers may add provider-specific
    arguments (e.g. Codex's --sandbox) to the returned parser before calling parse_args()."""
    parser = argparse.ArgumentParser(description=description)
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
    parser.add_argument("--dangerously-skip-permissions", action="store_true")
    parser.add_argument("--model", type=str, default=None,
                         help="Model id passed to every invocation this run. Omit to use the "
                              "CLI's own default.")
    parser.add_argument("--effort", type=str, default=None,
                         help="Reasoning effort passed to every invocation this run (e.g. "
                              "medium). Omit to use the CLI's own default.")
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
    return parser


def run_ralph(args, *, tool_name, invoke_step, build_create_prd_prompt, build_loop_prompt,
              build_complete_prd_prompt, determine_stop_reason, complete_prd_hint):
    """Runs the full Ralph loop: resolves the branch, drives /create-prd, loops one task per
    fresh-context iteration until the PRD is done (or stalled/erroring), then drives
    /complete-prd. Everything provider-specific is supplied via hooks:

    - invoke_step(phase, iteration, prompt, csv_file, log_dir, activity_file, model, effort)
      -> dict | None. Performs the actual CLI call, including its own diagnostic logging on
      failure (see log_step_failure above). Returns None on hard failure, or a dict with at
      least {"is_error": bool, "result": str} on completion.
    - build_create_prd_prompt(spec_id, env) -> str
    - build_loop_prompt(prompt_text, env) -> str
    - build_complete_prd_prompt(complete_prd_arg) -> str
    - determine_stop_reason(result, prd_text, stall_count, stall_limit) -> str | None. Returns a
      stop_reason to end the loop this iteration, or None to keep going. See the module
      docstring for why this stays a hook instead of shared logic.
    - complete_prd_hint(complete_prd_arg) -> str. The command to print when the loop didn't
      finish and the user needs to run /complete-prd by hand.
    """
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
        if not has_open_tasks(prd_text):
            raise RuntimeError(f"{prd_file} has no open tasks - nothing to loop.")
        print(f"Skipping /create-prd (bot-feature mode), reusing existing {prd_file}")
    elif perf_mode:
        # Perf-optimization PRDs are written directly by /optimize-performance - no spec-folder
        # resolution and no /create-prd phase, same as bot-feature mode.
        if not prd_file.exists():
            raise RuntimeError(f"{prd_file} does not exist - run /optimize-performance first.")
        prd_text = prd_file.read_text(encoding="utf-8")
        if not has_open_tasks(prd_text):
            raise RuntimeError(f"{prd_file} has no open tasks - nothing to loop.")
        print(f"Skipping /create-prd (perf-target mode), reusing existing {prd_file}")
    elif args.skip_create_prd:
        if not prd_file.exists():
            raise RuntimeError(f"--skip-create-prd was passed but {prd_file} does not exist.")
        print(f"Skipping /create-prd, reusing existing {prd_file}")
    else:
        print()
        create_prd_prompt = build_create_prd_prompt(args.spec, args.env)
        print(f"=== Phase: {create_prd_prompt} ===")
        r = invoke_step("create-prd", "", create_prd_prompt, csv_file, log_dir, activity_file,
                         args.model, args.effort)
        if r is None or r.get("is_error"):
            raise RuntimeError("/create-prd failed - aborting before the loop.")
        prd_text = prd_file.read_text(encoding="utf-8")
        if not has_open_tasks(prd_text):
            raise RuntimeError(f"/create-prd produced no open tasks in {prd_file} - check its output before looping.")

    # --- Guard: MaxIterations should cover at least 1.5x the task count ---
    prd_text = prd_file.read_text(encoding="utf-8")
    task_count = len(parse_prd_tasks(prd_text))
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
        prompt = build_loop_prompt(prompt_file.read_text(encoding="utf-8"), args.env)
        r = invoke_step("loop", str(i), prompt, csv_file, log_dir, activity_file, args.model, args.effort)
        if r is None:
            stop_reason = f"{tool_name}_error"
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

        this_iter_stop_reason = determine_stop_reason(r, prd_text, stall_count, args.stall_limit)
        if this_iter_stop_reason:
            stop_reason = this_iter_stop_reason
            break

    print()
    print(f"=== Loop finished: {stop_reason} ===")

    # --- Phase 3: commit + pull request ---
    passed_tasks, total_tasks, _ = task_progress(prd_text)
    if args.skip_pull_request:
        print("Skipping /complete-prd (--skip-pull-request).")
    elif not should_complete_prd(args.skip_pull_request, passed_tasks):
        print("No PRD tasks passed - skipping /complete-prd because there is no verified progress to propose.")
        print(f"To inspect or package the branch manually, run: {complete_prd_hint(complete_prd_arg)}")
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
        complete_prompt = build_complete_prd_prompt(complete_prd_arg)
        if passed_tasks < total_tasks:
            print(
                f"Loop stopped with {passed_tasks}/{total_tasks} tasks passed ({stop_reason}); "
                "creating a PR for the verified partial work."
            )
        print(f"=== Phase: {complete_prompt} ===")
        r = invoke_step("complete-prd", "", complete_prompt, csv_file, log_dir, activity_file,
                         args.model, args.effort)
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
