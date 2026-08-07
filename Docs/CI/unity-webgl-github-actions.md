# Unity WebGL GitHub Actions

Workflow: [`.github/workflows/build-webgl.yml`](../../.github/workflows/build-webgl.yml)

Builds the Unity project for WebGL with [game-ci/unity-builder](https://game.ci/docs/github/builder), using the existing build profile `Assets/Settings/Build Profiles/Web - Desktop - Release.asset`. Successful runs upload a `GlobalStrategy-WebGL` artifact (download from the Actions run page). This does **not** deploy to GitHub Pages — Pages already hosts the Blazor debug client via `deploy-web-client.yml`.

Official activation docs: https://game.ci/docs/github/activation

## Required secrets (Personal / free Unity license)

Add these under **GitHub → this repo → Settings → Secrets and variables → Actions → New repository secret**.

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | Full contents of your local Unity `.ulf` license file |
| `UNITY_EMAIL` | Email for the Unity account that owns that license |
| `UNITY_PASSWORD` | Password for that Unity account |

Prefer a password with **letters and digits only**. Special characters in `UNITY_PASSWORD` are a known GameCI failure mode.

### How to get `UNITY_LICENSE`

1. Install [Unity Hub](https://unity.com/download) and sign in with the CI account.
2. Open **Unity Hub → Preferences → Licenses → Add**.
3. Choose **Get a free personal license** and finish activation (click **Add** even if a license already shows — that is what writes the `.ulf` file).
4. Open the license file and copy its entire contents into the `UNITY_LICENSE` secret:

   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

The `.ulf` is not tied to a specific Unity editor version or OS. Activating on your desktop machine and using the file in Linux CI is fine.

## Professional / Plus / Pro license (alternative)

Do **not** set `UNITY_LICENSE` if you use a paid serial. Instead set:

| Secret | Value |
|---|---|
| `UNITY_SERIAL` | Serial from https://id.unity.com/en/subscriptions (`XX-XXXX-XXXX-XXXX-XXXX-XXXX`) |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

Then change the build step `env:` in `build-webgl.yml` from `UNITY_LICENSE` to `UNITY_SERIAL` (keep email/password).

## After secrets are set

1. Run **Actions → Build Unity WebGL → Run workflow**, or push a change under `Assets/`, `Packages/`, or `ProjectSettings/`.
2. When the job finishes, open the run and download the `GlobalStrategy-WebGL` artifact.
3. Serve the unzipped folder over HTTPS (or use a local static server) to smoke-test the build.

Until the secrets exist, the build job will fail at Unity license activation.
