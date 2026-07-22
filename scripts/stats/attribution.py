"""Attributes a stage/sub-stage segment to a Docs/Specs/<dir>/ spec directory.

Per the spec's non-goal of finer splitting: a segment touching more than one spec
dir still maps to exactly one (first match wins). Sessions with neither a
Docs/Specs/<dir>/ write nor a matching ralph/<spec_id> branch produce no attribution.
"""

import re
from pathlib import Path

SPEC_DIR_RE = re.compile(r"Docs[/\\]Specs[/\\]([^/\\]+)[/\\]")
RALPH_BRANCH_RE = re.compile(r"^ralph/(.+)$")


def attribute_segment(write_paths, git_branch, repo_root):
    for path in write_paths:
        match = SPEC_DIR_RE.search(path)
        if match and (Path(repo_root) / "Docs" / "Specs" / match.group(1)).is_dir():
            return match.group(1)

    if git_branch:
        match = RALPH_BRANCH_RE.match(git_branch)
        if match:
            spec_id = match.group(1)
            if (Path(repo_root) / "Docs" / "Specs" / spec_id).is_dir():
                return spec_id

    return None
