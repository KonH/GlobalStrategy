---
name: dotnet-test
description: Test a .NET solution from the project root without `cd`, logging output to a single file that gets read once instead of piped through repeated Bash calls.
---

# dotnet test

Runs `dotnet test` against a solution, avoiding the two habits that cause unnecessary
permission prompts and wasted turns:

1. **Never prefix the command with `cd`.** The Bash tool's working directory is already
   the project root for every call — a leading `cd` is redundant and (per this project's
   shell rule) triggers an extra permission prompt for no benefit.
2. **Log to one file, read it once.** Don't pipe through `tail`/`grep` as a second Bash
   call. Redirect stdout+stderr to a single log file, then use the `Read` tool on that
   file (with `offset`/`limit` if it's long) instead of issuing more shell commands.

## Args

`/dotnet-test [solution-path] [Configuration] [--filter <expr>]`

- **solution-path** (optional): path to the `.sln`, relative to project root. Defaults to
  `src/GlobalStrategy.Core.sln` for this repo.
- **Configuration** (optional): defaults to `Debug`.
- **--filter** (optional): forwarded verbatim to `dotnet test --filter` to scope to a
  subset of tests (e.g. a single class while iterating).

Any additional flags the caller needs should be appended to the command line as given —
this skill only fixes *how* the command is invoked, not *what* it is.

## Steps

1. Pick the log path: `.tmp/dotnet-test.log` (gitignored, per
   `.claude/rules/temp_scripts.md`).
2. Run, in a single Bash tool call, with `dangerouslyDisableSandbox: true` (per this
   project's dotnet-command convention — no permission prompt needed as long as there's
   no `cd`):

   ```
   dotnet test <solution-path> -c <Configuration> [--filter <expr>] > .tmp/dotnet-test.log 2>&1
   ```

   Default:
   ```
   dotnet test src/GlobalStrategy.Core.sln -c Debug > .tmp/dotnet-test.log 2>&1
   ```

3. Read `.tmp/dotnet-test.log` with the `Read` tool. Use `offset` from the end (or a
   large `limit`) for heavy output — don't re-run the command to see more, and don't add
   a second Bash call to filter it.
4. Report pass/fail/skip counts from what you read, and quote any failing test names and
   assertion messages.
5. Delete the log when done with it, as a separate Bash call, per the temp-scripts
   run-then-delete convention.

## Notes

- This applies to any solution/project, not just one repo — the log-file + single-Read
  pattern is the reusable part. The default solution path above is specific to
  GlobalStrategy; adjust it per-project.
