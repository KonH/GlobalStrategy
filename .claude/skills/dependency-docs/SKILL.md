---
name: dependency-docs
description: >-
  Retrieve version-matched external API documentation for Unity, Unity packages,
  and third-party libraries. Use for version-sensitive or uncertain API behavior,
  integration details, signatures, migrations, and package-specific examples;
  do not use for project-local architecture or conventions already covered by
  AGENTS.md and .claude/rules.
---

# Dependency documentation

Ground dependency answers and implementation decisions in documentation that
matches the version this checkout actually uses. Keep retrieval narrow: load the
few pages or files needed to answer the current question, not an entire manual.

## Source order

1. Establish the installed version before searching:
   - Unity Editor: `ProjectSettings/ProjectVersion.txt`.
   - Unity packages: prefer the resolved entry in `Packages/packages-lock.json`,
     then `Packages/manifest.json`. Preserve Git URL tags, branches, and commit
     fragments when present.
   - .NET packages: inspect `PackageReference` entries plus any
     `Directory.Packages.props` or lock file that applies.
2. Inspect exact local material when available:
   - For Unity packages, locate the matching package under
     `Library/PackageCache` by its `package.json` name and version. Check its
     `Documentation~`, README, changelog, XML documentation, and source as
     relevant.
   - Use `rg` to locate a symbol or topic; do not read a package tree wholesale.
   - Treat source as behavioral evidence, not a substitute for documented API
     guarantees.
3. Consult authoritative external documentation when local material is absent or
   insufficient:
   - Unity APIs: prefer the versioned Unity Manual, Scripting API, and official
     package documentation on `docs.unity3d.com`.
   - Microsoft/.NET APIs: prefer Microsoft Learn and the owning official
     repository.
   - Other libraries: prefer the maintainer's documentation site, release/tag
     documentation, and official repository.
   - Browse when current external documentation is needed. Match the installed
     version; if only a nearby version is available, identify the mismatch before
     relying on it.
4. Optionally use the Context7 CLI for focused third-party-library discovery and
   examples:
   - First check whether `ctx7` is already available (`Get-Command ctx7` in
     PowerShell or `command -v ctx7` in POSIX shells).
   - Resolve an uncertain library ID with
     `ctx7 library <library> "<narrow question>"`.
   - Query with `ctx7 docs <library-id>@<version> "<single concept>"` when a
     matching version is available. Omit the version only when the project is
     intentionally tracking the library's current branch.
   - Do not install or download `ctx7` through `npx` merely to complete a lookup
     unless the user authorizes that installation/download. Fall back to official
     web documentation when it is unavailable.
   - Do not send proprietary code, repository content, secrets, tokens, personal
     data, or issue text to Context7. Queries should contain only public library
     names, versions, symbols, and generic API questions.

## Applying the result

- Reconcile vendor documentation with the relevant project rule in
  `.claude/rules`; vendor docs explain the API, while project rules decide how
  GlobalStrategy uses it.
- Prefer documentation for the exact installed version over newer examples.
- Cite or link the authoritative page used when reporting an answer or a
  version-sensitive implementation choice. If Context7 found the material,
  prefer its original maintainer source link over citing Context7 as the
  authority.
- State any unresolved version mismatch or inference explicitly.
- Documentation lookup does not by itself authorize code changes, dependency
  upgrades, package installation, or edits outside the user's requested scope.
