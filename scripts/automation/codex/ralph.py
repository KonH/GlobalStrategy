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
  python scripts/automation/codex/ralph.py --spec 26_07_11_10_province-ownership --max-iterations 10
  python scripts/automation/codex/ralph.py --spec 26_07_11_10_province-ownership --env full-env-headless --model gpt-5.6-sol --effort medium --auto-adjust-iterations --skip-pull-request

The loop stops early (before -MaxIterations) if --stall-limit consecutive iterations commit
nothing but .ralph/activity.md and the mandatory ProjectSettings.asset version bump - a sign
the loop is blocked on an unavailable prerequisite (e.g. Unity Editor MCP bridge not connected,
a missing tool like Node.js) rather than making real progress.

--model/--effort apply to every Codex invocation this run (create-prd, loop, complete-prd);
omit either to use the CLI's own defaults. --auto-adjust-iterations raises --max-iterations to
the recommended minimum instead of failing when the PRD ends up with more tasks than expected -
meant for automation with nobody watching to retry by hand.

Metrics per phase/iteration are appended to .ralph/metrics_<SpecId>.csv (gitignored).

Shared orchestration lives in scripts/automation/common/ralph.py - this file only supplies what's
specific to driving Codex: CLI argument shape, prompt text, and result interpretation.
"""

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


TOOL_NAME = "codex"
DEFAULT_SANDBOX = "danger-full-access"
SANDBOX_CHOICES = ["read-only", "workspace-write", "danger-full-access"]


def find_codex_executable():
    return shutil.which("codex") or "codex"


def build_create_prd_prompt(spec_id, env):
    prompt = (
        "Read and follow .claude/commands/create-prd.md as a Codex procedure. "
        f"The dated spec identifier is {spec_id}."
    )
    if env:
        prompt += f" The environment marker is {env}."
    return prompt


def build_loop_prompt(prompt_text, env):
    prompt = "Read AGENTS.md first, then follow these iteration instructions exactly:\n\n" + prompt_text
    if env:
        prompt += (
            f"\n\nAutomation environment: {env}. Unity Editor/MCP and image-generation "
            "tools are unavailable. Do not probe for or invoke them; leave excluded asset, "
            "scene, and image tasks untouched."
        )
    return prompt


def build_complete_prd_prompt(complete_prd_arg):
    return (
        "Read and follow .claude/commands/complete-prd.md as a Codex procedure. "
        f"The argument is {complete_prd_arg}."
    )


def complete_prd_hint(complete_prd_arg):
    return f'codex exec "Read and follow .claude/commands/complete-prd.md for {complete_prd_arg}."'


def determine_stop_reason(result, prd_text, stall_count, stall_limit):
    if result.get("is_error"):
        return "result_error"
    if not has_open_tasks(prd_text):
        return "all_tasks_passed"
    if "<promise>COMPLETE</promise>" in (result.get("result") or ""):
        print("Ignoring COMPLETE promise because the PRD still has open tasks.")
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
    log_file = log_dir / f"{phase}_{iteration}_{stamp}.log"

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
        log_step_failure(TOOL_NAME, phase, iteration, prompt, exit_code, stdout_text, stderr_text,
                          err_file, log_file, csv_file, activity_file)
        return None

    if err_file.exists():
        err_file.unlink()

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
    parser = build_arg_parser(description="Ralph loop runner (Codex)")
    parser.add_argument(
        "--sandbox", default=DEFAULT_SANDBOX, choices=SANDBOX_CHOICES,
        help="Codex sandbox mode (defaults to danger-full-access for this dedicated automation clone).",
    )
    args = parser.parse_args()

    codex_exe = find_codex_executable()

    def invoke_step(phase, iteration, prompt, csv_file, log_dir, activity_file, model, effort):
        return invoke_codex_step(
            codex_exe, phase, iteration, prompt, args.sandbox, args.dangerously_skip_permissions,
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
