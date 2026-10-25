# Releasing Coreloom

Releases are built entirely by GitHub Actions from a version tag. Nothing is uploaded by hand.

## One-time setup

1. Create a GitHub repository, for example `<you>/Coreloom`. Public repositories get unlimited Actions minutes
   on hosted runners, and update checks need no token.
2. Push the code:

   ```powershell
   git remote add origin https://github.com/<you>/Coreloom.git
   git push -u origin main
   ```

3. Under *Settings → Actions → General*, make sure Actions may run. The release job only needs the default
   `GITHUB_TOKEN` with `contents: write`, which the workflow requests itself.
4. Watch the **CI** workflow on that first push. It builds and tests on Windows and Linux, publishes the
   self-contained app and runs CoreloomDiag on the runner. Fix anything red before tagging.

## Cutting a release

1. In `CHANGELOG.md`, turn the *Unreleased* notes into a section for the new version, for example
   `## [1.3.0] - 2026-11-03` (keep an empty `## [Unreleased]` above it). The release notes are taken from that
   section, and Coreloom builds its in-app *What's new* summary from them (see
   [Writing release notes](#writing-release-notes-for-the-whats-new-summary) below).
2. Commit and push to `main`. That's all: when a push to `main` changes `CHANGELOG.md` and its newest version
   heading has no tag yet, the **Release** workflow creates the tag (`v1.3.0`) on that commit and publishes the
   release. Pushes that don't add a new version heading are checked and skipped.

   You can still release by pushing a tag yourself instead (`git tag v1.3.0`, `git push origin v1.3.0`, or
   *Create Tag* in GitHub Desktop). A version with a suffix, such as `1.3.0-beta.1`, becomes a **pre-release**.
   Users only see it when they enabled *Include pre-release versions*.
3. The **Release** workflow:
   - works out the version (from the new changelog heading or the pushed tag);
   - builds the **Mac apps** on two of GitHub's Macs, one Apple silicon (`macos-15`) and one Intel
     (`macos-15-intel`): `build/mac/verify-app.sh` runs the macOS back-end's tests, then `build/mac/bundle.sh`
     publishes the app self-contained as a single file, lays out `Coreloom.app`, signs it, makes the update zip
     and the disk image, and checks them the way macOS and the updater will (signature, the zip unpacked with
     `ditto`, the disk image mounted). It then installs the app in `/Applications`, opens every page,
     photographs it and quits it. The release isn't published unless both Macs pass; what they saw is kept as
     the `mac-verify-<arch>-<version>` artifact of the run;
   - installs Inno Setup;
   - runs `build/build.ps1 -Package`, which restores, builds, tests, publishes self-contained `win-x64`
     (`Coreloom.exe` + `CoreloomDiag.exe`), builds the installer, zips the portable build and writes
     `SHA256SUMS.txt`;
   - adds the Mac packages, copies three files under stable names for the download buttons and the website,
     and writes `SHA256SUMS.txt` for everything;
   - creates the GitHub release, marked *Latest* (a pre-release isn't), with these assets:
     - `Coreloom-<version>-win-x64-Setup.exe` and `Coreloom-<version>-win-x64-Portable.zip`
     - `Coreloom-<version>-macos-arm64.dmg` and `.zip`, `Coreloom-<version>-macos-x64.dmg` and `.zip`
     - `Coreloom-Windows-Setup.exe`, `Coreloom-macOS-Apple-Silicon.dmg` and `Coreloom-macOS-Intel.dmg`: the same
       files under names that never change, so `https://github.com/<you>/Coreloom/releases/latest/download/<name>`
       (the README's buttons, the website) always downloads the newest version
     - `SHA256SUMS.txt`
4. The build embeds `GITHUB_REPOSITORY` as the update source. Copies of earlier versions find the
   new release at their next check (20 seconds after they start, then as often as *Settings → Updates* says;
   daily by default) or right away with *Check for updates*. With *Update automatically* on (the default), they
   download and verify it in the background, show a badge and a notification, and install it the next time
   Coreloom starts, while the PC is idle, or when the user clicks *Install update*. Installed copies update with
   `-Setup.exe`, portable copies with `-Portable.zip`, so every release needs both (plus `SHA256SUMS.txt`). Copies
   of 1.1.0 or earlier that are portable still link to the release page: they predate in-place updates.
   Mac copies (1.6.0 and later) update with `-macos-arm64.zip` or `-macos-x64.zip`, whichever matches the Mac:
   the download is checked against `SHA256SUMS.txt`, unpacked with `ditto`, its bundle identifier and code
   signature are checked, and a small helper swaps it in once Coreloom has quit (putting the old copy back if
   anything fails) and opens it again.

If you create the release yourself on GitHub's website (for example to write its text there), the workflow adds
the files to that release and keeps your title and text; it only fills in the text when you left it empty. If a
release ever ends up without its files, open *Actions → Release → Run workflow* and enter the tag: it builds that
tag again and uploads the files.

Keep the asset names unchanged: the updaters look for `*-win-x64-Setup.exe` (with `x64` in the name, so the
stable `Coreloom-Windows-Setup.exe` copy is never picked by mistake), `*-win-x64-Portable.zip`,
`*-macos-arm64.zip`, `*-macos-x64.zip` and `SHA256SUMS.txt`. Never replace
the assets of a published release: copies that already downloaded it would see a different checksum. Publish
a new version instead.

## Writing release notes for the "What's new" summary

Coreloom shows a short summary of each release in *Settings → Updates*, in the update notification and after
updating. It's built from the release notes like this:

- The **first sentence** before any heading becomes the intro, so start with one plain sentence that says what
  the release is about.
- Up to **five bullets** are shown. If there's a `### Highlights` (or `### What's new` / `### Summary`) section,
  its bullets are used. Otherwise the Keep a Changelog sections are used, in this order: `### Security`,
  `### Added` (tagged *New*), `### Changed` (*Improved*), `### Fixed` (*Fixed*), `### Removed`, `### Deprecated`.
  Anything beyond five becomes "…and N more changes in the full release notes."
- Each bullet is cut to its **bold lead** and **first sentence**: `- **Faster charts.** Scrolling now keeps up
  with 240 Hz displays. More details…` shows *Faster charts* with "Scrolling now keeps up with 240 Hz displays."
  Put the most important words first; keep the first sentence under about 140 characters.
- Sub-bullets, code blocks, tables and everything after a `---` line (the download footer the workflow adds) are
  left out.

Example:

```markdown
## [1.1.0] - 2026-11-03

Coreloom now keeps itself up to date.

### Added
- **Automatic updates.** New versions download in the background and install when Coreloom restarts.
- **What's new.** A short summary of every release, right in Settings.

### Fixed
- **Tray icon.** It no longer disappears after Explorer restarts.
```

## Verifying a release

- Download the assets and check them:

  ```powershell
  Get-FileHash .\Coreloom-1.0.0-win-x64-Setup.exe -Algorithm SHA256
  ```

  The hash must match the line in `SHA256SUMS.txt`.
- Install the previous version (and unzip its portable build), then use *Check for updates* in each. *Install
  update* should download, verify, install and relaunch. The full checklist, including the automatic paths, is in
  [TESTING-ON-YOUR-PC.md](TESTING-ON-YOUR-PC.md#6-automatic-updates-after-publishing-two-releases).
- Mac: push any commit to a branch named `macupdatetest-…` (or run *Mac update test* from the Actions tab). On
  GitHub's Apple silicon and Intel Macs it installs an older Coreloom built from that commit, and then the previous
  release's own Mac app, and lets each update itself to the release you just published (find, download, checksum,
  signature, replace, reopen). What it saw, with screenshots and logs, lands on the `mac-update-test-arm64` and
  `mac-update-test-x64` branches (`update-test.md`; the previous release's run is in `from-<version>/`).

## The website

`site/` is the download page. The **Website** workflow publishes it to the `gh-pages` branch whenever it changes
on `main`, and GitHub Pages serves it at `https://<you>.github.io/Coreloom/` (*Settings → Pages*: deploy from the
`gh-pages` branch, root folder; GitHub turns this on by itself the first time the branch is pushed). The page asks
GitHub's API for the latest release when it opens (its version, date and file sizes) and its buttons use the
stable names, so a new release needs no change to the site.

## Mac: signing and notarization

Without an Apple Developer account, the Mac app is signed **ad hoc**: its code is sealed and checked (the updater
refuses a bundle whose signature doesn't check out), but it isn't tied to a developer identity, so the first
time someone opens a downloaded copy macOS says it could not verify Coreloom and they confirm it once in *System
Settings → Privacy & Security → Open Anyway*. The disk image's window, the README and the website say so.
Updates installed from inside Coreloom aren't downloaded through a browser, so they open without asking.

With an Apple Developer Program membership, add these repository secrets and the next release is signed with your
Developer ID, with the hardened runtime (`src/Coreloom.Mac/Coreloom.entitlements`), notarized by Apple and stapled
(the app and the disk image), so it opens with no warning at all. Nothing else changes.

| Secret | What |
|---|---|
| `MAC_CERT_P12` | Your *Developer ID Application* certificate and key, exported as .p12 and base64-encoded (`base64 -i cert.p12`) |
| `MAC_CERT_PASSWORD` | The .p12's password |
| `MAC_SIGN_IDENTITY` | The identity's name, e.g. `Developer ID Application: Jerry Ubah (TEAMID1234)` |
| `MAC_NOTARY_APPLE_ID` | The Apple ID of the developer account |
| `MAC_NOTARY_TEAM_ID` | The team ID |
| `MAC_NOTARY_PASSWORD` | An app-specific password for that Apple ID (appleid.apple.com → Sign-In and Security) |

Copies signed ad hoc update to Developer ID-signed versions normally (the updater checks that the new bundle's
signature is valid and that it is Coreloom, not who signed it).

## Code signing on Windows (recommended before wide distribution)

Releases aren't code-signed yet, so Windows SmartScreen will warn on first run until the download builds
reputation. To sign:

1. Get a code-signing certificate (or use Azure Trusted Signing).
2. Add the certificate as repository secrets.
3. Add a `signtool sign` step to `release.yml` after the build step and before the checksums are written.
   Sign `Coreloom.exe` and `CoreloomDiag.exe` in `artifacts/publish/win-x64`, and the setup executable (Inno
   Setup's `SignTool` directive can do the last one).
4. Regenerate `SHA256SUMS.txt` after signing. The simplest way is to sign inside `build.ps1` before its
   checksum step.

## Local packaging

```powershell
./build/build.ps1 -Version 1.0.0-local -Repository <you>/Coreloom -Package
```

The output goes to `artifacts/release/`. Without Inno Setup, only the portable zip and checksums are
produced.

On a Mac (`pip install dmgbuild` lays out the disk image's window; without it the image is plainer):

```bash
bash build/mac/bundle.sh --arch arm64 --version 1.6.0-local --repository <you>/Coreloom   # or --arch x64
```

The app, the update zip and the disk image go to `artifacts/mac/`. To check a build on a Mac the way the release
does, `bash build/mac/verify-app.sh arm64 artifacts/mac-verify`; pushing a branch named `macverify-*` runs the same
check on GitHub's Macs and publishes what they saw to the `mac-verify-arm64` and `mac-verify-x64` branches.
