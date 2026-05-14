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

## Work autonomously — do not ask for approval on intermediate steps

Act, then report the outcome. Do not narrate what you are about to do and then ask whether to proceed.

Normal work that needs no confirmation: reading files, searching, writing/editing files, refreshing Unity, checking the console, spawning sub-agents.

Still confirm before: deleting files or branches, force-pushing, creating PRs, sending messages to external services, any action that is hard to reverse or affects shared state.

**Explicit approval checkpoints (do not skip these):**
- After `/plan` writes a plan — stop and wait for user feedback before proceeding
- After `/plan-review` — present concerns and wait for user to say which to apply
- After `/code-review` — present concerns and wait for user to say which to apply

"No approval" means: do not prompt during implementation for reading/writing/editing files, using tools, refreshing Unity, or running searches. Those are internal work steps, not decisions that need user input.

## Edit indentation tip

When `Edit` fails due to tab/space mismatch, `Read` the exact line range to copy the raw indentation from the output, then retry.
