---
name: dotnet-build
description: Build GlobalStrategy's .NET solution with the shared captured-output workflow. Use for Debug or Release builds.
---

# .NET Build

Invoke `cd:dotnet-build` with `src/GlobalStrategy.Core.sln` as the default solution and `Debug` as the default configuration. Forward any user-supplied solution, project, configuration, or build flags unchanged.

For Release builds, follow `.claude/rules/unity/plugins.md`; the solution writes Unity-consumed DLLs into `Assets/Plugins/Core/`.
