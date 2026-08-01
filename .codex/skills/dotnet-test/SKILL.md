---
name: dotnet-test
description: Run and diagnose .NET tests from a repository root, including targeted test filters, captured output, and concise pass/fail reporting. Use when the user asks to run, verify, or troubleshoot `dotnet test` for a solution or project.
---

# .NET Test

Run the requested suite without changing directories. Default to `src/GlobalStrategy.Core.sln` and the `Debug` configuration when neither is specified.

1. Create `.tmp` if it does not already exist.
2. Run `dotnet test` once with stdout and stderr captured to `.tmp/dotnet-test.log`. Use the shell tool's `workdir` rather than `cd`. In PowerShell:

   ```powershell
   dotnet test src/GlobalStrategy.Core.sln -c Debug *> .tmp/dotnet-test.log
   ```

   Forward a requested solution/project path, configuration, `--filter` expression, and other flags verbatim. For example:

   ```powershell
   dotnet test src/GlobalStrategy.Core.sln -c Debug --filter "FullyQualifiedName~Namespace.ClassName" *> .tmp/dotnet-test.log
   ```

3. Read the captured log once. For long output, inspect the end of the file instead of rerunning the command or adding shell filtering commands.
4. Report test totals (passed, failed, skipped) and, on failure, the failing test names and assertion messages.
5. Remove `.tmp/dotnet-test.log` in a separate shell call when the result has been reported.

Preserve the command's exit status: a failed test run is a validation result, not a reason to hide or retry it unless the user asks.