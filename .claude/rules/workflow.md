# Workflow & Tool Usage

## Use dedicated tools — never PowerShell for what they already handle

| Need | Use |
|---|---|
| Read a file | `Read` |
| Write / create a file | `Write` |
| Edit a file | `Edit` (Read first if the file already exists) |
| Search file contents | `Grep` |
| Find files by name/pattern | `Glob` |
| Unity asset / scene changes | MCP tools, then `refresh_unity` + `read_console` |
| Delete a Unity asset | `manage_asset(action="delete")` |

PowerShell is for things with no dedicated tool: `git`, `dotnet build`, image generation scripts.

Every PowerShell call requires a permission prompt and blocks the session. Dedicated tools run in the approved sandbox without interrupting the user.

### Glob directory-listing gotcha (Windows)

`Glob` with a trailing-slash pattern (`Docs/Specs/*/`) or a bare `Docs/Specs/*` silently returns nothing on Windows. Before falling back to Bash `ls`/`find`, try a nested file pattern instead — e.g. `Docs/Specs/*/*.md` — which matches files one level down and reliably returns the folder names as path prefixes. This avoids a PowerShell/Bash permission prompt for what is still just a file listing. Only fall back to Bash if no Glob pattern shape can express the query.

## Work autonomously — do not ask for approval on intermediate steps

Act, then report the outcome. Do not narrate what you are about to do and then ask whether to proceed.

Normal work that needs no confirmation: reading files, searching, writing/editing files, refreshing Unity, checking the console, spawning sub-agents.

Still confirm before: deleting files or branches, force-pushing, creating PRs, sending messages to external services, any action that is hard to reverse or affects shared state.

**Explicit approval checkpoints (do not skip these):**
- After `/specify` writes a spec — present it to the user and stop; do not run `/plan` until the user approves
- After `/plan` surfaces constitution violations — present each violation and wait for user to confirm resolution before finalising the plan
- After `/plan` writes a plan — stop and wait for user feedback before proceeding
- After `/plan-review` — present concerns and wait for user to say which to apply
- After `/code-review` — present concerns and wait for user to say which to apply

"No approval" means: do not prompt during implementation for reading/writing/editing files, using tools, refreshing Unity, or running searches. Those are internal work steps, not decisions that need user input.

## When stuck — cap retries at 3

After 3 failed attempts fixing the same issue, stop instead of trying a 4th variation on the same approach:
- State what was tried and the specific error each attempt produced
- Check 2-3 similar implementations already in the codebase for a different pattern
- Reconsider whether the abstraction level is wrong or the problem should be split, rather than repeating the same fix with small tweaks

## Edit indentation tip

When `Edit` fails due to tab/space mismatch, `Read` the exact line range to copy the raw indentation from the output, then retry.
