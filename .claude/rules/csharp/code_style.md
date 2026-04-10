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
