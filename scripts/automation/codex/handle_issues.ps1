# Handle Labeled Issues (Codex) - thin wrapper for handle_issues.py in this same folder.
#
# Run this scheduled task from a dedicated automation checkout. It resets to origin/main
# before invoking Codex, so do not use a development checkout or create a worktree.
# See handle_issues.py and .codex/skills/codex-issue/SKILL.md.
#
# Only issues/PRs labeled 'codex' are ever considered - create the labels once per repo:
#   gh label create codex --color 5319E7 --description "Execute this item's prompt via the Codex automation"
#   gh label create codex-in-progress --color FBCA04 --description "Automation actively working this item"
#   gh label create codex-needs-attention --color D93F0B --description "Automation waiting on the owner"
#   gh label create codex-complete --color 0E8A16 --description "Automation finished this item's prompt"
#
# The labels are the whole state machine: an item is picked up iff it carries 'codex' and
# none of the three status labels. Resume a needs-attention/complete item by replying in a
# comment and removing that status label. Codex is only invoked when discovery finds at
# least one candidate.
#
# The Python runner uses an OS-level non-blocking lock on Windows and POSIX. Also set Task
# Scheduler's "don't start a new instance if already running" option as a second safeguard.
#
# The Python script writes its own auto-rotating log (Logs\handle_issues_codex.log,
# 5MB x 5 backups by default) - no need to also capture Task Scheduler's own output.
#
# Example Task Scheduler action (e.g. every 15 min): run this script with working directory
# set to the dedicated clone's root.
#
# Usage (from the dedicated clone's root):
#   .\scripts\automation\codex\handle_issues.ps1
#   .\scripts\automation\codex\handle_issues.ps1 -Model gpt-5.6-sol -Effort high
#   .\scripts\automation\codex\handle_issues.ps1 -Sandbox danger-full-access

param(
    [string]$Model = "gpt-5.6-sol",
    [ValidateSet("minimal", "low", "medium", "high", "xhigh")]
    [string]$Effort = "high",
    [ValidateSet("read-only", "workspace-write", "danger-full-access")]
    [string]$Sandbox = "workspace-write",
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_issues.py"

$pyArgs = @($scriptPath, "--model", $Model, "--effort", $Effort, "--sandbox", $Sandbox)
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }
& $pythonExe @pyArgs
exit $LASTEXITCODE
