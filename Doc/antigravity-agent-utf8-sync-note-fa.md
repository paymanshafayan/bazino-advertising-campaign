<div dir="rtl" align="right">

# UTF-8 sync hygiene — mandatory note for the Antigravity agent

> Short rule sheet. Read before the next cross-repo sync. Written after a real incident (2026-10-04).

## What happened
- Your sync commit "sync(app): copy changes from campaign repo [build]" copied `PublishQueueView.xaml` into the portal repo **with corrupted (mojibake) Persian text**: every Persian label was re-encoded (`محتوای …` → `Ù…Ø­ØªÙˆØ§ÛŒ …`).
- Result: builds `dev-62 / dev-64 / dev-65` shipped a review screen with garbled Persian labels. The portal repo is where the Windows app is built; your copy is not.
- The same corruption exists **in your repo copy right now** in three files:
  - `marketing-app/src/BazinoMarketing.Core/Publishing/PublishQueueModels.cs`
  - `marketing-app/tests/BazinoMarketing.Core.Tests/PublishQueueTests.cs`
  - `marketing-app/src/BazinoMarketing.App/Views/PublishQueueView.xaml`

## Rules from now on
1. **Never copy file text through a lossy path.** Read and write files as raw UTF-8 bytes. Do not paste file contents through tools that decode as Latin-1/Windows-1252 and re-encode.
2. **For any file that contains non-ASCII text, copy the file verbatim** (git object / raw download), not via string APIs with a guessed encoding.
3. **Before every sync commit, run a mojibake check** on the changed files. If a file contains sequences such as `Ø§`, `Ù…`, `Ù†`, `ÛŒ`, `â€`, it is corrupted — do not commit it.
4. **Prefer syncing forward from the portal repo** (it is the build source): the canonical files for the story fix and the Persian labels now live there at the head of branch `arena/01a10048-bazino-gamenet-portal` (commit `39d562c047b2ecccb59ee8d83de29343c2844c27` and newer). Pull those two/three files from there instead of overwriting them from your copy.
5. **Never build a release from your copy** while it contains mojibake; versions built from the portal repo are the ones the owner installs.

## If you want to repair your copy
- The corrupted text in all three files is **fully reversible**: decode the stored text as Windows-1252 bytes and re-interpret as UTF-8.
- Verification: after repair, the reversed `PublishQueueModels.cs` must be identical to the portal copy except for the two explanatory comment lines; the reversed `PublishQueueView.xaml` must be identical to the repaired portal file; and `PublishQueueTests.cs` must run green.
- After repair, do not re-encode: save UTF-8 (with or without BOM, one consistent choice per file), and re-run the mojibake check from rule 3.

## Out of scope
The Story publishing fix and the version numbers stay as they are in the portal repo; do not renumber or re-release.
