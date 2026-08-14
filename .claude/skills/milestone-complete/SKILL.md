---
name: milestone-complete
description: Generate a milestone-completion report (summary.md, stats_code.md, stats_dev.md) under Docs/Milestones/<major>_<version-name>/, comparing code LoC and Docs/Specs/*/usage.csv dev stats against the previous milestone, then (with the user's sign-off) write Release Notes, draft a tech blog post and a player-facing announcement, cut a GitHub release + releases/<version> branch, and roll the project version over to the next milestone. Tracks progress in the milestone dir's checklist.md so a run can be resumed. Load when the user asks to close out, wrap up, or report on a project milestone/version.
---

# Milestone Complete

Produces a point-in-time report of everything shipped in the current major-version
milestone: codebase size (LoC per tracked extension) and development cost/effort
(aggregated from every spec's `usage.csv`, see `Docs/Specs/26_07_22_17_spec-dev-stats/`),
each compared against the previous milestone if one exists. It then walks the user
through actually closing the milestone out: Release Notes, a tech blog post, a
player-facing announcement, a GitHub release, a `releases/<version>` branch, and the
version bump that starts the next milestone.

Per `.claude/commands/commit.md`, only a human decides when the milestone actually
turns over — this skill is that decision moment. It never bumps the major version or
renames `_versionName` without the user explicitly confirming the next milestone's
name first (step 15). Re-running the report before that confirmation simply
regenerates the current milestone's report with an updated end date.

This is a long, multi-session flow. **Step 0 makes it resumable** — always do that
first, and always check it first if the user asks to resume a milestone close-out
already in progress.

## Step 0: Checklist (before anything else)

1. Resolve the milestone identity the same way the report script does: major version
   (`bundleVersion`'s `X` in `X.YYY`, from `ProjectSettings/ProjectSettings.asset`) and
   version name (`_versionName` on `MainMenuDocument` in `Assets/Scenes/MainMenu.unity`),
   slugified. This gives the output dir `Docs/Milestones/<major>_<slug>/`.
2. If `Docs/Milestones/<major>_<slug>/checklist.md` already exists, this is a
   **resume** — read it, find the first unchecked step, tell the user where the last
   run left off, and continue from there instead of restarting. Don't re-run
   already-checked steps (e.g. don't regenerate a report that's already been reviewed,
   don't re-cut a release that's already checked off).
3. Otherwise, create the directory and write `checklist.md` with every step below
   (1 through 17) as an unchecked box, e.g.:

   ```markdown
   # Milestone Close-Out Checklist — <major>. <name>

   - [ ] 1. Generate report (summary.md, stats_code.md, stats_dev.md)
   - [ ] 2. Present report to user
   - [ ] 3. Decide: continue to close-out now, or stop after the report
   - [ ] 4. Review commit history in range
   - [ ] 5. Propose release-note bullets; get user's list
   - [ ] 6. Write ## Release Notes into summary.md
   - [ ] 7. Propose tech_post.md themes; get user's choice
   - [ ] 8. Propose players_post.txt themes; get user's choice
   - [ ] 9. Draft tech_post.md and players_post.txt
   - [ ] 10. Get user approval on both drafts
   - [ ] 11. Commit milestone docs (this repo) + publish tech_post.md to the site
   - [ ] 12. Checkpoint: confirm release plan
   - [ ] 13. Cut the GitHub release + releases/<version> branch
   - [ ] 14. Ask for next milestone's name
   - [ ] 15. Checkpoint: confirm version transition
   - [ ] 16. Bump version, commit
   - [ ] 17. Report final summary
   ```

   `Edit` the box to `[x]` immediately after finishing each step — don't batch this at
   the end. A crashed/interrupted session should still leave an accurate checklist.

## Step 1–3: Generate the report

Runs `scripts/milestones/generate_milestone_report.py`, which:

- Resolves the date range: `end_date` = today; `start_date` = the day after the
  previous milestone's `end_date` (read from its `summary.md`), or the repo's first
  commit date if no previous milestone exists.
- Writes `stats_code.md` — LoC + file count for `.cs`, `.py`, `.sh`, `.ps1`,
  `.prefab`, `.unity` (Unity's actual scene extension), `.asset`, `.json`, `.md`
  (via `git ls-files`, so untracked/ignored files never skew the count), diffed
  against the previous milestone's numbers.
- Writes `stats_dev.md` — every spec whose `Docs/Specs/<YY_MM_DD_HH>_<name>/`
  timestamp falls in the date range, its `usage.csv` rows aggregated into one table,
  plus meta stats (specs count, provider/model share, min/max/avg spec/plan size in
  tokens, avg cost per stage, total cost/tokens), diffed against the previous
  milestone's numbers.
- Writes `summary.md` — `# <major>. <name>` header, the date range with duration,
  and a `## Dev Notes` section with short insight bullets pulled from both stats
  files.

Each generated file also carries a hidden `<!-- milestone-meta: {...} -->` /
`<!-- milestone-stats-code: {...} -->` / `<!-- milestone-stats-dev: {...} -->`
HTML-comment JSON block — this is how the *next* milestone run finds this one's exact
prior numbers without re-parsing markdown tables, and how later steps find this
milestone's exact `start_date`/`end_date`. Don't strip these comments when editing a
generated file by hand.

1. Run, in a single Bash call (no `cd`, matching this repo's shell rule):

   ```
   python3 scripts/milestones/generate_milestone_report.py
   ```

   Useful overrides (rarely needed — defaults cover the normal case):
   - `--major X` / `--name "..."` — force the milestone identity instead of reading
     it from `ProjectSettings.asset` / `MainMenu.unity`.
   - `--start-date YYYY-MM-DD` / `--end-date YYYY-MM-DD` — force the date range.
   - `--out <path>` — force the output directory.

2. Read the three generated files and present the `summary.md` content (and anything
   notable from the two stats files) to the user. Do not commit them yet — that
   happens, deliberately, as its own approved step (11) further down.
3. Ask the user whether to continue into closing the milestone out now, or stop here
   with just the report. If they stop, the checklist is left mid-way on purpose —
   this is a valid resume point.

## Steps 4–11: Release Notes and the two write-ups

4. **Review what shipped.** Pull `start_date`/`end_date` from the `<!-- milestone-meta:
   {...} -->` comment at the end of `summary.md` (already read in step 2), then review
   the commit history for that range on the current branch:

   ```
   git log --pretty=format:"%h %s" --no-merges --since=<start_date> --until="<end_date> 23:59:59"
   ```

   Read through the subjects (and skim bodies for anything non-obvious) to understand
   what actually shipped — new systems, major fixes, cut features — not just the raw
   list. This review feeds steps 5, 7, and 8 below, so do it once and reuse it.

5. **Propose release-note bullets.** Draft up to 10 candidate bullets for the
   milestone's highlights, most important first, in plain user-facing language (not
   commit-message shorthand). Present them as a numbered list and ask the user to
   confirm, trim, reorder, or add to it — free-form reply, not a fixed menu (e.g.
   "keep 1, 3, 5; drop the rest; add: X").
6. **Write `## Release Notes`.** Once the user has settled the bullet list, `Edit`
   the milestone's `summary.md` to insert a `## Release Notes` section (the settled
   bullets) — insert it *before* the trailing `<!-- milestone-meta: {...} -->` line,
   after the existing `## Dev Notes` section. Do not touch or remove the hidden
   comment.
7. **Propose `tech_post.md` themes.** This is a technical blog post for the personal
   site (`../konh.github.io`, published under `src/content/blog/` — see its
   `EXAMPLE.md` for the exact post format, and skim a couple of real posts already in
   that directory for tone). Drawing on the commit review (step 4) and `stats_dev.md`,
   propose 2–4 possible angles (e.g. "architecture deep-dive on system X",
   "the AI-assisted dev-process story this milestone", "one flagship feature,
   spotlighted end-to-end") and ask the user which theme/accent to run with — and
   whether the milestone's timeline is better shown as a short table or a small inline
   diagram (see step 9's formatting notes). This is a discussion, not a menu; take a
   free-form reply.
8. **Propose `players_post.txt` themes.** Same idea, for a short player-facing
   announcement (Discord/Steam/itch.io-style, not this repo's own docs). Propose 2–4
   angles (e.g. "lead with the headline feature", "narrative/flavor framing",
   "plain patch-notes list") and ask which to run with. Keep this pass light — it's a
   ≤1024-character post, not a spec.
9. **Draft both documents** once themes are chosen, and save them into
   `Docs/Milestones/<major>_<slug>/`:
   - **`tech_post.md`** — already written in the site's exact blog format so it can
     be copied over with only a rename:
     - Line 1: `# <Post Title>`.
     - Line 2 (optional but recommended): `# tags: tag1, tag2, tag3`.
     - Everything after is the Markdown body: tech details in the chosen theme,
       dev/AI usage stats (pull the *key* figures out of `stats_dev.md` — specs
       count, total cost, provider/model share, notable avg-cost-per-stage — as a
       short table or a few sentences, don't dump the whole file), and a timeline of
       milestone highlights.
     - **Diagrams**: the site's blog renderer (`marked` → `v-html`, see
       `generateBlog.ts`/`BlogPostView.vue`) has no Mermaid support — a ```mermaid
       fence would render as inert text, not a picture. If a diagram earns its place,
       author it as **inline `<svg>...</svg>` markup directly in the Markdown body**
       (raw HTML blocks pass through `marked` untouched and `v-html` renders them).
       Load the `artifact-diagramming` skill for diagram-composition know-how (what
       makes a diagram worth including, how to keep it legible), but ignore its
       Artifact-specific CSS token names — style the SVG with *this site's* tokens
       instead, matched from `BlogPostView.vue`'s scoped styles: `var(--text)`,
       `var(--text-muted)`, `var(--accent)`, `var(--border)`, `var(--bg-surface)`,
       `var(--bg-elevated)`. Keep it simple (boxes/arrows/a timeline bar) — this is a
       blog post, not a spec diagram.
   - **`players_post.txt`** — plain text (no title/tags header — this isn't going
     into the site's blog pipeline), gameplay/feature framing per the chosen theme,
     light on tech detail. **Hard limit: 1024 characters.** Count the characters
     before presenting it; trim if over.
10. **Preview, then get approval.** Before asking for sign-off, let the user see
    `tech_post.md` rendered as it will actually appear:
    - Pick the **unique slug filename** now (the filename becomes the URL slug) —
      list the existing files in `../konh.github.io/src/content/blog/` first and pick
      something that doesn't collide, e.g. based on the game's title and milestone
      name (the game's title is `Hidden Council`, per `deploy-unity-play.yml`'s
      default). Copy `tech_post.md` there under that name.
    - In `../konh.github.io`, run `npm run generate_blog` to regenerate
      `src/assets/blog.json` from the new file.
    - Start the dev server in the background: `npm run serve` (this repo's shell
      rule against `&`/backgrounding doesn't apply to the site repo — use the Bash
      tool's own `run_in_background`). Read its startup output for the actual local
      URL/port (defaults to `http://localhost:8080` but confirm from the log).
    - **Just show the link** — `http://localhost:<port>/blog/<slug>` — rather than
      trying to auto-open a browser; this is WSL, so there's no reliable way to pop a
      GUI browser window from here. Let the user open it themselves.
    - The dev server hot-reloads on `src/assets/blog.json` changes, so if the user
      asks for edits, just re-`Edit` `tech_post.md`, re-copy it over the site-repo
      copy, and re-run `npm run generate_blog` — no need to restart the server.
    - Present `players_post.txt` (with its character count) alongside the link for
      approval — it has no live preview, just show the text.
    - Incorporate any edits before moving on — both are about to be published
      outside this repo.
11. **Commit**, once approved:
    - In *this* repo: stage the milestone dir's files — `checklist.md`, `summary.md`,
      `stats_code.md`, `stats_dev.md`, `tech_post.md`, `players_post.txt` — and commit
      them **directly on the current branch** (same reasoning as step 16's version
      bump below: this must land wherever the milestone was completed, not on a
      throwaway feature branch spun off by the `commit`/`k:commit` skill). Message
      e.g. `Add {major}. {name} milestone report and release write-ups`.
    - Stop the `npm run serve` dev server started for the preview in step 10 — it's
      no longer needed and shouldn't be left holding a port/background task open.
    - In `../konh.github.io`: the post is already at `src/content/blog/<slug>.md`
      from the preview step above — if the user asked for edits during preview,
      make sure that copy matches the final approved text before continuing.
    - Run that repo's build + deploy step (`./deploy-github.sh` on macOS/Linux or
      `.\deploy-github.ps1` on Windows — both run `npm run build`, which regenerates
      `src/assets/blog.json` via `generate_blog`, then copy `dist/*` into the repo
      root).
    - Commit the new blog post plus the regenerated build artifacts **directly on
      the site repo's default branch (`master`)**. This intentionally bypasses that
      repo's own `CLAUDE.md` feature-branch convention — that convention is written
      for code changes; the user has asked for a direct commit for this specific
      content-only publish step. Still confirm with the user before pushing if
      anything about the diff looks unexpected (stray build output, unrelated
      changes already sitting in that working tree, etc.).

## Steps 12–17: Cut the release, roll the version

12. **Checkpoint — confirm before any GitHub-visible action.** Show the user the
    exact plan: the version number to release, the tag/release name, the
    `releases/<version>` branch name, and the finalized Release Notes text. Wait for
    explicit go-ahead before continuing — creating a release and pushing a branch are
    public, hard-to-reverse actions per `.claude/rules/workflow.md`.
13. **Cut the release**, once confirmed. Version number = the current full
    `bundleVersion` (`X.YYY`) read from `ProjectSettings/ProjectSettings.asset` — this
    pins the exact commit being released, unlike the milestone's bare major version.
    - Confirm `git status` is clean and the current commit already exists on `origin`
      (e.g. `git log origin/main..HEAD` is empty) — `gh release create` needs the
      commit to be reachable on GitHub already. If it isn't pushed, stop and tell the
      user to push first rather than guessing at what to push.
    - `git branch releases/<X.YYY>` at the current commit, then `git push origin
      releases/<X.YYY>`.
    - `gh release create v<X.YYY> --target releases/<X.YYY> --title "<major>. <name>"
      --notes "<Release Notes bullets from step 6>"`.
    - Report the release URL (`gh release create` prints it) back to the user.
14. **Ask for the next milestone's name.** The major version is about to increment,
    which per `Docs/Constitution.md`/`commit.md` convention also gets a fresh
    `_versionName`. Ask the user what to call it.
15. **Checkpoint — confirm the version transition** before editing anything: old
    `<major>.<name>` → new `<major+1>.0`, `<new name>`. Wait for explicit go-ahead.
16. **Bump the version**, once confirmed:
    - `Read` `ProjectSettings/ProjectSettings.asset`, parse `X` and `YYY` from
      `bundleVersion: X.YYY`, then `Edit` it to `  bundleVersion: {X+1}.0` (major
      +1, minor reset to `0` — this is the one place `X` is allowed to change; see
      `.claude/commands/commit.md`).
    - `Edit` `Assets/Configs/game_settings.json`'s top-level `"version"` field to the
      same `"{X+1}.0"` (the main-menu label reads this, not `Application.version` —
      see `commit.md`).
    - `Edit` `Assets/Scenes/MainMenu.unity`'s `_versionName:` line to the new name.
    - Stage all three files (`git add`) and commit them **directly on the current
      branch** — do not route this through the `commit`/`k:commit` skill: that skill
      (a) always branches off the default branch first, which would strand this
      bump on a throwaway feature branch instead of starting the milestone on `main`,
      and (b) always bumps `YYY` by 1, which would fight the reset to `0` just made.
      Commit message: short imperative subject (e.g. `Start milestone {X+1}. {name}`),
      no bullet-point file dump, trailer `Co-Authored-By: <model in use>
      <noreply@anthropic.com>` per the repo's usual commit-message rules.
    - Leave the commit unpushed, same as the normal commit flow — pushing is the
      user's call.
17. Report a short summary of everything done: report location, tech/player post
    locations (this repo and the live blog URL/slug), release URL, release branch,
    and the new milestone identity. Tick off the final checklist box.

## Notes

- Report generation is pure stdlib Python (mirrors `scripts/stats/`'s
  no-third-party-deps convention) — no LLM calls, safe to re-run.
- If `Docs/Milestones/` has no prior entries yet (first-ever run), both stats files
  clearly say so instead of fabricating a comparison — that's expected, not a bug.
- A spec with no `usage.csv` (shouldn't happen post-`spec-dev-stats`, but possible for
  a brand-new spec mid-session) is silently skipped in `stats_dev.md`'s aggregation.
- For iterating on the *upcoming* milestone's WebGL build without touching the public
  listing, use the separate **Deploy Unity Play (DEV)** GitHub Actions workflow
  (`.github/workflows/deploy-unity-play-dev.yml`, manual `workflow_dispatch`) — this
  skill does not trigger it automatically.
- `checklist.md` is scratch state for resuming a close-out, not a generated-report
  artifact like the `stats_*`/`summary` files — it's fine (expected, even) for it to
  carry all-checked boxes once the milestone is fully closed; no need to delete it.
