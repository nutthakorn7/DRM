# Phase 5Z External Browser Viewer Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a public `/share/` browser viewer shell that walks external guests through verification and opens a metadata-only viewer session.

**Architecture:** Reuse ASP.NET Core static files by adding `wwwroot/share/index.html`, `app.css`, and `app.js`, plus a `/share` to `/share/` redirect in `Program.cs`. The browser app calls the existing external share verification and viewer-session endpoints, keeps the verification session token in memory only, renders safe metadata, and keeps download, print, and export disabled.

**Tech Stack:** ASP.NET Core static files, vanilla HTML/CSS/JavaScript, xUnit/FluentAssertions static asset tests.

---

### Task 1: Static Share Viewer Route And Assets

**Files:**
- Modify: `src/Drm.Server/Program.cs`
- Create: `src/Drm.Server/wwwroot/share/index.html`
- Create: `src/Drm.Server/wwwroot/share/app.css`
- Create: `src/Drm.Server/wwwroot/share/app.js`
- Test: `tests/Drm.Server.Tests/ExternalShareViewerShellTests.cs`

- [x] **Step 1: Write the failing static shell tests**

Create `tests/Drm.Server.Tests/ExternalShareViewerShellTests.cs` with tests that:
- Assert `/share` redirects to `/share/`.
- Assert `/share/` serves HTML with `External Share Viewer`, `verificationStartForm`, `verificationConfirmForm`, `viewerStatus`, `Download disabled`, `Print disabled`, and `Export disabled`.
- Assert `/share/app.css` includes `.viewer-shell` and `.locked-preview`.
- Assert `/share/app.js` includes `/api/share-links/verification/start`, `/api/share-links/verification/confirm`, and `/api/share-links/viewer/session`.
- Assert `/share/app.js` does not contain `localStorage`, `sessionStorage`, `wrappedKey`, `ciphertext`, `decrypted`, `/unwrap`, `/download`, `print()`, or `exportOriginal`.

- [x] **Step 2: Run static shell tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareViewerShellTests
```

Expected: FAIL because `ExternalShareViewerShellTests` either does not compile before the test file is complete or the `/share` route/assets do not exist.

- [x] **Step 3: Implement the `/share` redirect**

In `src/Drm.Server/Program.cs`, add a redirect branch beside the existing `/admin` redirect:

```csharp
if (context.Request.Path.Equals("/share", StringComparison.OrdinalIgnoreCase))
{
    context.Response.Redirect("/share/");
    return;
}
```

- [x] **Step 4: Add the viewer HTML**

Create `src/Drm.Server/wwwroot/share/index.html` with:
- A public `External Share Viewer` title.
- A `verificationStartForm` for tenant ID, access token, and guest email.
- A `verificationConfirmForm` for verification ID and code.
- A `viewerStatus` area.
- A locked preview pane with disabled Download, Print, and Export buttons.
- Links to `/share/app.css` and `/share/app.js`.

- [x] **Step 5: Add the viewer CSS**

Create `src/Drm.Server/wwwroot/share/app.css` with:
- `.viewer-shell` responsive two-column layout.
- `.workflow` left-side verification surface.
- `.locked-preview` right-side metadata/preview surface.
- Disabled action button styling.
- Mobile layout under `760px`.

- [x] **Step 6: Add the viewer JavaScript**

Create `src/Drm.Server/wwwroot/share/app.js` with:
- `startVerification(event)` posts JSON to `/api/share-links/verification/start`.
- `confirmVerification(event)` posts JSON to `/api/share-links/verification/confirm`.
- `openViewerSession()` posts JSON to `/api/share-links/viewer/session`.
- A module-scoped `verificationSessionToken` variable that is cleared after viewer session open.
- `renderViewerSession(payload)` that fills safe metadata fields only.
- `renderError(response, fallbackMessage)` that shows `reasonCode` or a neutral `404` message.
- No browser storage API calls and no content/key release endpoint calls.

- [x] **Step 7: Run static shell tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareViewerShellTests
```

Expected: PASS.

### Task 2: Documentation And Full Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-05-15-phase-5z-external-browser-viewer-shell.md`

- [x] **Step 1: Document Phase 5Z**

Add a README section explaining `/share/`, the verification-to-viewer flow, and the boundary that this shell does not render content or release keys.

- [x] **Step 2: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all commands exit 0.

- [x] **Step 3: Commit implementation**

Commit message:

```bash
git commit -m "feat: add external browser viewer shell"
```

---

**Self-review**

- Spec coverage: Covers `/share/`, three-step guest verification/session flow, locked metadata preview, and no-key/no-content boundary.
- Placeholder scan: No implementation placeholders remain.
- Type consistency: Element IDs and endpoint paths are named consistently across tests, HTML, and JavaScript.
