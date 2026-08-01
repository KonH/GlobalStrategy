---
name: dotnet-build
description: Build .NET solutions or projects from a repository root with captured output and clear compiler-error reporting. Use when the user asks to run, verify, or troubleshoot `dotnet build`, including Debug or Release builds.
---

# .NET Build

Build without changing directories. Default to `src/GlobalStrategy.Core.sln` and the `Debug` configuration when neither is specified.

1. Create `.tmp` if it does not already exist.
2. Run `dotnet build` once with stdout and stderr captured to `.tmp/dotnet-build.log`. Use the shell tool's `workdir` rather than `cd`. In PowerShell:

   ```powershell
   dotnet build src/GlobalStrategy.Core.sln -c Debug *> .tmp/dotnet-build.log
   ```

   Forward a requested solution/project path, configuration, and other flags verbatim. For example:

   ```powershell
   dotnet build src/GlobalStrategy.Core.sln -c Release --no-restore *> .tmp/dotnet-build.log
   ```

3. Read the captured log once. For long output, inspect the end of the file instead of rerunning the command or adding shell filtering commands.
4. Report the build result plus error and warning counts. Name the relevant compiler errors when the build fails.
5. Remove `.tmp/dotnet-build.log` in a separate shell call when the result has been reported.

Preserve the command's exit status: a failed build is a validation result, not a reason to hide or retry it unless the user asks.