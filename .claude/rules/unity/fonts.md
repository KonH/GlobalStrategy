---
paths:
  - "Assets/UI/Fonts/**"
---

# Font assets

Unity dirties `Assets/UI/Fonts/*.asset` (dynamic SDF atlas data) during Editor
use and Play mode. Those files are **not** part of normal feature work.

**Always discard local font `.asset` changes** unless the user explicitly asked
to keep or commit a font-asset edit (new fallback, atlas settings, adding a
font).

Reset with (project root, no `cd`):

```
git restore --worktree --staged -- "Assets/UI/Fonts/*.asset"
```

Do this before committing, before merging, and whenever `git status` shows
those files and the user did not ask for a font change. Do not "fix" atlas
noise by editing the YAML; restore from HEAD.
