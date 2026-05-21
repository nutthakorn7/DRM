# 08 — Engineer: install the Windows agent MSI on the demo laptop

> **For:** the engineer doing demo prep on the Windows machine that will be in front of the customer.
>
> **Time:** ~10 minutes, end-to-end.
>
> **Run this _before_ the customer arrives.** The MSI is unsigned, so the very first install on a fresh machine shows a SmartScreen "Unknown publisher" warning. Doing it ahead of time means the customer never sees that screen — they only see the working agent.

## 1. What you're installing

`zcrdrm-agent.msi` — the Windows agent built by CI. It contains:

- `Drm.Agent.Tray.Windows.exe` — the tray app where you click "Protect"
- `Drm.Viewer.Windows.exe` — what opens when you double-click a `.drmx` file
- A self-contained .NET 10 runtime (so you don't need to install one)

On install it registers:

- File association: `.drmx` files open in the viewer
- Right-click menu: any file gets "Protect with zcrDRM" with three sub-actions
- Server URL: baked in as `https://drm.zcr.ai` so the agent finds the server on its own
- Start Menu shortcut: "zcrDRM Agent"

## 2. Get the MSI

The MSI is uploaded as an artifact by every CI run on master.

1. Open <https://github.com/nutthakorn7/DRM/actions/workflows/ci.yml?query=branch%3Amaster>
2. Click the most recent **green** run (status: ✅).
3. Scroll to the **Artifacts** section at the bottom.
4. Download `zcrdrm-agent-msi`. It comes as a `.zip`.
5. Extract — inside is `zcrdrm-agent.msi` (~50 MB).

> **If the latest run is red:** scroll back to find the most recent green one. The MSI build job is `MSI — build & verify on Windows`; as long as that job is green, the MSI is good.

## 3. Install on the demo laptop

Copy `zcrdrm-agent.msi` to the demo laptop, then:

**Easy way (recommended):** double-click `zcrdrm-agent.msi`.

- SmartScreen will show **"Windows protected your PC"** with an "Unknown publisher" warning. Click **More info** → **Run anyway**. *(This is normal for unsigned MSIs — we're buying a code-signing cert after the demo.)*
- UAC will ask for admin permission. Click **Yes**.
- The MSI wizard runs Next → Next → Install → Finish. No options to pick.

**Silent way (if you want to script it):**

```powershell
msiexec /i zcrdrm-agent.msi /qn /l*v install.log
```

`/qn` means no UI; `/l*v` writes a verbose install log to `install.log` in case anything goes wrong.

## 4. Verify the install landed

Open PowerShell (no need for admin) and run:

```powershell
# Files
Test-Path "C:\Program Files\zcrDRM\Drm.Agent.Tray.Windows.exe"   # expect True
Test-Path "C:\Program Files\zcrDRM\Drm.Viewer.Windows.exe"       # expect True

# Server URL baked in
Get-ItemProperty HKLM:\SOFTWARE\zcrDRM | Select ServerUrl, Version
# expect ServerUrl = https://drm.zcr.ai

# File association
(Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\.drmx').GetValue('')
# expect zcrDRM.ProtectedFile.1

# Right-click menu
(Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\*\shell\zcrDRMProtect').GetValue('MUIVerb')
# expect "Protect with zcrDRM"
```

All four should return the expected values. If any is empty, the MSI didn't install cleanly — `msiexec /x zcrdrm-agent.msi` to uninstall and retry, or check `install.log` from the silent-install command.

## 5. First-run handshake

From Start Menu, click **zcrDRM Agent**. The first time you launch it:

1. A small dialog appears: **"Welcome to zcrDRM — Let's get you signed in"**.
2. Type any work email that exists in the demo tenant — e.g. `admin@example.test` (use whatever email was used to seed the demo tenant via `/admin/`).
3. Click **Sign in**.
4. Behind the scenes the agent calls `https://drm.zcr.ai/api/agent/discover?email=<that-email>`, gets back the tenant ID + user ID + default policy template, encrypts that bundle with Windows DPAPI, and saves it to `%LOCALAPPDATA%\zcrDRM\identity.bin`.
5. The dialog closes, the main "DRM Agent" window opens with **everything already filled in** — Server URL, Tenant ID, User ID, and Policy Template ID.

If you see the dialog say *"We couldn't find <email>"* — the email isn't registered in the tenant. Open `https://drm.zcr.ai/admin/` first and create a user, then retry.

> **You only do this once.** Subsequent launches read `identity.bin` and skip straight to the main window with all fields pre-filled.

## 6. Smoke-test the right-click flow

This is what the customer will see on stage. Drive it once to confirm it works.

**Before you start:** confirm a mail client is installed and set as
the default (Settings → Apps → Default apps → "Mail"). Stage 14
prefers Outlook (it can attach the .drmx programmatically via COM)
but Thunderbird / Mail.app / etc. work too — they just open as a
`mailto:` composer without the attachment pre-filled. **If no default
is configured at all** the mailto silently no-ops and the audience
sees nothing happen. **Recommended for the demo:** install Outlook
on the demo laptop and set it as default — that's the path that
gives the cleanest "one-click Send" story.

1. Drop any test PDF (or `.docx`, `.xlsx`) onto the Desktop.
2. **Right-click it → Protect with zcrDRM → Quick send (recommended).**
3. The tray's "Quick Send" tab opens with the file pre-selected.
4. Type a recipient email (use a real address you can check), click **Send protected file**.
5. Three things should happen, in this order:
   - Status line — **with Outlook as default** (Stage 14):
     `✅ Wrote <filename>.drmx + Outlook opened with it attached. Just hit Send.`
     **With any other default mail client** (mailto fallback):
     `✅ Wrote <filename>.drmx. Share URL copied + email composer opened — attach the .drmx and send.`
   - File Explorer: `<filename>.drmx` appears next to the source file.
   - Mail composer opens with `To:`, `Subject: Encrypted file: <name>.drmx`, body pre-filled with the share URL, AND — Outlook only — the `.drmx` already sitting in the attachments tray.
6. **Outlook path:** click **Send**. Done. **Other clients:** drag the `.drmx` from Explorer into the composer as an attachment, then click Send.
7. On the recipient side: open the share URL → enter the same recipient email → enter the 6-digit code from the verification email → land on the file page → click **Download .drmx**.
8. Back on the demo laptop: double-click either the downloaded `.drmx` or the one the agent wrote → it should open in the zcrDRM viewer with the watermark overlay.

If step 5 shows `Share-link failed: HTTP 400` — the .drmx was still written (look in Explorer), but the share-link request was rejected. Most likely cause: the demo tenant's admin key is wrong; check `ClientApiKeyBox` in the agent's main window matches `DEMO_ADMIN_KEY` in your shell.

## 7. Screenshots for the demo deck

Capture these on the demo laptop while everything's working:

- [ ] Right-click menu showing "Protect with zcrDRM" → submenu with three actions
- [ ] First-run dialog (do this once on a throwaway VM — the demo laptop should be past first-run already)
- [ ] Main "DRM Agent" window with `Title = "zcrDRM Agent — <Name> (<email>)"` — that title proves the cache is working
- [ ] A protected `.drmx` open in the viewer with the watermark overlay visible

## 8. Re-run the handshake if something gets weird

If you need a clean start (e.g. the cache got into a bad state, or you want to demo the first-run flow itself):

```powershell
Remove-Item "$env:LOCALAPPDATA\zcrDRM\identity.bin" -Force
```

Next launch will show the welcome dialog again.

## 9. Uninstall (for clean handoff)

When you're done with this machine:

```powershell
msiexec /x zcrdrm-agent.msi /qn
```

Or via **Settings → Apps → zcrDRM Agent → Uninstall**.

This removes everything: files, registry, file associations, right-click menu.

## 10. If anything is broken — what to check first

| Symptom | Likely cause | Fix |
|---|---|---|
| First-run dialog says "We couldn't find <email>" | User isn't in tenant on `drm.zcr.ai` | Create user via `/admin/` first |
| Dialog hangs at "Connecting to drm.zcr.ai…" | Laptop has no internet / corporate proxy blocking | Test `curl https://drm.zcr.ai/healthz` from the laptop |
| Right-click menu doesn't show "Protect with zcrDRM" | Explorer needs restart after MSI install | `taskkill /im explorer.exe /f && explorer.exe` |
| Double-click `.drmx` opens Notepad instead of viewer | File association overridden by Windows | Settings → Apps → Default apps → search `.drmx` → set to "zcrDRM Viewer" |
| Tray exits immediately on launch | DPAPI cache file is corrupt | `Remove-Item "$env:LOCALAPPDATA\zcrDRM\identity.bin"` and relaunch |

If you hit something that's not on this list, grab a screenshot and message Pop. Don't try to debug live during the demo — fall back to the script in [06-fallback-plan.md](06-fallback-plan.md).

## 11. Next step: full pre-demo smoke

The §6 quick smoke above covers Stage 13 only. **The day before the customer arrives, run the full Stage 13-20 smoke test in [11-engineer-full-smoke-test.md](11-engineer-full-smoke-test.md)** — explicit step-by-step for every shipped sender feature (Outlook auto-attach, recent recipients, mail-client warning, My Shares, bulk send, self-revoke), with expected results, failure modes, and recovery commands. ~30 minutes if everything works.
