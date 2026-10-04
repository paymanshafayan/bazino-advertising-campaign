# Jinus for Windows — User Guide

> **App name:** in English documents this Windows app is written **Jinus**; in Persian documents the same app is written **«ژینوس»**. The executable is still `BazinoMarketing.exe` and the data folder is still `%LOCALAPPDATA%\BazinoMarketing\`.

**Audience:** the Windows owner/operator<br>
**Source of truth:** the implemented `marketing-app/` source and its publishing contract, not older proposals or mock screenshots.<br>
**Reviewed:** 2 October 2026 · **Current app:** 0.8.1 · **Validated private release:** [`marketing-app-dev-41`](https://github.com/paymanshafayan/bazino-gamenet-portal/releases/tag/marketing-app-dev-41)

> **Important current status:** Version 0.8.1 has already been installed on the owner’s Windows computer. This guide does not ask you to repeat that update. The recorded live check was a mailbox `ping` that returned `pong=true` and `version=0.8.1`; that proves the app answered that command at that time, not that every connected service or publishing operation is currently healthy.

## 1. What the app does

Jinus is a self-contained Windows desktop app for service checks, browser-assisted research, local media work, content review, publishing and logs. The interface installed as 0.8.1 has six Persian-labeled tabs. The current branch adds a seventh section (the daily-report page), a light sidebar layout and a full-screen start; the Windows CI build for these changes is green (run `37105410361`, 7/7 pages rendered, pre-release `marketing-app-dev-45`), but they are **not installed on your computer yet** — treat them as built-but-not-installed until that release is installed with your approval.

| Persian tab | English meaning | Main purpose |
|---|---|---|
| اتصال | Connection | Check configured services, review mailbox status, run network diagnostics and copy a redacted diagnostic bundle. |
| تنظیمات | Settings | Configure GitHub, Zernio, Portal Ingest, Kling, FLUX, Groq, custom services and browser options. |
| مرورگر | Browser | Launch and control a dedicated Chrome instance, select a tab, open a URL and read visible page text. |
| رسانه | Media | Download one specified Instagram post/Reel, manage local files, edit with FFmpeg, transcribe speech and optionally send selected files to the private GitHub mailbox. |
| محتوای آماده انتشار | Ready-to-publish content | Review queue cards in an Instagram-like preview, approve a specific item or return feedback. |
| لاگ | Logs | Search, filter, copy and inspect sanitized app events. |
| گزارش روزانه | Daily report | (branch) Read the newest daily Instagram report from the repository as KPI tiles, bar/line/donut diagrams and a best-posts table. Read-only; it never writes to the repository. |

The app is a workstation tool, not a content idea editor or a general-purpose Instagram account browser. Its checked-in code does **not** expose a command to list Instagram Saved collections or automatically unsave items. Older content documents that describe those actions are workflow proposals, not app capabilities.

### Safety and approval model — read before connecting

- While the app is listening to the configured GitHub mailbox, valid commands from the paired agent are executed **without an in-app approval prompt**. Risk labels are logged but do not create a confirmation dialog. The Settings permission checkboxes are not a reliable command-execution safety boundary in this version. Use **Stop listening** or close the app when remote access is not wanted.
- A specific content card’s **Approve** action is a consequential action: the app records approval and the scheduler may publish it at its approved time while the app is open. Inspect the media, caption, content type, destination, time and attached engagement instructions before clicking it.
- Public posts and real DMs must have separate, explicit approval for that specific content/operation. Do not infer permission from this guide, a plan, a successful connection check, or a queued draft.
- Do not test Portal Ingest by sending a sample POST. The existing endpoint records media, and a test request could create real data. The Portal card currently has no connection-test button; this guide explains that limitation rather than pretending it has been tested.

## 2. Windows installation, first run and local data

### Requirements

- Windows 10 or 11, 64-bit. The published app is a self-contained Windows x64 build; .NET does not need to be installed separately.
- Internet access to the private GitHub repository and any service you choose to check or use.
- Additional tools are optional and feature-specific: Node.js plus the official Kling CLI for Kling; FFmpeg for media editing if it is not already on `PATH`.

### Install or open the app

1. If you need to reinstall or manually recover it, open the private release page linked above and download `BazinoMarketing.exe` from the validated release. Keep it in a stable folder that your Windows account can write to.
2. If Windows SmartScreen warns about the unsigned private build, verify that the file came from the private release and compare its SHA-256 with `SHA256SUMS.txt` in that same release before choosing whether to run it. Do not bypass a warning for an unverified download.
3. Start `BazinoMarketing.exe`. Only one regular instance is supported at a time. The app may attempt a one-time, non-overwriting import of supported legacy settings on first launch; review the import summary if it appears.
4. The normal app data root is `%LOCALAPPDATA%\BazinoMarketing\`. It contains non-secret settings, encrypted secrets, logs and downloads. Media defaults to `%USERPROFILE%\Downloads\BazinoMarketing\` unless you select another folder in the Media tab.

Secrets are protected for the current Windows user using DPAPI and are separate from `settings.json`. They are not meant to be copied as plaintext or restored to a different Windows user/device. Never put API keys, tokens, cookies, passwords or private media in a Git commit or chat.

### Legacy import

The app can import supported values from the old app’s `%APPDATA%\BazinoMarketingBrowser\vault.json` and `%APPDATA%\BazinoBridge\settings.json`. Importing is local to the Windows user, does not prove the imported credentials still work, and does not import the old Kling MCP login as a current Kling CLI login. The Settings page contains legacy-import controls and a summary; use the non-overwriting option unless you explicitly intend to replace current values.

### Export or import non-secret settings

The **Storage and backup** area can export a JSON settings file **without keys/tokens**, import a settings JSON file, and open the data folder. Import replaces the app’s non-secret settings and rebuilds its cards, but does not replace saved secrets. Keep a copy of the current settings before importing a file, and re-check the repository, branch, services and proxy choices afterwards. An exported settings file is not a credential backup.

## 3. Connection page

Open **اتصال (Connection)**. The page has service cards, mailbox controls and network/diagnostic actions.

### Check configured services

- Choose **بررسی همه (Check all)** to run each service card’s configured check in sequence. You can also use **آزمایش اتصال (Test connection)** on an individual card.
- These checks are probes, not proof of account authorization for every future action. A successful Zernio check does not authorize a post or DM; a successful GitHub check does not validate Portal Ingest.
- The visible service status is time-bound. Run a fresh check before relying on a connection, and read the summary/error details as well as the colored status.
- Choose **بررسی شبکه (Probe network)** to inspect the machine’s network path. The result diagnoses this computer’s current reachability only; it is not an app-wide guarantee.
- **Copy diagnostic bundle** copies a redacted summary of settings, service states, recent events and optional network probes. Review it before sharing. The app’s log redactor removes known secrets, but never intentionally add secret values to diagnostic notes.

### GitHub command mailbox

The current defaults point to the private repository `paymanshafayan/bazino-gamenet-portal`, branch `arena/01a0f126-bazino-gamenet-portal`, and mailbox path `marketing-app-mailbox`. The app starts listening automatically when GitHub is configured; the Connection page displays the current mailbox/agent state and recent commands. It also offers immediate polling and a listening stop/start control.

The mailbox is a remote command channel, not merely a status widget. Keep it listening only while the agent needs it. Press **Stop listening** or close the app to stop accepting new work; closing the app is the clearest off switch. The same card can request an app restart and shows the app fingerprint and recent command history. Do not change repository, branch or mailbox path unless the operator has coordinated the matching agent-side configuration.

## 4. Configure services in Settings

Open **تنظیمات (Settings)**, edit a card, enter secrets into its password/key field, and choose **ذخیره (Save)**. Use **آزمایش اتصال (Test connection)** where the card provides it. Save and test are separate actions. Most cards also let you clear their saved secret. Proxy choices are system proxy, no proxy or a custom URL; only enter a proxy you trust.

### GitHub

1. Set the private repository as `owner/repo`, the exact branch and the mailbox folder path.
2. Enter a GitHub token with the repository permissions required by the mailbox (Contents read/write for this private repo). Prefer the narrowest token scope available and do not paste the token into logs or chat.
3. Save, then test the card. The app uses GitHub for mailbox traffic, settings/command exchange, media uploads and daily-insights reports. These are separate from the Portal Ingest credential.

### Zernio

1. Keep the API base URL at `https://zernio.com/api` unless the service operator has supplied a different supported endpoint.
2. Enter the Zernio API key and save; use the card’s connection check.
3. A check can report account/connection information, but publishing, automations and analytics still require current account access, supported permissions and explicit per-content authorization.

### Bazino Portal — Affiliate Reel ingest

- The default base URL is `https://bazino.pro`. The implemented client accepts only the official HTTPS root for `bazino.pro` and appends the existing route `/api/integrations/instagram/published-media` itself; do not paste the route into the base-URL field.
- Enter the separate **ingest Bearer token** and select **Save settings**. The app stores it in the Windows-protected secret store. The status text indicates whether a token is stored; it does not validate that token with a request.
- There is currently **no Test Connection button and no read-only Portal probe** in this card. Do not use the reporting POST as a connection test. A future safe health check would need to be an explicitly implemented read-only GET.
- The client is one-way: after a successfully published, owner-approved Affiliate Reel, it reports the real Instagram `media_id`, `media_type=reel` and `published_at` to the existing endpoint. It does not create webhooks, callbacks or a new Portal route. Affiliate is Reel-only; the Portal’s broader technical acceptance of other media types does not widen the owner’s rule.

### Kling CLI

1. Use the region that matches the owner’s real Kling account: **Global** (`kling.ai`) or **China** (`klingai.com`). Do not guess or install both variants.
2. Install Node.js on the Windows computer first; Node is not bundled with this app. The card’s **Install CLI** action opens a visible console for the official regional package installation.
3. Use **Login** to run the official `kling login` browser-based sign-in. Sign in only in the browser flow; do not paste a session token into the app or repo. The app can inspect the CLI/account state with its test action.
4. If the CLI or Node cannot be found, use **Detect again** or set the optional full paths to `kling.cmd` and `node.exe`. Exit/logout is available on the card.
5. CLI operations that generate media or consume credits are not ordinary connection checks. Require explicit approval of the proposed operation and cost before running them.

### FLUX / image-provider card

- The provider is **disabled by default**; no provider or spend is assumed. The card supports Cloudflare Workers AI or a generic API provider. For Cloudflare, supply the account ID, model and API token. For a generic service, supply its base URL, model and a safe GET test URL such as a documented models route.
- Use **Test connection** only for the card’s configured check. A connection/model-list check is not an image-generation action. Image generation may incur provider charges and must not be launched without explicit approval.

### Groq / speech transcription

- The app supports `whisper-large-v3` and `whisper-large-v3-turbo`, an optional language code (`fa`, `en`, etc.; blank means auto-detect) and an optional Groq key. Save the key on this card or in the Media page’s Groq key field.
- If no Groq key is stored, the speech transcription implementation can use a configured Cloudflare Whisper fallback. A missing or failed provider is not proof of a successful transcript; inspect the reported provider/model and review the text.
- The Translate-to-English option is available in Media. It requests direct speech translation where supported; it does not replace human review.

### Custom service cards

Choose **Add service**, name the card, then choose the HTTP API or MCP card type and set its base URL, optional test URL, authentication type/header and proxy as appropriate. Save and use its configured test. A successful generic GET does not prove that a separate custom command is safe or authorized; do not use a test URL with side effects. Remove a card only if you also intend to remove its saved credential.

## 5. Browser page

The Browser tab launches a dedicated Chrome instance using the app’s configured remote-debugging port (default `9334`) and an app-managed profile. It does not attach to every ordinary Chrome window by default.

1. Press **Refresh status**. If Chrome is not available, press **Open Chrome**. If automatic discovery fails, set the full `chrome.exe` path and save the browser settings.
2. Enter a URL and choose **Open and read text**, or refresh the tab list, select a tab and choose **Connect to tab**. The app displays the selected URL/title and readable page text. **Copy text** copies the visible text result, not a screenshot.
3. Use **Detach tab** when finished; close the dedicated Chrome window or the app to end the session.

Treat any signed-in tab as data the agent may be able to inspect or operate through the app. Use only pages that are in scope; do not sign into sensitive accounts in this dedicated profile unless that access is expressly intended. The browser connection is local to this computer; it is not a cloud browser or a secure publishing approval by itself.

## 6. Media page

### Choose a workspace folder

The default is `%USERPROFILE%\Downloads\BazinoMarketing\`. Use **Browse folder**, then **Confirm** to switch folders. Confirmation checks that the app can read and write there. **Open folder** and **Refresh** reveal the selected files. The list includes up to 250 supported image, video and audio files; selecting media shows its preview when the format is supported.

### Download one Instagram post or Reel

1. Paste the complete URL of the one post/Reel you are authorized to use, or choose **Get current tab URL** after selecting the intended browser tab.
2. Choose **Download this post** and wait for the result. It downloads the supplied item; it does not search an account, browse Saved collections, download a whole profile or automatically unsave a post.
3. Respect the account’s access rules, rights and the owner’s content instructions. A failed download may be due to access/network/provider restrictions; do not claim the item was fetched unless it appears in the local file list.

### Edit or convert with FFmpeg

- The app searches for `ffmpeg.exe` on `PATH`. If it is not found, browse to the executable and save its path.
- **Preview image** generates a still preview from the selected video.
- **Trim video** takes start/end times in seconds and writes an MP4 under the `edited` subfolder. Review the output; this tool does not enforce any social-platform duration/aspect-ratio rule for you.
- **Convert file** supports MP4, WebM and MP3 outputs. Choose the target format first, then convert. Keep the original until you have checked the output.

### Transcribe speech

1. Select a video or audio file. Set the optional Groq key, model/language settings as needed, and decide whether to enable direct translation to English.
2. Choose **Transcribe speech and send to agent**. The service extracts audio, requests transcription from the configured provider/fallback, displays the text and writes transcript/text outputs alongside the media.
3. Review the transcript for names, numbers, language and timing. The tool can send transcript results/files through the GitHub connection; this writes content into the private repository/mailbox and its Git history, not just to your local disk. Do not upload private or sensitive media unless that storage is explicitly approved.
4. Use **Copy text** if you only need the displayed transcript locally. Use **Upload selected file to GitHub** only when you intend to share that selected file with the agent through the private repo.

## 7. Review and publish content

The Ready-to-publish tab reads complete queue items from the private mailbox and shows an Instagram-style preview with media, caption, CTA, content type, format, language and scheduled local time. Use **Refresh list** to reload.

### Review an item

Before approval, check at minimum:

- The media is the final, correct file and the preview matches it.
- Caption, language, CTA and any required affiliate disclosure are correct.
- Content type, topic, media format, account/platform, time and time zone match the intended operation.
- For Affiliate, the item is an Instagram **Reel only**. Affiliate Post, Carousel and Story are rejected by the current rule.
- For a DM/interactive item, understand that the app may create a per-post Zernio comment-to-DM automation after a successful Instagram post. The documented follow verification is one-step; repeated checking until someone follows is not guaranteed.
- Story posts are not assigned a location by this app. For Feed/Post, Reel and Carousel, the configured Manus location is used and the active Facebook Page must match the expected Page ID; platform/Zernio restrictions may still reject a post.

The app binds an approval to the queue JSON and associated media hashes. If the file changes after review, the old approval must not be assumed valid.

### Approve or return feedback

- Click the arrow/**Approve** control only when the exact item is approved. This records approval; it does not mean the post has already been published.
- Use **Comment** to write revision feedback. The item is returned to the agent for revision; this does not publish it.
- The scheduler checks approved items every 30 seconds while the app is running. If the app is closed, scheduled publishing pauses. An item more than five minutes late is failed for review and needs a new approved time; the app does not silently send it late.
- Before the scheduled action, ensure the owner has explicitly authorized that particular public post and any attached DM operation. Check the app’s published/failed result afterwards; an ambiguous attempt is not automatically retried.

### Affiliate reporting and DM are different paths

- **Affiliate:** only after an approved Reel is successfully published to Instagram, the app makes a one-way report to the existing Portal endpoint with the actual media ID. It does not register a Zernio DM automation or send an event callback/webhook to the Portal. Portal reporting success is separate from Instagram publishing success.
- **DM:** only for an explicitly configured interactive/DM queue item, the app uses Zernio’s per-post comment automation after successful Instagram publishing. This is distinct from Portal Ingest. No new Portal webhook or callback is created.
- No actual public publish, real DM or live Portal ingest is established by the existence of a queued item or by this guide.

For queue schema, destinations, retry/ambiguous-result behavior, analytics limits and further implementation detail, see [`marketing-app/PUBLISHING.md`](../marketing-app/PUBLISHING.md).

## 8. Daily Insights report

Two mechanisms produce the daily report, and they back each other up:

1. **Windows Task Scheduler job (08:00).** On normal Windows startup the app registers an interactive-token task that runs `BazinoMarketing.exe --daily-insights` at 08:00 local time, with catch-up when the computer becomes available. The task needs the app's configured GitHub and Zernio credentials.
2. **Launch check (past 08:00).** On every app launch after 08:00 local time the app reads one small marker file in its own data folder — `%LOCALAPPDATA%\BazinoMarketing\daily-report.marker.json` — which records the last report date it produced. If that file has no entry for today, the app builds the report immediately and commits it to the configured branch. Only this file is consulted at launch; if it is missing or unreadable it is treated as empty, so the report is produced again rather than skipped.

Both write a dated JSON report to `marketing-app-mailbox/insights/daily/YYYY-MM-DD.json` on the configured branch. Registration history: the task XML used by 0.8.1 contained the non-existent element `AllowStartIfOnBatteries`, so `schtasks.exe` rejected it ("The task XML contains an unexpected node"); the fixed definition uses the schema element `DisallowStartIfOnBatteries` and falls back twice if a machine rejects the XML.

Reports may include account totals, daily Reach, follower history, post analytics, demographics and active Story insights. Missing sections remain unavailable/marked as incomplete rather than being fabricated as zero. Provider delays, account thresholds, permissions, missing analytics add-ons or network failures can make sections unavailable. The task does not publish posts or send DMs. Check the app log and report metadata before using a report for decisions; the presence of Task Scheduler code does not prove that a task ran successfully on a particular day.

### The daily-report page (branch)

The «گزارش روزانه» page reads the newest file under `marketing-app-mailbox/insights/daily/`, shows reach per day as bars, follower history as a line, the engagement split as a donut, and the best posts of the period in a table (ordered by reach; compare like-for-like formats and ages). It shows clearly-marked sample numbers only in the screenshot/sample build — the CI screenshots are layout samples with fake numbers and are not account data. If no report exists yet, the page explains that instead of inventing numbers.

## 9. Logs and troubleshooting

Open **لاگ (Logs)** to search message/detail/category/request ID, filter by tool, show failures only, toggle live refresh, copy selected/all events, or open the log folder. Entries are JSON Lines under `%LOCALAPPDATA%\BazinoMarketing\logs\`. The redactor removes known secret values before log writing, but avoid entering secrets into free-text fields and review copied output before sharing.

| Symptom | Checks |
|---|---|
| A card says “not configured” | Reopen its Settings card, check URL/account/region and whether its key is saved; save, then test. Do not paste a key into a diagnostic message. |
| Connection test times out | Run Network Probe, inspect the proxy mode, and retry only after checking the service/network path. A network error does not prove an account or remote service is broken. |
| Mailbox is stopped or stale | Confirm GitHub settings and token, then use the Connection page’s immediate poll/start control. If remote access is not wanted, leave it stopped. |
| Kling is not found | Install Node, use the correct official regional CLI, choose Detect again, or set explicit `node.exe`/`kling.cmd` paths. Login uses a browser. |
| FFmpeg action fails | Confirm the selected file is a supported format and the FFmpeg path is valid; retain the original and inspect the app log. |
| No daily report appears | Check that the PC was available at/after the scheduled time, that Task Scheduler registration succeeded, GitHub and Zernio credentials are present, and the report/log records a success. |
| Portal Ingest status is unclear | The card only shows local token-presence status. There is no safe test button. Do not issue a sample POST; ask the operator to add a read-only check if needed. |
| Daily report did not appear | Check the app log for `instagram-insights` (`schedule`, `startup-check`, `daily-run`), the marker file `daily-report.marker.json`, and the task in Windows Task Scheduler. The report needs the GitHub and Zernio credentials and at least one available insights section. |
| Publishing outcome is ambiguous | Do not click/retry blindly. Check the app result and Zernio’s real post state first; the app intentionally avoids automatic duplicate posts after an ambiguous attempt. |

## 10. Updates and current limitations

The app updater is an agent mailbox command (`app.update`) that downloads a release asset, verifies its published SHA-256, then replaces/restarts the executable when requested. There is no general-purpose “update now” button in the current UI. The owner’s computer is already confirmed on 0.8.1; do not repeat the installation unless explicitly requested.

**Not current app features:** Instagram Saved-list browsing/unsaving; a Portal Ingest connection test or read-only health GET; a newly created Portal webhook/callback; automatic approval prompts for mailbox commands; and a guarantee that a connection probe implies live publish/DM permission. Older design proposals and archived desktop-app material are not the implementation source of truth.

## 11. Building and Updating the App by the Agent

This section explains how the agent builds a new Windows version of Jinus and applies that exact build to the owner’s running app. The build workflow also publishes a private GitHub pre-release; it is not a tests-only action. Run it only for an approved app change and after the owner has authorized the planned build/release.

### 11.1 Use the Active Windows Build Workflow

The active workflow is:

`.github/workflows/marketing-app-ci.yml`

Do not use `marketing-app/ci/marketing-app-ci.yml` as the live workflow; that file is only a reference copy.

The active workflow has two jobs:

- **Core tests (Linux):** runs on a matching `arena/**` branch when app source files or the workflow change. It runs the Core test suite and an informational cross-target WPF compile. The cross-target compile is configured as non-blocking; review its output, but use the Windows job as the actual Windows build check.
- **Windows build + screenshots + pre-release:** runs only when manually dispatched with `build_windows=true`, or when an eligible push commit message contains `[build]`. It depends on the Linux Core test job succeeding. It runs Windows tests, including the DPAPI round-trip test, publishes a self-contained Windows x64 executable, renders screenshots with sample data, creates `SHA256SUMS.txt`, and publishes a private GitHub pre-release.

For a deliberate new Windows build, use **Actions → marketing-app-ci → Run workflow**, select the approved session branch, and set `build_windows` to `true`. The default is `true`. A manual run therefore publishes a pre-release automatically when the required jobs pass.

The agent may start the same manual workflow with GitHub CLI:

```bash
gh workflow run marketing-app-ci.yml \
  --repo paymanshafayan/bazino-gamenet-portal \
  --ref arena/01a0f126-bazino-gamenet-portal \
  -f build_windows=true
```

Record the run ID and wait for it to finish. Do not treat a queued or failed run as a completed build.

(Note added by the agent, 2026-10-03: the fine-grained agent token of this workspace **cannot dispatch the workflow**: the API answers `HTTP 403 Resource not accessible by integration` for `POST /actions/workflows/<id>/dispatches`. The working route for the agent is therefore a commit whose message contains `[build]` on the active session branch, which starts the same Windows job and pre-release. A manual dispatch remains available in the GitHub UI to the owner.)

### 11.2 Prepare the Approved App Revision

1. Follow the approved implementation plan and make only the app changes covered by that approval.
2. If the app’s user-visible version is changing, update the `<Version>` value in `marketing-app/Directory.Build.props` to the intended semantic version before building. The GitHub pre-release tag is separate: the workflow creates a tag in the form `marketing-app-dev-<RUN_NUMBER>`. The run number is not the app version.
3. Commit the approved app source and version change to the fixed session branch `arena/01a0f126-bazino-gamenet-portal`. (Note added by the agent: this branch name comes from an earlier session. The active session branch is `arena/01a10048-bazino-gamenet-portal`, and the installed app is configured to it — that is the branch used below.) Keep mailbox state, private agent files, media and unrelated changes out of the commit.
4. Verify that the workflow will build the intended commit. A manual workflow run builds the selected branch’s current commit; it does not build uncommitted files from the agent’s workspace.

A push to an `arena/**` branch normally starts only the Linux test job. Do not add `[build]` to a commit message unless a Windows build and private pre-release are intended and authorized; that marker causes the Windows job and release step to run automatically.

### 11.3 Inspect the Build and Pre-release

After starting the workflow:

1. Wait for the **Core tests (Linux)** job to complete successfully. If it fails, do not proceed to installation.
2. For a Windows build, confirm that **Windows build + screenshots + pre-release** also succeeds. Review its Windows test results and `render-info.txt`; inspect the screenshots rendered from sample data.
3. Record the GitHub run number. The matching pre-release tag is `marketing-app-dev-<RUN_NUMBER>`.
4. Open that exact pre-release and verify that it contains:
   - `BazinoMarketing.exe`
   - `SHA256SUMS.txt`
   - the rendered screenshots and `render-info.txt`
5. Confirm the pre-release targets the intended commit and that the executable’s checksum is listed in `SHA256SUMS.txt`. The development executable is self-contained and unsigned; use only the expected private repository and verified release.

The app’s updater requires both the executable and its checksum file. Do not apply an incomplete release, a failed build, or an unverified asset.

### 11.4 Apply the Exact Build Through the App Mailbox

Before updating, read the app’s signed mailbox state and confirm that the installed app is online and `active/listening`. Check its reported version. If it already reports the intended version, do not update it again.

Send the update command through the paired agent mailbox, specifying the exact tag rather than relying on “latest”:

```json
{
  "cmd": "app.update",
  "args": {
    "tag": "marketing-app-dev-<RUN_NUMBER>",
    "apply": true,
    "delaySec": 10
  },
  "note": "Apply the validated Windows pre-release"
}
```

Replace `<RUN_NUMBER>` with the run number from the successful workflow. `apply` defaults to `true`; specifying it explicitly makes clear that the verified executable should be installed and the app restarted. The default restart delay is 10 seconds.

The app downloads `BazinoMarketing.exe` and `SHA256SUMS.txt` from the selected private release, verifies the executable’s SHA-256, then replaces the running executable and restarts. No manual installer step is required. Keep the mailbox connection available long enough for the command reply to arrive before the app restarts.

### 11.5 Verify the Updated App

1. Check the `app.update` reply. Confirm that `release` matches the requested tag, `applied` is `true`, the returned SHA-256 matches the release checksum, and there is no restart error.
2. Allow the app to restart. Read its signed state again and send a fresh `ping`.
3. Confirm that the app reports the intended semantic version and answers the ping. Record the workflow run, release tag, checksum, update result and verification time in the project handoff.
4. If the command fails, the checksum differs, the app does not return after restart, or the version is unexpected, report the observed error and stop. Do not claim the update succeeded or blindly retry it. Reconcile the release and app state first.

A successful build proves that the workflow produced an executable; it does not prove that the owner’s app installed or started it. A successful update reply and post-restart version check are required to report installation as complete.
