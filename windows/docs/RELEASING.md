# Releasing Clicksmith for Windows

1. Set the version in `Directory.Build.props` and `ClicksmithInfo.Version` (src/Clicksmith.Core/EngineLogic.cs).
2. Regenerate `MANIFEST.sha256` (from the `windows` folder):
   ```bash
   find src scripts assets Directory.Build.props -type f | grep -vE '/(bin|obj)/' | sort | xargs sha256sum > MANIFEST.sha256
   ```
3. Commit, then tag and push — the tag must match the version:
   ```bash
   git tag windows-v2.5.0-beta.1 && git push origin windows-v2.5.0-beta.1
   ```
4. GitHub Actions (*Windows release*) builds x64 and arm64 packages, `Clicksmith-Setup-<version>.exe`, `release-windows.json` and `SHA256SUMS.txt`, and publishes them. Versions with `-beta`/`-rc` become pre-releases.

The web installer (`get.ps1`) and in-app updates pick the newest `windows-v*` release and verify the package against `release-windows.json`.

Code signing isn't set up. Free options for open-source projects exist (for example SignPath Foundation); with a certificate, sign `Clicksmith.exe`, `Clicksmith.Engine.exe` and the Setup.exe in the release workflow.
