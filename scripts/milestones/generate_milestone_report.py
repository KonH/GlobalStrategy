"""Generates a milestone-completion report under Docs/Milestones/<major>_<slug>/.

Pure stdlib, no LLM calls - mirrors the scripts/stats/ convention (see
Docs/Specs/26_07_22_17_spec-dev-stats/). Produces three files:

  summary.md     - version/name header, date range, Dev Notes insights
  stats_code.md  - LoC per tracked extension, compared to the previous milestone
  stats_dev.md   - aggregated Docs/Specs/*/usage.csv data for specs in range,
                   compared to the previous milestone

Each generated stats_*.md carries a hidden HTML-comment JSON block
(`<!-- milestone-stats-code: {...} -->` / `<!-- milestone-stats-dev: {...} -->`)
so the *next* milestone run can diff against exact prior numbers without
re-parsing markdown tables. `summary.md` carries a similar
`<!-- milestone-meta: {...} -->` block recording its own start/end dates so
future runs can find "the previous milestone's end date" precisely.
"""

import argparse
import calendar
import csv
import json
import re
import subprocess
import sys
from collections import defaultdict
from datetime import date, datetime, timedelta
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts" / "stats"))

from version_info import read_bundle_version  # noqa: E402

MAIN_MENU_SCENE = REPO_ROOT / "Assets" / "Scenes" / "MainMenu.unity"
MILESTONES_DIR = REPO_ROOT / "Docs" / "Milestones"
SPECS_DIR = REPO_ROOT / "Docs" / "Specs"

# Requested extensions. ".scene" has no literal meaning in Unity - scene files
# are ".unity" - so it is mapped there and labelled accordingly in the table.
CODE_EXTENSIONS = [
    ("cs", "*.cs"),
    ("py", "*.py"),
    ("sh", "*.sh"),
    ("ps1", "*.ps1"),
    ("prefab", "*.prefab"),
    ("scene (.unity)", "*.unity"),
    ("asset", "*.asset"),
    ("json", "*.json"),
    ("md", "*.md"),
]

SPEC_DIR_RE = re.compile(r"^(\d{2})_(\d{2})_(\d{2})_(\d{2})_")
USER_INPUT_SUFFIX_RE = re.compile(r"_user_input_\d+$")
META_COMMENT_RE = re.compile(r"<!--\s*milestone-meta:\s*(\{.*?\})\s*-->")
STATS_CODE_COMMENT_RE = re.compile(r"<!--\s*milestone-stats-code:\s*(\{.*?\})\s*-->")
STATS_DEV_COMMENT_RE = re.compile(r"<!--\s*milestone-stats-dev:\s*(\{.*?\})\s*-->")


def slugify(name):
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", name.strip().lower()).strip("-")
    return slug or "unnamed"


def read_version_name():
    if not MAIN_MENU_SCENE.exists():
        return "Unnamed"
    text = MAIN_MENU_SCENE.read_text(encoding="utf-8")
    match = re.search(r"_versionName:\s*(.*)", text)
    if not match:
        return "Unnamed"
    value = match.group(1).strip()
    return value or "Unnamed"


def git_first_commit_date():
    out = subprocess.run(
        ["git", "log", "--reverse", "--date=short", "--format=%ad"],
        cwd=REPO_ROOT, capture_output=True, text=True, check=True,
    ).stdout.strip().splitlines()
    if not out:
        raise RuntimeError("Repository has no commits")
    return datetime.strptime(out[0], "%Y-%m-%d").date()


def git_tracked_files(pattern):
    out = subprocess.run(
        ["git", "ls-files", "--", pattern],
        cwd=REPO_ROOT, capture_output=True, text=True, check=True,
    ).stdout.splitlines()
    return [line for line in out if line]


def count_lines(path):
    try:
        data = (REPO_ROOT / path).read_bytes()
    except OSError:
        return 0
    if not data:
        return 0
    lines = data.count(b"\n")
    if not data.endswith(b"\n"):
        lines += 1
    return lines


def find_existing_milestones(exclude_dir=None):
    """Returns a list of parsed milestone-meta dicts for every existing
    Docs/Milestones/*/summary.md, excluding `exclude_dir` if given."""
    results = []
    if not MILESTONES_DIR.exists():
        return results
    for summary_path in sorted(MILESTONES_DIR.glob("*/summary.md")):
        if exclude_dir is not None and summary_path.parent.resolve() == exclude_dir.resolve():
            continue
        text = summary_path.read_text(encoding="utf-8")
        match = META_COMMENT_RE.search(text)
        if not match:
            continue
        meta = json.loads(match.group(1))
        meta["_dir"] = summary_path.parent
        results.append(meta)
    return results


def previous_milestone(existing_meta):
    if not existing_meta:
        return None
    return max(existing_meta, key=lambda m: m["end_date"])


def load_prev_stats_code(prev_meta):
    if prev_meta is None:
        return None
    path = prev_meta["_dir"] / "stats_code.md"
    if not path.exists():
        return None
    match = STATS_CODE_COMMENT_RE.search(path.read_text(encoding="utf-8"))
    return json.loads(match.group(1)) if match else None


def load_prev_stats_dev(prev_meta):
    if prev_meta is None:
        return None
    path = prev_meta["_dir"] / "stats_dev.md"
    if not path.exists():
        return None
    match = STATS_DEV_COMMENT_RE.search(path.read_text(encoding="utf-8"))
    return json.loads(match.group(1)) if match else None


def pct_change(old, new):
    if old in (None, 0):
        return None
    return (new - old) / old * 100.0


def fmt_pct(value):
    if value is None:
        return "n/a"
    sign = "+" if value >= 0 else ""
    return f"{sign}{value:.1f}%"


def fmt_delta(value):
    sign = "+" if value >= 0 else ""
    return f"{sign}{value}"


def calendar_diff(start, end):
    """Calendar-based (years, months, days) elapsed from `start` to `end`
    (end - start, non-inclusive) - e.g. 2026-04-02 -> 2026-08-13 is (0, 4, 11):
    4 full months (Apr 2 -> Aug 2) plus 11 leftover days (Aug 2 -> Aug 13)."""
    years = end.year - start.year
    months = end.month - start.month
    days = end.day - start.day
    if days < 0:
        months -= 1
        prev_month = end.month - 1 or 12
        prev_year = end.year if end.month > 1 else end.year - 1
        days += calendar.monthrange(prev_year, prev_month)[1]
    if months < 0:
        years -= 1
        months += 12
    return years, months, days


def humanize_duration(start_date, end_date):
    """Human-readable duration, e.g. "4 months, 11 days (134 days total)" -
    returned pre-parenthesized as a single unit, ready to drop into a header
    that already wraps it in its own outer parens (caller must not add more).
    Calendar parts (years/months/days) come from `calendar_diff`; the exact
    inclusive day count (matching this file's other date-range semantics) is
    kept alongside it since "4 months, 11 days" alone loses precision."""
    total_days = (end_date - start_date).days + 1
    years, months, days = calendar_diff(start_date, end_date)
    parts = []
    if years:
        parts.append(f"{years} year{'s' if years != 1 else ''}")
    if months:
        parts.append(f"{months} month{'s' if months != 1 else ''}")
    if days or not parts:
        parts.append(f"{days} day{'s' if days != 1 else ''}")
    human = ", ".join(parts)
    return f"{human} ({total_days} day{'s' if total_days != 1 else ''} total)"


# --- Code stats -------------------------------------------------------------

def collect_code_stats():
    stats = {}
    for label, pattern in CODE_EXTENSIONS:
        files = git_tracked_files(pattern)
        loc = sum(count_lines(f) for f in files)
        stats[label] = {"files": len(files), "loc": loc}
    return stats


def render_stats_code(stats, prev_stats):
    lines = []
    lines.append("# Code Stats")
    lines.append("")
    if prev_stats is None:
        lines.append("_No previous milestone to compare against - this is the first milestone._")
    lines.append("")
    lines.append("| Extension | Files | LoC | Δ Files | Δ LoC | % LoC change |")
    lines.append("|---|---:|---:|---:|---:|---:|")
    total_files = 0
    total_loc = 0
    insights = []
    for label, _ in CODE_EXTENSIONS:
        cur = stats[label]
        total_files += cur["files"]
        total_loc += cur["loc"]
        if prev_stats and label in prev_stats:
            prev = prev_stats[label]
            d_files = cur["files"] - prev["files"]
            d_loc = cur["loc"] - prev["loc"]
            pct = fmt_pct(pct_change(prev["loc"], cur["loc"]))
            lines.append(
                f"| `.{label}` | {cur['files']} | {cur['loc']} | {fmt_delta(d_files)} | {fmt_delta(d_loc)} | {pct} |"
            )
        else:
            lines.append(f"| `.{label}` | {cur['files']} | {cur['loc']} | n/a | n/a | n/a |")
    lines.append(f"| **Total** | **{total_files}** | **{total_loc}** | | | |")
    lines.append("")

    # Insights. `.cs` stats are mandatory and always lead, since it's the project's
    # actual game-logic language (per Docs/Constitution.md) - the raw top LoC
    # contributor is often a data format like `.json`/`.md`, which is worth noting
    # but shouldn't upstage the code-size figure that matters most.
    cs = stats["cs"]
    insights.append(f"`.cs`: {cs['loc']:,} LoC across {cs['files']} files.")

    biggest = max(CODE_EXTENSIONS, key=lambda e: stats[e[0]]["loc"])[0]
    if biggest == "cs":
        insights.append(f"`.cs` is also the largest tracked extension by LoC, out of {total_files} tracked files ({total_loc:,} LoC total).")
    else:
        insights.append(
            f"Codebase stands at {total_loc:,} LoC across {total_files} tracked files; "
            f"`.{biggest}` is the largest share ({stats[biggest]['loc']:,} LoC)."
        )
    if prev_stats:
        prev_total_loc = sum(v["loc"] for v in prev_stats.values())
        insights.append(
            f"Total LoC changed {fmt_delta(total_loc - prev_total_loc)} "
            f"({fmt_pct(pct_change(prev_total_loc, total_loc))}) since the previous milestone."
        )
    else:
        insights.append("No previous milestone recorded yet, so this snapshot is the new baseline.")

    meta_json = json.dumps(stats, sort_keys=True)
    body = "\n".join(lines)
    body += f"\n<!-- milestone-stats-code: {meta_json} -->\n"
    return body, insights


# --- Dev stats ---------------------------------------------------------------

def spec_dir_date(dir_name):
    match = SPEC_DIR_RE.match(dir_name)
    if not match:
        return None
    yy, mm, dd, _hh = match.groups()
    try:
        return date(2000 + int(yy), int(mm), int(dd))
    except ValueError:
        return None


def specs_in_range(start_date, end_date):
    result = []
    if not SPECS_DIR.exists():
        return result
    for spec_dir in sorted(SPECS_DIR.iterdir()):
        if not spec_dir.is_dir():
            continue
        d = spec_dir_date(spec_dir.name)
        if d is None:
            continue
        if start_date <= d <= end_date:
            result.append(spec_dir)
    return result


def read_usage_rows(spec_dir):
    path = spec_dir / "usage.csv"
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def to_float(value):
    if value in (None, ""):
        return None
    try:
        return float(value)
    except ValueError:
        return None


def to_int(value):
    if value in (None, ""):
        return None
    try:
        return int(float(value))
    except ValueError:
        return None


def base_stage(stage):
    return USER_INPUT_SUFFIX_RE.sub("", stage)


def collect_dev_stats(spec_dirs):
    per_spec = {}
    provider_rows = defaultdict(int)
    model_rows = defaultdict(int)
    stage_costs = defaultdict(list)  # base_stage -> [per-spec total cost]
    stage_totals = defaultdict(float)  # base_stage -> total cost
    total_rows = 0
    total_cost = 0.0
    priced_rows = 0
    total_input = total_cached = total_output = 0
    spec_size_tokens_vals = []
    plan_size_tokens_vals = []
    diff_lines_vals = []
    specs_missing_implement = 0

    for spec_dir in spec_dirs:
        rows = read_usage_rows(spec_dir)
        if not rows:
            continue
        if "implement" not in {base_stage(r.get("stage", "")) for r in rows}:
            specs_missing_implement += 1
        spec_cost = 0.0
        spec_input = spec_cached = spec_output = 0
        spec_providers = set()
        spec_models = set()
        spec_stage_cost = defaultdict(float)
        last_spec_tokens = None
        last_plan_tokens = None
        last_diff_lines = None
        last_end = ""
        for row in rows:
            total_rows += 1
            provider = row.get("provider") or ""
            model = row.get("model") or ""
            if provider:
                provider_rows[provider] += 1
                spec_providers.add(provider)
            if model:
                model_rows[model] += 1
                spec_models.add(model)

            cost = to_float(row.get("cost_usd"))
            if cost is not None:
                total_cost += cost
                priced_rows += 1
                spec_cost += cost
                stage_totals[base_stage(row.get("stage", ""))] += cost
                spec_stage_cost[base_stage(row.get("stage", ""))] += cost

            inp = to_int(row.get("input_tokens")) or 0
            cached = to_int(row.get("cached_input_tokens")) or 0
            out = to_int(row.get("output_tokens")) or 0
            total_input += inp
            total_cached += cached
            total_output += out
            spec_input += inp
            spec_cached += cached
            spec_output += out

            end = row.get("end", "")
            if end >= last_end:
                last_end = end
                spec_tokens = to_int(row.get("spec_size_tokens"))
                plan_tokens = to_int(row.get("plan_size_tokens"))
                diff_lines = to_int(row.get("diff_lines"))
                if spec_tokens is not None:
                    last_spec_tokens = spec_tokens
                if plan_tokens is not None:
                    last_plan_tokens = plan_tokens
                if diff_lines is not None:
                    last_diff_lines = diff_lines

        for stage_name, cost in spec_stage_cost.items():
            stage_costs[stage_name].append(cost)

        if last_spec_tokens is not None:
            spec_size_tokens_vals.append(last_spec_tokens)
        if last_plan_tokens is not None:
            plan_size_tokens_vals.append(last_plan_tokens)
        if last_diff_lines is not None:
            diff_lines_vals.append(last_diff_lines)

        per_spec[spec_dir.name] = {
            "rows": len(rows),
            "providers": sorted(spec_providers),
            "models": sorted(spec_models),
            "cost_usd": round(spec_cost, 4),
            "input_tokens": spec_input,
            "cached_input_tokens": spec_cached,
            "output_tokens": spec_output,
            "spec_size_tokens": last_spec_tokens,
            "plan_size_tokens": last_plan_tokens,
            "diff_lines": last_diff_lines,
        }

    def summary_stats(values):
        if not values:
            return None
        return {
            "min": min(values), "max": max(values),
            "avg": sum(values) / len(values), "n": len(values),
        }

    return {
        "specs_count": len(per_spec),
        "total_rows": total_rows,
        "priced_rows": priced_rows,
        "total_cost_usd": round(total_cost, 4),
        "total_input_tokens": total_input,
        "total_cached_input_tokens": total_cached,
        "total_output_tokens": total_output,
        "provider_rows": dict(provider_rows),
        "model_rows": dict(model_rows),
        "stage_total_cost_usd": {k: round(v, 4) for k, v in stage_totals.items()},
        "stage_avg_cost_usd": {
            k: round(sum(v) / len(v), 4) for k, v in stage_costs.items() if v
        },
        "spec_size_tokens": summary_stats(spec_size_tokens_vals),
        "plan_size_tokens": summary_stats(plan_size_tokens_vals),
        "diff_lines": summary_stats(diff_lines_vals),
        "specs_missing_implement": specs_missing_implement,
        "per_spec": per_spec,
    }


def render_stats_dev(stats, prev_stats, start_date, end_date):
    lines = []
    lines.append("# Dev Stats")
    lines.append("")
    lines.append(
        f"Specs with `Docs/Specs/<dir>/` timestamp between {start_date.isoformat()} "
        f"and {end_date.isoformat()} (inclusive), aggregated from each spec's `usage.csv`."
    )
    lines.append("")
    if stats["specs_missing_implement"]:
        lines.append(
            f"**Coverage gap:** {stats['specs_missing_implement']}/{stats['specs_count']} specs in range have "
            "**no `implement`-stage row at all** (the backfill's multi-PR implement-detection heuristic produced "
            "false positives and was disabled, so those rows were logged as unverified suggestions instead of "
            "written - see the backfill commits' own messages). Implementation is where most real coding work - "
            "and most non-Claude-tool usage - happens, so every provider/model/cost figure below is skewed toward "
            "whichever tool authored `spec.md`/`plan.md` and **undercounts actual tool usage, especially for "
            "cursor/codex.** Treat provider/model shares and cost totals as directional, not exact, until those "
            "rows are backfilled."
        )
        lines.append("")
    if prev_stats is None:
        lines.append("_No previous milestone to compare against - this is the first milestone._")
        lines.append("")

    # Per-spec table
    lines.append("## Per-Spec Data")
    lines.append("")
    lines.append("| Spec | Rows | Providers | Models | Cost ($) | Input tok | Cached tok | Output tok | Spec tok | Plan tok | Diff lines |")
    lines.append("|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|")
    for spec_id, row in stats["per_spec"].items():
        lines.append(
            "| {spec} | {rows} | {providers} | {models} | {cost} | {inp} | {cached} | {out} | {spec_tok} | {plan_tok} | {diff} |".format(
                spec=spec_id,
                rows=row["rows"],
                providers=", ".join(row["providers"]) or "n/a",
                models=", ".join(row["models"]) or "n/a",
                cost=f"{row['cost_usd']:.4f}" if row["cost_usd"] else "n/a",
                inp=row["input_tokens"],
                cached=row["cached_input_tokens"],
                out=row["output_tokens"],
                spec_tok=row["spec_size_tokens"] if row["spec_size_tokens"] is not None else "n/a",
                plan_tok=row["plan_size_tokens"] if row["plan_size_tokens"] is not None else "n/a",
                diff=row["diff_lines"] if row["diff_lines"] is not None else "n/a",
            )
        )
    lines.append("")

    # Meta stats
    lines.append("## Meta Stats")
    lines.append("")

    def prev(key, *path):
        if prev_stats is None:
            return None
        node = prev_stats
        for p in (key, *path):
            if node is None or p not in node:
                return None
            node = node[p]
        return node

    def with_prev(current, prev_value, fmt=str):
        if prev_value is None:
            return f"{fmt(current)} (no previous milestone)"
        return (
            f"{fmt(current)} (Δ {fmt_delta(round(current - prev_value, 4))}, "
            f"{fmt_pct(pct_change(prev_value, current))})"
        )

    specs_count = stats["specs_count"]
    lines.append(f"- **Specs count:** {with_prev(specs_count, prev('specs_count'))}")
    lines.append(
        f"- **Specs missing an `implement` row:** {with_prev(stats['specs_missing_implement'], prev('specs_missing_implement'))} "
        "(see Coverage gap note above)"
    )
    lines.append(f"- **Total rows:** {stats['total_rows']} ({stats['priced_rows']} carry `cost_usd`)")
    lines.append(f"- **Total cost (priced rows only):** {with_prev(stats['total_cost_usd'], prev('total_cost_usd'), lambda v: f'${v:.2f}')}")
    lines.append(
        "- **Total tokens:** input {}, cached {}, output {}".format(
            with_prev(stats["total_input_tokens"], prev("total_input_tokens"), lambda v: f"{v:,}"),
            with_prev(stats["total_cached_input_tokens"], prev("total_cached_input_tokens"), lambda v: f"{v:,}"),
            with_prev(stats["total_output_tokens"], prev("total_output_tokens"), lambda v: f"{v:,}"),
        )
    )

    total_rows = stats["total_rows"] or 1
    lines.append("- **Provider share (by row count):**")
    for provider, count in sorted(stats["provider_rows"].items(), key=lambda kv: -kv[1]):
        lines.append(f"  - {provider}: {count} ({count / total_rows * 100:.1f}%)")
    lines.append("- **Model share (by row count):**")
    for model, count in sorted(stats["model_rows"].items(), key=lambda kv: -kv[1]):
        lines.append(f"  - {model}: {count} ({count / total_rows * 100:.1f}%)")

    def render_summary_stat(name, node, prev_node):
        if node is None:
            lines.append(f"- **{name}:** n/a")
            return
        avg_str = with_prev(node["avg"], prev_node["avg"] if prev_node else None, lambda v: f"{v:.1f}")
        lines.append(
            f"- **{name}:** min {node['min']}, max {node['max']}, avg {avg_str} (n={node['n']})"
        )

    render_summary_stat("Spec size (tokens)", stats["spec_size_tokens"], prev("spec_size_tokens"))
    render_summary_stat("Plan size (tokens)", stats["plan_size_tokens"], prev("plan_size_tokens"))
    render_summary_stat("Diff lines (final, per spec)", stats["diff_lines"], prev("diff_lines"))

    lines.append("- **Avg cost per stage (priced rows only):**")
    for stage_name, avg_cost in sorted(stats["stage_avg_cost_usd"].items()):
        prev_avg = prev("stage_avg_cost_usd", stage_name)
        avg_str = with_prev(avg_cost, prev_avg, lambda v: f"${v:.4f}")
        lines.append(f"  - {stage_name}: {avg_str}/spec (total ${stats['stage_total_cost_usd'][stage_name]:.2f})")
    lines.append("")

    insights = []
    if stats["specs_missing_implement"]:
        insights.append(
            f"**Coverage gap:** {stats['specs_missing_implement']}/{specs_count} specs have no `implement`-stage "
            "row (unbackfilled), so provider/model/cost figures below undercount real tool usage - see stats_dev.md."
        )
    if specs_count:
        insights.append(f"{specs_count} specs shipped in range, {stats['total_rows']} usage rows total.")
    if stats["priced_rows"]:
        insights.append(
            f"${stats['total_cost_usd']:.2f} total priced cost across {stats['priced_rows']}/{stats['total_rows']} rows "
            f"with pricing data (rest are backfilled/unpriced)."
        )
    top_provider = max(stats["provider_rows"].items(), key=lambda kv: kv[1], default=None)
    if top_provider:
        insights.append(f"Primary provider: {top_provider[0]} ({top_provider[1] / total_rows * 100:.1f}% of rows).")
    if prev_stats:
        prev_cost = prev_stats.get("total_cost_usd")
        if prev_cost is not None:
            insights.append(
                f"Total priced cost changed {fmt_pct(pct_change(prev_cost, stats['total_cost_usd']))} "
                f"since the previous milestone."
            )
    else:
        insights.append("No previous milestone recorded yet, so this snapshot is the new baseline.")

    meta_json = json.dumps({k: v for k, v in stats.items() if k != "per_spec"}, sort_keys=True)
    body = "\n".join(lines)
    body += f"\n<!-- milestone-stats-dev: {meta_json} -->\n"
    return body, insights


def render_summary(major, name, start_date, end_date, code_insights, dev_insights):
    duration = humanize_duration(start_date, end_date)
    lines = []
    lines.append(f"# {major}. {name}")
    lines.append(f"## {start_date.isoformat()} - {end_date.isoformat()} - {duration}")
    lines.append("")
    lines.append("See [`stats_code.md`](stats_code.md) and [`stats_dev.md`](stats_dev.md) for full data.")
    lines.append("")
    lines.append("## Dev Notes")
    lines.append("")
    for insight in code_insights + dev_insights:
        lines.append(f"- {insight}")
    lines.append("")
    meta = {"major": major, "name": name, "start_date": start_date.isoformat(), "end_date": end_date.isoformat()}
    body = "\n".join(lines)
    body += f"\n<!-- milestone-meta: {json.dumps(meta, sort_keys=True)} -->\n"
    return body


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--major", help="Override the major version (default: bundleVersion's X in X.YYY)")
    parser.add_argument("--name", help="Override the version name (default: _versionName in MainMenu.unity)")
    parser.add_argument("--start-date", help="Override start date YYYY-MM-DD (default: day after previous milestone's end, or first commit date)")
    parser.add_argument("--end-date", help="Override end date YYYY-MM-DD (default: today)")
    parser.add_argument("--out", help="Override output directory (default: Docs/Milestones/<major>_<slug>)")
    args = parser.parse_args()

    major = args.major or read_bundle_version().split(".")[0]
    name = args.name or read_version_name()
    slug = slugify(name)

    out_dir = Path(args.out) if args.out else MILESTONES_DIR / f"{major}_{slug}"
    out_dir = out_dir if out_dir.is_absolute() else REPO_ROOT / out_dir

    existing = find_existing_milestones(exclude_dir=out_dir)
    prev_meta = previous_milestone(existing)

    if args.end_date:
        end_date = datetime.strptime(args.end_date, "%Y-%m-%d").date()
    else:
        end_date = date.today()

    if args.start_date:
        start_date = datetime.strptime(args.start_date, "%Y-%m-%d").date()
    elif prev_meta:
        start_date = datetime.strptime(prev_meta["end_date"], "%Y-%m-%d").date() + timedelta(days=1)
    else:
        start_date = git_first_commit_date()

    code_stats = collect_code_stats()
    prev_code_stats = load_prev_stats_code(prev_meta)
    stats_code_body, code_insights = render_stats_code(code_stats, prev_code_stats)

    spec_dirs = specs_in_range(start_date, end_date)
    dev_stats = collect_dev_stats(spec_dirs)
    prev_dev_stats = load_prev_stats_dev(prev_meta)
    stats_dev_body, dev_insights = render_stats_dev(dev_stats, prev_dev_stats, start_date, end_date)

    summary_body = render_summary(major, name, start_date, end_date, code_insights, dev_insights)

    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "summary.md").write_text(summary_body, encoding="utf-8")
    (out_dir / "stats_code.md").write_text(stats_code_body, encoding="utf-8")
    (out_dir / "stats_dev.md").write_text(stats_dev_body, encoding="utf-8")

    print(f"Wrote milestone report to {out_dir.relative_to(REPO_ROOT)}")
    print(f"  major={major} name={name!r} range={start_date}..{end_date}")
    print(f"  specs in range: {len(spec_dirs)}")


if __name__ == "__main__":
    main()
