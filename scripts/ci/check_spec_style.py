"""Lint Docs/Specs/*/spec.md for implementation detail that belongs in plan.md.

A spec captures intent. Anything technical -- class and method names, config
keys, file paths, tech-stack names -- belongs in the plan's Technical Mapping
section instead.

Usage:
    python scripts/ci/check_spec_style.py                 # lint every spec
    python scripts/ci/check_spec_style.py <spec-folder>   # lint one spec

Exit code 1 if any violation is found.
"""

import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SPECS_DIR = Path("Docs/Specs")

REQUIRED_SECTIONS = ("Feature Intent", "Acceptance Criteria", "Success Criteria", "Out of Scope")
FORBIDDEN_SECTIONS = ("Tech Notes", "Design Notes", "Technical Mapping")

TECH_STACK = (
    "Unity",
    "VContainer",
    "UI Toolkit",
    "UIToolkit",
    "UXML",
    "USS",
    "ECS",
    "asmdef",
    "MonoBehaviour",
    "Blazor",
    "WebGL",
    "JSON",
    "DLL",
    "C#",
)

RULES = (
    (
        "path",
        re.compile(r"(?<![\w/])(Assets|src|Docs|scripts)/[\w./*-]+"),
        "file path -- move to the plan's Technical Mapping",
    ),
    (
        "filename",
        re.compile(r"[\w.-]+\.(cs|json|uxml|uss|unity|prefab|asset|asmdef|dll)\b"),
        "file name -- move to the plan's Technical Mapping",
    ),
    (
        "identifier",
        re.compile(r"`[^`]*\b(?:[A-Z][a-z0-9]+){2,}\b[^`]*`"),
        "code identifier in backticks -- describe the behaviour instead",
    ),
    (
        "member",
        re.compile(r"`[^`]*\.[A-Za-z_]\w*(\(\))?[^`]*`"),
        "member/method reference -- describe the behaviour instead",
    ),
    (
        "config-key",
        re.compile(r"`[a-z][a-zA-Z0-9]*_[a-z][\w]*`|`[a-z]+[A-Z]\w*`"),
        "config key or camelCase field -- move to the plan's Technical Mapping",
    ),
    (
        "tech-stack",
        re.compile(r"\b(" + "|".join(re.escape(t) for t in TECH_STACK) + r")\b"),
        "tech-stack name -- the spec stays technology-agnostic",
    ),
)


def lint(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    problems: list[str] = []

    headings = re.findall(r"^##\s+(.+?)\s*$", text, re.MULTILINE)
    for forbidden in FORBIDDEN_SECTIONS:
        if any(h.startswith(forbidden) for h in headings):
            problems.append(f"  section: '## {forbidden}' is not allowed in a spec -- it belongs in plan.md")
    for required in REQUIRED_SECTIONS:
        if not any(h.startswith(required) for h in headings):
            problems.append(f"  section: missing required '## {required}'")

    in_fence = False
    for number, line in enumerate(text.splitlines(), start=1):
        if line.lstrip().startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence or line.startswith("#"):
            continue
        for name, pattern, hint in RULES:
            match = pattern.search(line)
            if match:
                problems.append(f"  {path.name}:{number} [{name}] {match.group(0)!r} -- {hint}")
                break

    return problems


def main(argv: list[str]) -> int:
    if len(argv) > 1:
        specs = [SPECS_DIR / argv[1] / "spec.md"]
    else:
        specs = sorted(SPECS_DIR.glob("*/spec.md"))

    failed = 0
    for spec in specs:
        if not spec.exists():
            print(f"{spec}: not found")
            return 1
        problems = lint(spec)
        if problems:
            failed += 1
            print(f"{spec.parent.name}:")
            for problem in problems:
                print(problem)

    if failed:
        print(f"\n{failed} of {len(specs)} spec(s) contain implementation detail.")
        return 1

    print(f"{len(specs)} spec(s) clean.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
