# Unity WebGL GitHub Actions

## Workflows

| Workflow | File | What it does |
|---|---|---|
| Build Unity WebGL | [`.github/workflows/build-webgl.yml`](../../.github/workflows/build-webgl.yml) | Manual-only: builds WebGL and uploads a `GlobalStrategy-WebGL` artifact |
| Deploy Unity Play | [`.github/workflows/deploy-unity-play.yml`](../../.github/workflows/deploy-unity-play.yml) | Manual-only: builds WebGL, then uploads to [Unity Play](https://play.unity.com/en/games/e1953a2d-a3eb-40b1-b8ac-75282d4cf315/global-strategy) |

Both builds use [game-ci/unity-builder](https://game.ci/docs/github/builder) with `Assets/Settings/Build Profiles/Web - Desktop - Release.asset`.

Unity Play upload uses [`scripts/ci/deploy_unity_play.py`](../../scripts/ci/deploy_unity_play.py), which mirrors the WebGL Publisher package API (`POST /api/webgl/upload` + progress poll on `play.unity.com`).

Official GameCI activation docs: https://game.ci/docs/github/activation

GitHub Pages still hosts only the Blazor debug client (`deploy-web-client.yml`) — not the Unity WebGL build.

## Required secrets

Add these under **GitHub → this repo → Settings → Secrets and variables → Actions → New repository secret**.

### Unity license + account (required for both workflows)

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | Full contents of your local Unity `.ulf` license file |
| `UNITY_EMAIL` | Email for the Unity account that owns that license **and** the Unity Play game |
| `UNITY_PASSWORD` | Password for that Unity account |

Prefer a password with **letters and digits only**. Special characters in `UNITY_PASSWORD` are a known GameCI failure mode, and the same credentials are reused for Unity Play login.

The Unity Play deploy account must be the owner of the existing game (Unity Play publishing is per-user, not org-wide).

### How to get `UNITY_LICENSE`

1. Install [Unity Hub](https://unity.com/download) and sign in with the CI account.
2. Open **Unity Hub → Preferences → Licenses → Add**.
3. Choose **Get a free personal license** and finish activation (click **Add** even if a license already shows — that is what writes the `.ulf` file).
4. Open the license file and copy its entire contents into the `UNITY_LICENSE` secret:

   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

The `.ulf` is not tied to a specific Unity editor version or OS. Activating on your desktop machine and using the file in Linux CI is fine.

### Unity Play project id (optional override)

| Secret | Value |
|---|---|
| `UNITY_PLAY_PROJECT_ID` | Unity Play game id to **update** (default: `e1953a2d-a3eb-40b1-b8ac-75282d4cf315` from the live demo URL) |

Leave unset to keep updating the existing Global Strategy listing. Set only if you intentionally publish/update a different Play game.

Optional repository variable (Settings → Secrets and variables → Actions → **Variables**):

| Variable | Value |
|---|---|
| `UNITY_PLAY_TITLE` | Display title sent with the upload (default: `Global Strategy`) |

## Professional / Plus / Pro license (alternative)

Do **not** set `UNITY_LICENSE` if you use a paid serial. Instead set:

| Secret | Value |
|---|---|
| `UNITY_SERIAL` | Serial from https://id.unity.com/en/subscriptions (`XX-XXXX-XXXX-XXXX-XXXX-XXXX`) |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

Then change the build step `env:` in both Unity workflows from `UNITY_LICENSE` to `UNITY_SERIAL` (keep email/password). Unity Play deploy still needs `UNITY_EMAIL` / `UNITY_PASSWORD`.

## After secrets are set

Both workflows are **manual only** (`workflow_dispatch`) — they do not run on push or pull request.

### Artifact-only build

1. Run **Actions → Build Unity WebGL → Run workflow**.
2. Download the `GlobalStrategy-WebGL` artifact from the run.

### Deploy to Unity Play

1. Run **Actions → Deploy Unity Play → Run workflow**.
2. When the job finishes, open https://play.unity.com/en/games/e1953a2d-a3eb-40b1-b8ac-75282d4cf315/global-strategy (or the `url=` printed in the job log).

Until the license/account secrets exist, builds fail at Unity license activation. Until email/password are valid for the Play game owner, deploy fails at Unity ID login or upload.
