# Temporary Scripts

When writing one-off scripts for data inspection, analysis, or code generation:

- Place them under `.tmp/` in the project root (e.g. `.tmp/analyze.py`)
- Run the script, then delete it — as separate commands, never with `&&`
- Never use `cd` — the shell already starts in the project root
- `.tmp/` is gitignored — never commit files from it

Example:
```bash
python3 .tmp/analyze.py
rm .tmp/analyze.py
```
