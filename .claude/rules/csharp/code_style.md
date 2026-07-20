# C# Code Style

- Indentation: tabs (not spaces)
- Opening brace `{` on the same line as the declaration
- Private members prefixed with `_` (e.g. `_health`, `_speed`)
- Do not write explicit access modifiers where the default is already correct (e.g. omit `private` on private members)
- Serialized private fields use `[SerializeField]` inline on the same line as the field declaration, not on a separate line:
  ```csharp
  [SerializeField] int _speed = 0;
  ```
- Always use braces `{}` for control flow bodies (`if`, `else`, `foreach`, `for`, `while`), even for single-line bodies:
  ```csharp
  if (x == null) {
      return;
  }

  foreach (var item in list) {
      Process(item);
  }
  ```
- Fail fast with descriptive, contextual error messages rather than continuing on bad state
- Never silently swallow an exception — at minimum log it at error level

## Prefer `public` over `InternalsVisibleTo` for test access

When a `Game.Tests` test needs to call a class/method that's currently `internal` only
because nothing outside its own assembly needs it *yet*, make the class and that member
`public` instead of adding an `[assembly: InternalsVisibleTo("Game.Tests")]` escape hatch
in an `AssemblyInfo.cs`. `InternalsVisibleTo` grants the test assembly access to
*everything* internal, invisibly, forever — it decays into a blanket bypass of the
access-modifier boundary rather than a deliberate API decision. A `public` member is
visible in the type's own file, doesn't require readers to know a separate
`AssemblyInfo.cs` exists, and if it turns out other assemblies want it too, no further
change is needed.

If an existing `AssemblyInfo.cs` only exists for `InternalsVisibleTo`, grep the target
assembly's test project for which specific `internal` members it actually calls, make
just those (and their containing types, if the type itself is `internal`/default-access)
`public`, then delete the `AssemblyInfo.cs` file entirely. Example: `Game.Main`'s
`VisualStateConverter` (constructor + `Update`/`UpdateCountryScore`/`UpdateLeaderboards`)
and `Game.Configs.Loader`'s `Program.ApplyPreservedFields`/`ProvinceProcessor.Process`
(plus their containing `static class`es) were flipped to `public` this way, removing
both assemblies' `AssemblyInfo.cs`.

This does not apply to genuine encapsulation boundaries (e.g. hiding a helper that would
be actively misleading or unsafe to call from outside its owning system) — those should
stay `internal` and get exercised only through the assembly's public entry points.
