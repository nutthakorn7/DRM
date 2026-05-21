# 11 — Engineer: full smoke test (every shipped sender feature)

> **For:** the engineer prepping the demo laptop. Run this **the day
> before** the customer arrives, when there is still time to fix
> things. The shorter [05-preflight-checklist.md](05-preflight-checklist.md)
> runs again 15 minutes before the customer; this doc is the deeper
> "everything works end-to-end" pass.
>
> **Time:** ~30 minutes if everything works, ~60 minutes including
> investigation if something does not.
>
> **Scope:** v1.7.0 — Stages 13 through 20. Sender flow, recipient
> verification, /me/ My Shares, self-revoke, bulk send.

---

## 0. Prerequisites — confirm before you start

You should already have done [08-engineer-windows-msi-setup.md](08-engineer-windows-msi-setup.md)
(MSI install + first-run sign-in). This doc assumes:

- [ ] zcrDRM Agent MSI installed from the **latest** master CI run.
      Confirm by right-clicking the tray icon → "About" — version
      string should read `1.7.0` or later.
      ```powershell
      Get-ItemProperty HKLM:\SOFTWARE\zcrDRM | Select Version
      ```
- [ ] Agent signed in as `demo@zcr.ai` (first-run dialog dismissed,
      MainWindow shows the title bar with email + name).
- [ ] Demo laptop has Outlook desktop installed AND Outlook is the
      Windows default mail client.
      ```powershell
      # Default protocol handler:
      (Get-ItemProperty 'HKCR:\mailto\shell\open\command').'(default)'
      # Expect: a path containing OUTLOOK.EXE
      ```
- [ ] Sample PDF on Desktop: `Q4-Sales-Contract-ABC-XYZ.pdf` (~2 MB).
- [ ] An email mailbox the engineer can read in real time — needed to
      receive the verification 6-digit code during recipient flow.
      Use a personal Gmail / Outlook / etc. account that is **not**
      the demo tenant's own email. Call this address `engineer@<your-real-domain>`
      in the steps below.
- [ ] Demo tenant + user IDs ready to type into `/me/` (from
      [09-prod-seeded-credentials.md](09-prod-seeded-credentials.md)).

---

## 1. Stage 13 — Quick Send actually generates `.drmx`

**What's shipped:** the right-click Quick Send button calls the same
encryption workflow the advanced Protect button uses. Result: a real
`.drmx` file appears on disk, the share-link verification flow lands
on a 200 page, and downloading the `.drmx` from `/share/` opens in
the viewer with the correct policy.

**Steps:**

1. On Desktop, **right-click `Q4-Sales-Contract-ABC-XYZ.pdf`** →
   `Protect with zcrDRM` → `Quick send (recommended)`.
2. Agent main window snaps open. **Confirm the title bar:**
   `zcrDRM Agent — Demo Engineer (demo@zcr.ai)`. Tenant ID, User ID,
   Policy Template should be pre-filled. The file picker drop-zone
   shows the file name.
3. In the recipient box type `engineer@<your-real-domain>` (the mailbox
   you can check).
4. Click **Send protected file**.
5. **Expected** within ~3 seconds:
   - Status line: `✅ Wrote Q4-Sales-Contract-ABC-XYZ.pdf.drmx + Outlook opened with it attached. Just hit Send.`
   - On Desktop, a new file: `Q4-Sales-Contract-ABC-XYZ.pdf.drmx`.
6. Verify the `.drmx` is real (not zero bytes):
   ```powershell
   Get-Item "$env:USERPROFILE\Desktop\Q4-Sales-Contract-ABC-XYZ.pdf.drmx" `
     | Select Name, Length
   # Expect Length around 2 MB (slightly bigger than the source — header overhead).
   ```

**Pass:** status line matches + `.drmx` on disk + Length is ~source-size.

**Fail recovery:**
- Status `Share-link failed: HTTP 400` — `ClientApiKeyBox` (advanced
  panel) is missing or has the wrong value. Paste `DEMO_ADMIN_KEY`.
- Status `Send failed: <message>` — agent could not reach the server.
  Test connectivity: `curl https://drm.zcr.ai/healthz`.
- `.drmx` not on Desktop — workflow likely threw mid-encrypt. Open
  `%LOCALAPPDATA%\zcrDRM\logs\` (if logging is enabled) or run from
  PowerShell so console errors surface.

---

## 2. Stage 14 — Outlook COM auto-attach

**What's shipped:** when Outlook is the default mail client, the
agent uses Outlook COM (`Outlook.Application` ProgID, late-bound)
to attach the `.drmx` directly to the new email — no manual drag.

**Steps:**

1. After Step 1 completes, an Outlook compose window should be open
   already. Find it (`Alt-Tab` or check the taskbar — Outlook icon
   should have a new window).
2. **Expected** in the compose window:
   - `To:` is `engineer@<your-real-domain>`
   - `Subject:` is `Encrypted file: Q4-Sales-Contract-ABC-XYZ.pdf.drmx`
   - `Body:` contains "The encrypted .drmx file is already attached
     to this email — just click Send." then 1-2-3 instructions then
     a `https://drm.zcr.ai/share/?token=...` URL on a line by itself.
   - **Attachments tray** has `Q4-Sales-Contract-ABC-XYZ.pdf.drmx`
     listed already.
3. Click **Send** in Outlook.
4. Check the recipient mailbox (`engineer@<your-real-domain>`) for the
   email. The `.drmx` should be a real attachment, ~2 MB.

**Pass:** all four bullets in step 2 visible without sender intervention.

**Fail recovery:**
- Compose opened but Body says "BEFORE SENDING THIS EMAIL: attach
  the file at..." instead of "already attached" — means COM
  activation hit a Trust Center prompt or the Attachments.Add call
  failed silently. Fall back: drag the `.drmx` from Desktop into the
  Outlook composer manually, hit Send.
- Outlook compose window did not open at all — Outlook is not the
  registered default mail client. Set it: Settings → Apps → Default
  apps → Mail → Outlook. Restart agent.
- Composer opened but it's Mail / Thunderbird / etc. — same fix.

---

## 3. Stage 15 — Recent-recipients dropdown

**What's shipped:** the recipient ComboBox is backed by a persistent
MRU store at `%LOCALAPPDATA%\zcrDRM\recent-recipients.json`. Each
successful send adds the recipient to the top.

**Steps:**

1. Close the Agent window completely (don't just minimize — quit).
2. Re-open it from the tray icon or Start Menu.
3. Right-click any file → Quick send → click the recipient field's
   **chevron** (▼ on the right).
4. **Expected:** dropdown shows `engineer@<your-real-domain>` from
   Step 1.5 above. If you've done previous test runs, more entries
   may be present (capacity 20).
5. Verify the persistence file:
   ```powershell
   Get-Content "$env:LOCALAPPDATA\zcrDRM\recent-recipients.json"
   # Expect a JSON array of {Email, LastUsedUtc} records, MRU first.
   ```

**Pass:** dropdown shows the recipient + JSON file has the record.

**Fail recovery:**
- Dropdown empty — JSON file missing or corrupt. Inspect with the
  command above; if the file says `[]` or is broken, the store will
  rebuild itself on the next successful send (Stage 15 has corrupt-
  file recovery). Test by sending another Quick Send.

---

## 4. Stage 17 — Mail-client warning banner

**What's shipped:** the agent probes `HKEY_CLASSES_ROOT\mailto\shell\open\command`
at MainWindow load. If empty, a yellow banner appears at the top of
the Quick Send tab.

**⚠ Destructive test — do this only if you have time to restore.**

**Steps:**

1. Open `regedit.exe` as admin. Navigate to
   `HKEY_CLASSES_ROOT\mailto\shell\open\command`.
2. **Take a screenshot of the `(Default)` value** — you'll need to
   restore it. Or run:
   ```powershell
   (Get-ItemProperty 'HKCR:\mailto\shell\open\command').'(default)' `
     | Out-File "$env:USERPROFILE\Desktop\mailto-handler-backup.txt"
   ```
3. Rename the `command` key (right-click → Rename) to `command_backup`.
4. Close + re-open the Agent.
5. **Expected:** at the top of the Quick Send tab, a yellow banner:
   `⚠ No default mail client detected. Quick Send will still encrypt
   your file, but the email composer won't auto-open. Set a default
   at Settings → Apps → Default apps → Mail.`
6. **Restore:** rename `command_backup` back to `command`. Close +
   re-open the Agent — banner should now be hidden.

**Pass:** banner appears when the key is missing, disappears when restored.

**Fail recovery:**
- Banner did not appear — agent might be caching. Quit fully via tray
  → Exit, then relaunch from Start Menu.
- Restore step left the registry broken — paste the value from your
  screenshot back into a fresh `command` key under `mailto\shell\open\`.

**Cleanup confirmation:**
```powershell
(Get-ItemProperty 'HKCR:\mailto\shell\open\command').'(default)'
# Must NOT be empty, must contain OUTLOOK.EXE or your chosen mail client.
```

---

## 5. Recipient flow — `/share/` verification

**What's shipped (Stages 10 + 12):** recipient gets the email,
clicks the share URL, lands on `/share/` with a "Verify access"
two-step form, enters their email + 6-digit code from the
verification email, lands on the file detail page with permission
badges + download button.

**Steps:**

1. From the email you received in Step 1.4, **copy** the
   `https://drm.zcr.ai/share/?token=...` URL.
2. Open a **fresh Incognito / Private window** (cookies must not
   leak from any existing sessions).
3. Paste the URL. Click open.
4. **Expected:** `/share/` page with:
   - zcrDRM wordmark top-left
   - Heading "Open shared file"
   - Step 1: email field — pre-filled with `engineer@<your-real-domain>`
   - "Send verification code" button.
5. Click **Send verification code**.
6. Check the engineer mailbox — within 30 seconds an email arrives
   from `noreply@zcr.ai` (or your configured From) with a 6-digit
   code. Subject usually contains "verification".
7. Back on `/share/`, the form has advanced — Step 2 input expects
   the 6-digit code.
8. Paste the code. Click **Open viewer session**.
9. **Expected** on success:
   - "What to do next" panel visible (Stage 10).
   - Permission badges visible — `👁 View` should be green, others
     depend on what the sender allowed.
   - **Download .drmx** button visible.
   - **Open in zcrDRM Viewer** link (Stage 10).
10. Click **Download .drmx** — file downloads. Should be the same
    `.drmx` you sent (~2 MB, identical SHA256 to the one on Desktop).
    ```powershell
    Get-FileHash "$env:USERPROFILE\Desktop\Q4-Sales-Contract-ABC-XYZ.pdf.drmx" -Algorithm SHA256
    Get-FileHash "$env:USERPROFILE\Downloads\Q4-Sales-Contract-ABC-XYZ.pdf.drmx" -Algorithm SHA256
    # The two hashes MUST match.
    ```
11. Double-click the downloaded `.drmx`. **Expected:** zcrDRM Viewer
    opens, file decrypts, watermark visible.

**Pass:** every bullet of step 9 visible + SHA256 matches + viewer opens with watermark.

**Fail recovery:**
- Verification email didn't arrive within 60 seconds — check
  spam folder. If still missing, check SMTP delivery on the server:
  ```bash
  ssh root@drm.zcr.ai 'docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml logs -f drm-server' \
    | grep -i 'verification\|smtp'
  ```
- Verification email arrived but code "expired" — codes expire in
  10 minutes. Click "Resend code" to start over.
- HTTP 500 on verification/start — Resend domain not verified or
  the recipient's domain blocks the from-address. Use the engineer
  mailbox check at the top of section 0.
- File detail page loaded but permission badges all gray —
  back-end stored `Permission.None`. Re-send with explicit View.

---

## 6. Stage 18 — My Shares table

**What's shipped:** `/me/` has a "My recent shares" section showing
the caller's last 10 shares with recipient / dates / opens /
permissions / status. Auto-refreshes after each send.

**Steps:**

1. Open <https://drm.zcr.ai/me/> in a normal (not Incognito) browser.
2. Expand the "You are signed in as" details panel. Type:
   - Tenant ID: `<from 09-prod-seeded-credentials>`
   - User ID: `<demo@zcr.ai user ID, see 09>`
3. Scroll to "My recent shares".
4. **Expected:**
   - A table with header row: Recipient | Sent | Expires | Opens | Permissions | Status.
   - At least one row from the Step 1 send: recipient
     `engineer@<your-real-domain>`, Status pill green ("Active"),
     Opens `1/1` if you already opened from /share/ — `0/1` otherwise.
5. Click the **Refresh** button (↻) in the header. Table reloads.

**Pass:** row visible with correct recipient + status pill.

**Fail recovery:**
- Section hidden — Tenant ID + User ID fields are blank. Open the
  details panel and fill them in.
- Section visible but table says "Nothing here yet" — the session
  fields are wrong (User ID does not match the actual owner). Fix
  in the panel.
- HTTP error in browser console (F12 → Network → `/api/me/shares`)
  — check the response. 400 = bad tenant/user ID; 500 = server
  error, check server logs.

---

## 7. Stage 19 — Bulk Send

**What's shipped:** comma/semicolon-separated emails in the
recipient field → one encrypt + N share-links + N composer windows.
Each recipient sees only their own token.

**Steps:**

1. Right-click any file on Desktop → Quick send (recommended).
2. In the recipient field type:
   `alice-test@<your-real-domain>, bob-test@<your-real-domain>`
   (or use real mailboxes you can read; comma OR semicolon both work).
3. Click **Send protected file**.
4. **Expected** within ~5 seconds:
   - Status line counts progress: `Creating share link 1/2 (alice-test...)…`
     then `Creating share link 2/2 (bob-test...)…`
   - Final status: `✅ Wrote <file>.drmx + created 2 share link(s), opened 2 composer(s) with attachment already inlined.`
   - **Two Outlook compose windows open** — one per recipient. Each
     has the same `.drmx` attached but a DIFFERENT share URL in the
     body.
5. Verify the share URLs differ:
   - In window 1, copy the `https://drm.zcr.ai/share/?token=...` URL.
   - In window 2, copy the share URL.
   - The `token=` query value MUST be different across the two URLs.
6. Send both emails (or close the windows without sending — your call).
7. Refresh `/me/` My Shares — should now show 2 NEW rows for the same
   filename but different recipients.

**Pass:** all four bullets of step 4 + tokens differ + 2 rows appear in My Shares.

**Fail recovery:**
- Only 1 composer opened — one of the share-link POSTs failed. Check
  the final status text — it should say `⚠ 1 failed: <email> [<reason>]`.
  Usually means the email was malformed.
- Status says "0 share link(s)" — both POSTs failed. Check
  `ClientApiKeyBox` and server connectivity.
- Tokens are identical — you're reading the wrong URLs. Token is
  the query parameter starting with `accessToken=` (NOT `tenantId=`).

---

## 8. Stage 20 — Self-revoke from My Shares

**What's shipped:** Revoke button on each active row in `/me/` My
Shares. Calls `/api/me/shares/{id}/revoke`; flips Revoked=true on
the share-link; subsequent guest verification attempts fail.

**Steps:**

1. Open `/me/` (with Tenant ID + User ID filled in from Section 6).
2. Find a row in My Shares that has status pill green (Active) — use
   one of the Stage 19 bulk-send rows.
3. Click the **Revoke** button (small red-bordered button in the
   rightmost column).
4. **Expected:** browser `confirm()` dialog:
   `Revoke this share link? The recipient will lose access immediately.`
5. Click **OK**.
6. **Expected** within ~1 second:
   - Row's Status pill flips to red `Revoked (self_revoked)`.
   - Revoke button disappears (column blank for that row).
7. Open one of the just-revoked share URLs in Incognito. Try to
   verify (Step 5 of Section 5). **Expected:** the verify endpoint
   returns an error response, the page shows
   `This share link has been revoked` or equivalent.

**Pass:** status flips immediately + revoked URL fails verification.

**Fail recovery:**
- Confirm dialog appeared but Status pill didn't flip — server
  rejected the revoke. F12 → Network → POST `/api/me/shares/.../revoke`
  — likely 404 (User ID mismatch) or 400 (tenant mismatch).
- Status pill flipped but revoked URL still works — clear browser
  cache; the previous /share/ page may have cached the redeemable
  session state. Or the file underlying the share is still open in
  the viewer.

---

## 9. End-to-end pass summary

Once all sections (1-8) pass, the demo laptop is verified for v1.7.0.
Print this page and check off each section.

- [ ] §1 Stage 13 — Quick Send produces `.drmx`
- [ ] §2 Stage 14 — Outlook auto-attach
- [ ] §3 Stage 15 — Recent-recipients dropdown
- [ ] §4 Stage 17 — Mail-client warning banner (don't forget to restore registry!)
- [ ] §5 Recipient verification + download + viewer open
- [ ] §6 Stage 18 — My Shares table
- [ ] §7 Stage 19 — Bulk Send (2+ recipients)
- [ ] §8 Stage 20 — Self-revoke

**Engineer sign-off:**

Tested by: ____________________
Date: ____________________
Version verified: 1.7.0 (master commit hash: ____________________)
Outstanding issues: ____________________

---

## 10. If you found a bug

1. **Don't try to fix it on stage.** Note it down + tell the presenter
   which feature to skip.
2. Capture screenshot + the exact status text or error.
3. Check `06-fallback-plan.md` for the closest matching scenario.
4. Slack/LINE/Teams Pop with the screenshot + which §-section failed.
5. Demo can almost always proceed using web fallbacks — `/me/` for
   send, `/share/` for receive — see `06-fallback-plan.md`.

---

## 11. Cleanup after testing

You probably created several test share-links and `.drmx` files
during this pass. Clean up to avoid confusing the customer demo:

- [ ] Delete test `.drmx` files from Desktop.
- [ ] In `/me/` My Shares, revoke any rows you don't want visible
      during the live demo.
- [ ] Or, easier: re-seed the demo tenant with a clean state per
      `09-prod-seeded-credentials.md`.
- [ ] Confirm `recent-recipients.json` has only addresses you're OK
      with the customer briefly seeing in the ComboBox (the demo
      script may pop the dropdown).

---

## 12. Performance baseline (optional)

Record these once on the verified laptop — comparison points if
something feels slow during the customer demo:

| Step | Expected duration | Yours |
|---|---|---|
| Right-click → menu appears | < 0.5 s | ___ s |
| Quick send → tray window opens | < 1 s | ___ s |
| Click "Send protected file" → Outlook opens (Outlook already running) | 2-4 s | ___ s |
| Click "Send protected file" → Outlook cold-start opens | 5-8 s | ___ s |
| `/share/` verification email arrives | 5-30 s | ___ s |
| `/share/` enter code → file detail page | < 1 s | ___ s |
| Click Download .drmx → file in browser downloads | < 1 s | ___ s |
| Double-click .drmx → viewer opens | 1-2 s | ___ s |

If your number is significantly outside the expected range, mention
it to Pop before the demo. Usually it's network latency on first
request; subsequent requests are faster.
