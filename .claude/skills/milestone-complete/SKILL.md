---
name: milestone-complete
description: Generate a milestone-completion report (summary.md, stats_code.md, stats_dev.md) under Docs/Milestones/<zero-padded-major>_<version-name>/, comparing code LoC and Docs/Specs/*/usage.csv dev stats against the previous milestone, then (with the user's sign-off) write Release Notes, draft a tech blog post and an English + Russian player-facing announcement, update this repo's README.md with a player-facing Milestones entry and a refreshed Game-features summary, cut a GitHub release + releases/<version> branch, roll the project version over to the next milestone, and (after a separate confirmation) delete branches already merged into main, local and remote. Tracks progress in the milestone dir's checklist.md so a run can be resumed. Load when the user asks to close out, wrap up, or report on a project milestone/version.
---

# Milestone Complete

Produces a point-in-time report of everything shipped in the current major-version
milestone: codebase size (LoC per tracked extension) and development cost/effort
(aggregated from every spec's `usage.csv`, see `Docs/Specs/26_07_22_17_spec-dev-stats/`),
each compared against the previous milestone if one exists. It then walks the user
through actually closing the milestone out: Release Notes, a tech blog post, an
English + Russian player-facing announcement, this repo's own `README.md` (a new
player-facing `## Milestones` entry plus a refreshed `## The Game (briefly)`
section), a GitHub release, a `releases/<version>` branch, the version bump that
starts the next milestone, and — as its own confirmed step — deleting branches
already merged into `main` (via PR or manually), both local and remote.

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
   slugified. This gives the output dir `Docs/Milestones/<major>_<slug>/`, where
   `<major>` is zero-padded to 2 digits (`01`, `02`, ... `99`) so up to 99 milestones
   sort correctly by filename — e.g. `Docs/Milestones/01_world-domination/`. The major
   version itself stays unpadded everywhere else (summary header, checklist title,
   commit messages, etc.) — only the directory name is padded.
2. If `Docs/Milestones/<major>_<slug>/checklist.md` already exists (padded dir name,
   as above), this is a **resume** — read it, find the first unchecked step, tell the
   user where the last run left off, and continue from there instead of restarting.
   Don't re-run already-checked steps (e.g. don't regenerate a report that's already
   been reviewed, don't re-cut a release that's already checked off).
3. Otherwise, create the directory and write `checklist.md` with every step below
   (1 through 18) as an unchecked box, e.g.:

   ```markdown
   # Milestone Close-Out Checklist — <major>. <name>

   - [ ] 1. Generate report (summary.md, stats_code.md, stats_dev.md)
   - [ ] 2. Present report to user
   - [ ] 3. Decide: continue to close-out now, or stop after the report
   - [ ] 4. Review commit history in range
   - [ ] 5. Propose release-note bullets; get user's list
   - [ ] 6. Write ## Release Notes into summary.md
   - [ ] 7. Propose tech_post.md themes; get user's choice
   - [ ] 8. Propose players_post themes; get user's choice
   - [ ] 9. Draft tech_post.md, players_post.en.txt / players_post.ru.txt, and the README Milestones/Game-features updates
   - [ ] 10. Get user approval on all drafts (tech post, player posts, README updates)
   - [ ] 11. Commit milestone docs + README (this repo) + publish tech_post.md to the site
   - [ ] 12. Checkpoint: confirm release plan
   - [ ] 13. Cut the GitHub release + releases/<version> branch
   - [ ] 14. Ask for next milestone's name
   - [ ] 15. Checkpoint: confirm version transition
   - [ ] 16. Bump version, commit
   - [ ] 17. Checkpoint: confirm merged-branch cleanup list, then delete
   - [ ] 18. Report final summary
   ```

   `Edit` the box to `[x]` immediately after finishing each step — don't batch this at
   the end. A crashed/interrupted session should still leave an accurate checklist.
   Also **say** which step is starting/finishing as you go (a short status line, e.g.
   "Step 5/17: proposing release-note bullets…") — `checklist.md` is the resume state,
   not a substitute for telling the user where the flow currently is.

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
8. **Propose players_post themes.** Same idea, for a short player-facing announcement
   (Discord/Steam/itch.io-style, not this repo's own docs), written in **both English
   and Russian** — this project ships real (not machine-translated) Russian for
   player-facing text, per the `localization` skill's convention. Propose 2–4 angles
   (e.g. "lead with the headline feature", "narrative/flavor framing", "plain
   patch-notes list") and ask which to run with — one theme choice covers both
   locales. Keep this pass light — it's a ≤1024-character post, not a spec.
   Bias toward *short*: an early milestone reads better calling itself an
   "iteration" in exploratory/research-prototype mode than dressing it up as a
   "working prototype" — honest framing over polish. Once `tech_post.md`'s URL
   is known (step 10), close the player post with both the play-demo link and
   the blog-post link inline in the text itself, not just in this repo's README.
9. **Draft all three documents, plus the README player-facing updates,** once themes
   are chosen. Save the milestone documents into `Docs/Milestones/<major>_<slug>/`
   (padded `<major>`, per step 0):
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
       fence would render as inert text, not a picture. If a diagram or chart earns
       its place, author it as a **standalone `.svg` file** under
       `Docs/Milestones/<major>_<slug>/images/` — not inline in the Markdown body.
       Inline `<svg>` scales unpredictably against its surrounding container (it can
       render 2x+ too large if the diagram's own `viewBox` is narrower than the post
       column) and platforms like LinkedIn don't render embedded/inline SVG in link
       previews. Use a **fixed, self-contained palette** (a dark canvas with white
       blocks and one accent color reads well) rather than the site's `var(--*)` CSS
       tokens, since the file now stands alone outside the page's theme. Reference it
       from the post as `<img src="images/<name>.svg" style="max-width:100%;
       height:auto;display:block;margin:0 auto;" alt="...">`, wrapped in
       `<figure>`/`<figcaption>` where a caption helps — with a blank line before and
       after each `<figure>` block (blank lines *inside* it can break `marked`'s raw-
       HTML-block parsing). Load the `artifact-diagramming` skill for
       diagram-composition know-how (what makes a diagram worth including, how to
       keep it legible) — its CSS-token guidance doesn't apply here. Also export a
       `.png` backup at 2x density for platforms with poor SVG support, keeping the
       `.svg` as the editable source (see the rasterizing note just below). Sync both
       the `.svg` and `.png` into the site repo's `public/img/blog/<slug>/` and
       rewrite the post's `images/` paths to `/img/blog/<slug>/` when copying it over
       (a `sed` substitution on the copy is enough).
     - **Rasterizing SVG → PNG**: don't reach for browser screenshot/zoom automation
       for this — its viewport and region-coordinate behavior was unreliable across
       calls in practice (silent cropping, inconsistent scale, occasional blur from a
       viewport/content size mismatch). What works reliably: temporarily
       `npm install sharp --no-save` in the site repo, rasterize with a short one-off
       Node script (`sharp(svgPath, {density: 192}).resize({width: w*2}).png()
       .toFile(out)` — 192 is 2x the 96dpi baseline), then `npm uninstall sharp`.
       `git status -- package.json package-lock.json` should show nothing after —
       confirm that before moving on.
   - **`players_post.en.txt` and `players_post.ru.txt`** — plain text (no title/tags
     header — this isn't going into the site's blog pipeline), gameplay/feature
     framing per the chosen theme, light on tech detail. **Hard limit: 1024
     characters each.** Count the characters before presenting each; trim if over.
     Write the Russian version as a real, natural translation (idiomatic phrasing,
     correct terminology for in-game terms — check `Assets/Localization/ru.asset`
     for how existing terms are rendered) rather than a literal pass of the English
     text — see the `localization` skill for the project's translation conventions.
   - **README player-facing updates** — two edits to this repo's own `README.md`:
     - **`## Milestones`** section, placed immediately after `## Tech Stack` (before
       its trailing `---` separator) — create the section on the very first run if it
       doesn't exist yet. Prepend a new entry at the **top** (newest-first, this
       section only ever grows — never edit or reorder earlier entries):
       `### <major>. <name> — <end_date>` (end_date from this milestone's
       `<!-- milestone-meta: {...} -->` comment), followed by a short, concise bullet
       list of the milestone's player-facing highlights. Reuse the settled Release
       Notes bullets from steps 5/6 (trim to the handful that actually matter to a
       player rather than the full list) instead of drafting new copy from scratch —
       these bullets are already written in plain user-facing language.
     - **`## The Game (briefly)`** — review its existing bullets against what
       actually shipped (the commit-history review from step 4, plus anything else
       you know is now true of the game) and update it so it describes the
       *current, actual* feature set rather than a stale snapshot from an earlier
       milestone. Add bullets for major new systems, adjust or remove anything no
       longer accurate, and keep the section short and player-facing — it's the
       "briefly" summary, not an exhaustive feature list. Skip this edit silently if
       nothing has meaningfully changed since the section was last reviewed.
10. **Preview, then get approval.** Before asking for sign-off, let the user see
    `tech_post.md` rendered as it will actually appear, and the two `README.md`
    edits as a plain diff:
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
    - Present `players_post.en.txt` and `players_post.ru.txt` (each with its
      character count) alongside the link for approval — neither has a live preview,
      just show the text for both.
    - Present the proposed `README.md` diff (the new `## Milestones` entry and any
      `## The Game (briefly)` edits) in the same approval pass — a plain before/after
      diff is enough, no live rendering needed.
    - Incorporate any edits before moving on — all of this is about to be published
      or committed.
11. **Commit**, once approved:
    - In *this* repo: stage the milestone dir's files — `checklist.md`, `summary.md`,
      `stats_code.md`, `stats_dev.md`, `tech_post.md`, `players_post.en.txt`,
      `players_post.ru.txt` — plus `README.md`, and commit them **directly on `main`**
      (this repo's commits land on `main`, never a feature branch — switch there first
      if the close-out was run from elsewhere: `git fetch origin main`, `git checkout
      main`, `git pull --ff-only origin main`, carrying the milestone dir's untracked
      files across the switch, same as step 16's version bump below). Do not route
      this through the `commit`/`cc:commit` skill, which always branches off the
      default branch first. Message e.g. `Add {major}. {name} milestone report and
      release write-ups`.
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

## Steps 12–18: Cut the release, roll the version, clean up branches

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
    - Stage all three files (`git add`) and commit them **directly on `main`** (same
      branch-switch procedure as step 11, if not already there) — do not route this
      through the `commit`/`cc:commit` skill: that skill (a) always branches off the
      default branch first, which would strand this bump on a throwaway feature
      branch instead of starting the milestone on `main`, and (b) always bumps `YYY`
      by 1, which would fight the reset to `0` just made.
      Commit message: short imperative subject (e.g. `Start milestone {X+1}. {name}`),
      no bullet-point file dump, trailer `Co-Authored-By: <model in use>
      <noreply@anthropic.com>` per the repo's usual commit-message rules.
    - Leave the commit unpushed, same as the normal commit flow — pushing is the
      user's call.
17. **Clean up merged branches.** Checkpoint — confirm before deleting anything;
    deleting branches is hard-to-reverse per `.claude/rules/workflow.md`, and this is
    a separate confirmation from steps 12/15, not covered by either of them.
    - `git fetch --prune origin` first, so stale remote-tracking refs don't produce
      false positives or false negatives.
    - List candidates: `git branch --merged main` for local branches already merged
      into `main`, and `git branch -r --merged main` for remote branches on `origin`.
      Ancestry-based `--merged` catches a branch whether it landed via a GitHub PR or
      a manual/direct merge (like this repo's own commits land, per `commit.md`) — it
      doesn't need to distinguish the two, and this repo's PRs merge with a real merge
      commit rather than squashing, so ancestry checks correctly catch PR-merged
      branches too.
    - Exclude protected branches from both lists: `main`/`origin/main`, the branch
      currently checked out, anything under `releases/` (those are intentionally kept,
      cut in step 13), and `origin/HEAD`.
    - Present the resulting candidate list (local names and remote names, deduplicated,
      noting branches present on both) to the user and wait for explicit go-ahead.
    - Once confirmed, delete: `git branch -d <name>` for each local branch (plain
      `-d`, not `-D` — it refuses anything not actually merged, which is the safety
      net if the candidate list was somehow stale by the time deletion runs), and
      `git push origin --delete <name>` for each remote branch.
    - Report which branches were deleted, and note (don't fail the step over) any that
      were already gone or failed to delete.
18. Report a short summary of everything done: report location, tech/player post
    locations (this repo — including both `players_post.en.txt` and
    `players_post.ru.txt` — and the live blog URL/slug), release URL, release
    branch, the new milestone identity, and the branches deleted in step 17. Tick off
    the final checklist box.

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
- The README's `## Milestones` section is **additive only** — each run prepends one
  new entry and never edits, reorders, or removes an earlier one. It is the repo's
  running player-facing changelog; the full write-ups stay under `Docs/Milestones/`.
- `## The Game (briefly)` is a living summary, not a per-milestone log — it should
  always read as an accurate snapshot of the game *today*, so update/replace its
  bullets in place rather than appending to them.
- The site's `BlogPostView.vue` already has a built-in image lightbox (a
  hover-visible expand button on every post image opens a fullscreen, blurred-
  backdrop view; closes via the × button, the backdrop, or the image itself) and
  bordered/centered table styling. New posts get both automatically — no need to
  reimplement either.
- `generateBlog.ts`'s excerpt generator strips markdown image/link syntax and raw
  HTML tags (fixed after a post that opened with a bare `<img>` leaked its literal
  tag text into the blog-list excerpt) — safe to open a post with an image again.
- This repo's own automation (or another concurrent session) can commit to the
  *same* working tree while a close-out is in progress, in either repo. Before
  committing anything in step 11 or 16, run `git status` and check for unrelated
  staged/unstaged changes that aren't yours; stage by explicit pathspec (`git add
  <specific paths>`, verified with `git diff --stat` before committing) rather
  than `git add -A`/`git commit -a`, so unrelated concurrent work doesn't get
  swept into the milestone commit.
