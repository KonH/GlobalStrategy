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
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --max-iterations 10
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --skip-create-prd          # reuse the existing .ralph/prd.md
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --skip-pull-request        # stop after the loop, no commit/PR phase
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --dangerously-skip-permissions
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --stall-limit 5
  python scripts/automation/claude/ralph.py --spec 26_07_11_10_province-ownership --env full-env-headless --model claude-sonnet-5 --effort medium --auto-adjust-iterations --skip-pull-request --dangerously-skip-permissions

The loop stops early (before -MaxIterations) if --stall-limit consecutive iterations commit
nothing but .ralph/activity.md and the mandatory ProjectSettings.asset version bump - a sign
the loop is blocked on an unavailable prerequisite (e.g. Unity Editor MCP bridge not connected,
a missing tool like Node.js) rather than making real progress.

--model/--effort apply to every claude -p invocation this run (create-prd, loop, complete-prd);
omit either to use the CLI's own defaults. --auto-adjust-iterations raises --max-iterations to
the recommended minimum instead of failing when the PRD ends up with more tasks than expected -
meant for automation with nobody watching to retry by hand.

Metrics per phase/iteration are appended to .ralph/metrics_<SpecId>.csv (gitignored).

Shared orchestration lives in scripts/automation/common/ralph.py - this file only supplies what's
specific to driving Claude Code: CLI argument shape, prompt text, and result interpretation.
"""

import json
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from common.ralph import (  # noqa: E402
    NON_PROGRESS_FILES,
    build_arg_parser,
    has_open_tasks,
    log_step_failure,
    run_ralph,
)


TOOL_NAME = "claude"

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


def find_claude_executable():
    return shutil.which("claude") or "claude"


def build_create_prd_prompt(spec_id, env):
    prompt = f"/create-prd {spec_id}"
    if env:
        prompt += f" {env}"
    return prompt


def build_loop_prompt(prompt_text, env):
    if not env:
        return prompt_text
    return (
        prompt_text
        + f"\n\nAutomation environment: {env}. Unity Editor/MCP and image-generation "
          "tools are unavailable. Do not probe for or invoke them; leave excluded asset, "
          "scene, and image tasks untouched."
    )


def build_complete_prd_prompt(complete_prd_arg):
    return f"/complete-prd {complete_prd_arg}"


def complete_prd_hint(complete_prd_arg):
    return f'claude -p "/complete-prd {complete_prd_arg}"'


def determine_stop_reason(result, prd_text, stall_count, stall_limit):
    if result.get("is_error"):
        return "result_error"
    if "<promise>COMPLETE</promise>" in (result.get("result") or ""):
        return "complete_promise"
    if not has_open_tasks(prd_text):
        return "all_tasks_passed"
    if stall_count >= stall_limit:
        print(
            f"Stopping: {stall_count} consecutive iterations made no change beyond "
            f"{', '.join(sorted(NON_PROGRESS_FILES))}. This usually means a required "
            "prerequisite is unavailable (e.g. Unity Editor MCP bridge not connected, or a "
            "missing tool like Node.js) - check the latest .ralph/activity.md entries before "
            "resuming."
        )
        return "stalled_no_progress"
    return None


def invoke_claude_step(claude_exe, phase, iteration, prompt, dangerously_skip_permissions,
                        csv_file, log_dir, activity_file, model=None, effort=None):
    allowed_tools = LOOP_TOOLS + PR_EXTRA_TOOLS if phase == "complete-prd" else LOOP_TOOLS

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
    log_file = log_dir / f"{phase}_{iteration}_{stamp}.log"

    # Claude emits UTF-8 JSON/text.  On Windows, ``text=True`` otherwise uses
    # the active ANSI code page (often cp1252), which can fail while decoding
    # valid Claude output before the runner has a chance to record the error.
    proc = subprocess.run(claude_args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    exit_code = proc.returncode
    # A subprocess failure can leave either stream unset.  Error reporting
    # must still produce the Ralph activity and diagnostic logs in that case.
    stdout_text = proc.stdout or ""
    stderr_text = proc.stderr or ""

    if exit_code != 0:
        log_step_failure(TOOL_NAME, phase, iteration, prompt, exit_code, stdout_text, stderr_text,
                          err_file, log_file, csv_file, activity_file)
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
    parser = build_arg_parser(description="Ralph loop runner (Claude)")
    args = parser.parse_args()

    claude_exe = find_claude_executable()

    def invoke_step(phase, iteration, prompt, csv_file, log_dir, activity_file, model, effort):
        return invoke_claude_step(
            claude_exe, phase, iteration, prompt, args.dangerously_skip_permissions,
            csv_file, log_dir, activity_file, model=model, effort=effort,
        )

    run_ralph(
        args,
        tool_name=TOOL_NAME,
        invoke_step=invoke_step,
        build_create_prd_prompt=build_create_prd_prompt,
        build_loop_prompt=build_loop_prompt,
        build_complete_prd_prompt=build_complete_prd_prompt,
        determine_stop_reason=determine_stop_reason,
        complete_prd_hint=complete_prd_hint,
    )


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
