# Temporary Scripts

## Python scripts

Use a single reusable file `.tmp/run.py` with the PS1 wrapper:
1. Write the script to `.tmp/run.py` (overwrite each time)
2. Run it: PowerShell `& ".claude\run.ps1"`
3. Delete it: PowerShell `Remove-Item .tmp\run.py`

Run and delete as separate PowerShell tool calls.

The wrapper `.claude/run.ps1` invokes `.venv\Scripts\python.exe` on `.tmp\run.py`.
The venv is at `.venv/` in the project root.
Created with: `C:\Users\KonH\AppData\Local\Microsoft\WindowsApps\python.exe -m venv .venv`
To install packages: `.venv\Scripts\pip.exe install <package>`

## Other scripts

- Place under `.tmp/` (e.g. `.tmp/run.sh`)
- Run then delete as separate commands
- Never use `cd` — shell starts in project root
- `.tmp/` is gitignored — never commit files from it
