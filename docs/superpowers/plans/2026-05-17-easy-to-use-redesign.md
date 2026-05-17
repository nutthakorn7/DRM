# Easy-to-Use Redesign — Make DRM Workable for Non-Technical Users

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Each task ships UI on **both** admin console and Windows desktop per the project's standing rule.

**Goal:** Take DRM from "engineering preview" UX (C+ grade) to "non-technical user can ship a protected file in ≤ 60 seconds without training" (B+ grade). Today the product is feature-complete (98% FinalCode parity) but Admin/IT-only — sales reps, lawyers, HR staff cannot operate it.

**Research basis (in-repo):**
- Kiteworks 2024 survey — "ease of use" is the #1 DRM requirement, beating content protection
- Seclore — "no agent/plugin" + 30% faster file-open is marketed
- CapLinked FileProtect — 3-step workflow (Upload → Enable DRM → Track)
- Locklizard — single "Publish" button, "keys managed for you"
- Digify — "no training needed, started in minutes"
- IRM market survey (CloudNuro 2025) — top adoption barriers: 47% integration complexity, 42% user resistance, 35% admin overhead

**Tech Stack:** existing — `Drm.Server` (.NET 10 minimal API + SQLite), `Drm.Server/wwwroot/admin` (vanilla HTML/JS, cream theme), `Drm.Agent.Tray.Windows`, `Drm.Viewer.Windows`, `Drm.Cli`. No new languages introduced.

---

## Personas (drives every design decision)

| # | Persona | Primary task | Technical level | Daily DRM use? | Today's UX cost |
|---|---|---|---|---|---|
| **P1** | Sales Rep | Send pitch deck to prospect | None | Weekly | Cannot use — needs IT to provision |
| **P2** | Lawyer / Partner | Send case docs to co-counsel + client | None | Daily | Cannot use |
| **P3** | HR / Payroll | Send salary letter to employee, expire 30 days | Basic | Monthly | Cannot use |
| **P4** | Executive | Distribute board deck, no print / no photo | None | Quarterly | Cannot use |
| **P5** | Researcher / R&D | Share CAD assets inside design team only | Basic | Daily | Cannot use |
| **P6** | IT Admin | Configure tenants, audit, policies | Expert | Daily | Works (98% parity) |

**Today's DRM serves persona P6 only.** P1–P5 are blocked by:
- Required GUID typing (TenantId, UserId, DeviceId, FileId — 4 UUIDs per protect action)
- No "Share securely" right-click in Explorer / Office / Outlook
- No simple "drag file → enter recipient email → done" flow
- 19 admin panels visible to anyone with admin key
- Tray window is 1040 px tall with 3 drop zones + 6 status dots — overwhelms first-time user
- Browser viewer needs admin key fetched manually

---

## Self-rated UX scorecard (research-derived rubric)

| Research-backed principle | Source | Current score | Target after this plan |
|---|---|---|---|
| Workflow integration > separate tool | Kiteworks | 5/10 | 9/10 |
| Agentless / single-click access | Seclore | 7/10 | 9/10 |
| Single-click protect | Locklizard | 3/10 | 9/10 |
| Progressive disclosure | UXmatters | 3/10 | 8/10 |
| Right-click Explorer integration | FinalCode | 4/10 | 9/10 |
| Drag-and-drop default | FinalCode | 6/10 | 9/10 |
| Real-time status badges | UXmatters | 8/10 | 9/10 |
| Persona switch | UXmatters | 2/10 | 9/10 |
| Explorer-like browse | FinalCode 5.3 | 2/10 | 8/10 |
| Tooltip glossary for jargon | UXmatters | 2/10 | 9/10 |
| **Total** | | **42/100 (C-)** | **88/100 (B+)** |

---

## File structure — new code surface

```
src/Drm.Server/
  Models/
    PersonaProfile.cs                ← NEW (defines P1-P6 capability matrix)
    QuickShareLink.cs                ← NEW
  Endpoints/
    PersonaEndpoints.cs              ← NEW (GET /api/me/persona)
    QuickShareEndpoints.cs           ← NEW (POST /api/me/share — 1-call protect)
  wwwroot/
    me/                              ← NEW persona-switched landing page
      index.html
      app.js
      app.css
    admin/
      index.html                     ← refactor: collapse 19 panels into 5 groups
      app.js                         ← persistence (localStorage), keyboard `/`
      app.css                        ← collapsible-panel styles
      glossary.json                  ← NEW tooltip dictionary
      onboarding.html                ← NEW 60-second tour

src/Drm.Agent.Tray.Windows/
  Views/
    QuickProtectView.xaml            ← NEW dominant single-zone view
    AdvancedView.xaml                ← refactor: move old fields here
  MainWindow.xaml                    ← becomes a TabControl host

src/Drm.Viewer.Windows/
  HelpOverlay.xaml                   ← NEW first-run F1 overlay

src/Drm.Agent.Shell.Windows/         ← NEW project (Windows shell extension)
  Drm.Agent.Shell.Windows.csproj     ← C++/.NET COM in-proc server
  ProtectContextMenuHandler.cc       ← right-click "Protect with DRM"
  install.ps1                        ← regsvr32 helper

src/Drm.Cli/
  Commands/
    ShareCommand.cs                  ← NEW `drm share file.pdf --to bob@x.com`

tests/Drm.Server.Tests/
  PersonaEndpointsTests.cs
  QuickShareEndpointsTests.cs
```

---

## Bite-sized task breakdown

### Task 1: Persona profile model + identity bootstrap

**Why first:** every UX downstream depends on "who is this user, what can they do." Today everyone is implicitly Admin.

**Files:**
- Create `src/Drm.Server/Models/PersonaProfile.cs`
- Modify `src/Drm.Server/Entities.cs` — add `TenantUserPersonaEntity (TenantId, UserId, Persona, AssignedAtUtc)`
- Modify `src/Drm.Server/Program.cs` — SQLite migration
- Create `src/Drm.Server/Endpoints/PersonaEndpoints.cs`
- Test: `tests/Drm.Server.Tests/PersonaEndpointsTests.cs`

#### Step 1: Write the failing test

```csharp
[Fact]
public async Task Get_persona_returns_default_employee_for_unassigned_user()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var resp = await client.GetFromJsonAsync<PersonaResponse>(
        $"/api/me/persona?tenantId={tenantId}&userId={userId}");
    resp!.Persona.Should().Be("Employee");
    resp.CanProtect.Should().BeTrue();
    resp.CanRevoke.Should().BeFalse();
    resp.CanAdmin.Should().BeFalse();
}
```

Run: `dotnet test --filter "Get_persona_returns_default"` — Expected: FAIL ("PersonaResponse not defined").

#### Step 2: Define `PersonaProfile`

```csharp
public enum DrmPersona
{
    Employee = 0,        // P1, P3 — protect + recipient lookup, no admin
    KnowledgeWorker = 1, // P2, P5 — + revoke own files + bulk send
    Executive = 2,       // P4 — view-only access to dashboard, can revoke
    Admin = 3            // P6 — everything
}

public sealed record PersonaCapabilities(
    bool CanProtect,
    bool CanRevoke,
    bool CanInviteGuests,
    bool CanViewAuditLog,
    bool CanAdmin)
{
    public static PersonaCapabilities For(DrmPersona persona) => persona switch
    {
        DrmPersona.Employee        => new(true,  false, true,  false, false),
        DrmPersona.KnowledgeWorker => new(true,  true,  true,  false, false),
        DrmPersona.Executive       => new(true,  true,  true,  true,  false),
        DrmPersona.Admin           => new(true,  true,  true,  true,  true ),
        _ => new(false, false, false, false, false)
    };
}
```

#### Step 3: Entity + migration

```csharp
public sealed class TenantUserPersonaEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Persona { get; set; } = nameof(DrmPersona.Employee);
    public DateTimeOffset AssignedAtUtc { get; set; }
}
```

Migration in `Program.cs`:

```csharp
dbContext.Database.ExecuteSqlRaw("""
    CREATE TABLE IF NOT EXISTS "TenantUserPersonas" (
        "TenantId" TEXT NOT NULL,
        "UserId" TEXT NOT NULL,
        "Persona" TEXT NOT NULL DEFAULT 'Employee',
        "AssignedAtUtc" TEXT NOT NULL,
        CONSTRAINT "PK_TenantUserPersonas" PRIMARY KEY ("TenantId", "UserId")
    );
    """);
```

#### Step 4: Endpoint

```csharp
public static class PersonaEndpoints
{
    public static IEndpointRouteBuilder MapPersonaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/persona", GetPersonaAsync);
        endpoints.MapPut("/api/admin/personas/{userId:guid}", SetPersonaAsync);
        return endpoints;
    }
    // ...returns PersonaResponse(string Persona, bool CanProtect, bool CanRevoke,
    //                           bool CanInviteGuests, bool CanViewAuditLog, bool CanAdmin)
}
```

#### Step 5: Run test → PASS. Commit.

```bash
git add -A
git commit -m "feat: persona profile model (Employee / KnowledgeWorker / Executive / Admin)"
```

**Acceptance:** unauthenticated probe of `/api/me/persona` returns the conservative `Employee` defaults; admin can `PUT /api/admin/personas/{userId}` to elevate.

---

### Task 2: Quick-Share endpoint — one-call protect+invite+share-link

**Why:** today protecting a file requires 3 API calls (`POST /files`, `POST /files/{id}/grants`, `POST /files/{id}/share-links`). Non-technical users need one.

**Files:**
- Create `src/Drm.Server/Endpoints/QuickShareEndpoints.cs`
- Test: `tests/Drm.Server.Tests/QuickShareEndpointsTests.cs`

#### Step 1: Failing test

```csharp
[Fact]
public async Task Quick_share_protects_grants_and_returns_share_url_in_one_call()
{
    using var client = factory.CreateClient();
    var body = new {
        tenantId = Guid.NewGuid(),
        userId = Guid.NewGuid(),
        recipientEmail = "bob@example.com",
        fileName = "pitch.pdf",
        contentType = "application/pdf",
        fileBytesBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("PITCH DECK BYTES")),
        expiresInHours = 168,
        allowPrint = false
    };
    var resp = await client.PostAsJsonAsync("/api/me/share", body);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();
    result!.FileId.Should().NotBeEmpty();
    result.ShareUrl.Should().StartWith("http").And.Contain("/share/");
    result.ExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(168), TimeSpan.FromMinutes(1));
}
```

#### Step 2: Endpoint implementation

```csharp
[HttpPost("/api/me/share")]
async Task<Results<Created<QuickShareResponse>, BadRequest<ErrorResponse>>> QuickShareAsync(
    QuickShareRequest request,
    AppDbContext db,
    HttpContext http,
    CancellationToken ct)
{
    // 1. Validate (size, email, non-empty file)
    // 2. Generate fileId + protect (existing ProtectedFileEntity row)
    // 3. Auto-grant View (+ Print if requested) to recipient email subject
    // 4. Create share-link with email + expiresInHours + 1 max use (P1 default)
    // 5. Audit `quick_share_created` event
    // 6. Return { FileId, ShareUrl, ExpiresAtUtc }
}
```

#### Step 3: Run test → PASS. Commit.

**Acceptance:** one POST with file bytes + recipient email = file ID + ready-to-paste share URL.

---

### Task 3: Persona-switched landing page `/me/`

**Why:** today the admin console drowns sales reps in 19 panels. Persona-aware landing shows only what the user can do.

**Files:**
- Create `src/Drm.Server/wwwroot/me/index.html`
- Create `src/Drm.Server/wwwroot/me/app.js`
- Create `src/Drm.Server/wwwroot/me/app.css`
- Modify `src/Drm.Server/Program.cs` — redirect `/` to `/me/` for unauthenticated visits (was `/share/`)

#### Step 1: Layout (HTML)

```html
<!-- /me/index.html — Quick Share (Employee persona default) -->
<main class="hero">
  <h1>Send a protected file</h1>
  <p class="lead">Drop a file below. Type a recipient email. We handle the rest.</p>

  <form id="quickShareForm" class="quick-share">
    <div id="dropZone" class="drop-zone">
      <p><strong>Drop file here</strong> or click to browse</p>
      <input type="file" id="fileInput" hidden>
      <p id="fileSummary" class="hint"></p>
    </div>
    <label>Send to <input id="recipient" type="email" required placeholder="bob@example.com"></label>
    <details class="advanced">
      <summary>Advanced options</summary>
      <label>Expires after <input id="expiresHours" type="number" min="1" max="8760" value="168"> hours</label>
      <label><input id="allowPrint" type="checkbox"> Allow recipient to print</label>
    </details>
    <button type="submit" class="primary big">Send protected file</button>
  </form>

  <div id="result" class="result" hidden>
    <h2>✅ Sent</h2>
    <p>Share this link with the recipient. They'll need to verify their email to open it.</p>
    <input id="shareUrl" type="text" readonly>
    <button id="copyBtn">Copy link</button>
    <p class="hint">Expires <span id="expiresAt"></span>. <a id="revokeLink" href="#">Revoke now</a></p>
  </div>
</main>
```

#### Step 2: Persona-aware nav (JS)

```js
const persona = await fetch(`/api/me/persona?tenantId=${tenantId}&userId=${userId}`).then(r => r.json());
if (persona.canAdmin) {
    document.querySelector('[data-admin-link]').hidden = false;
}
if (persona.canViewAuditLog) {
    document.querySelector('[data-audit-link]').hidden = false;
}
```

#### Step 3: Drop-zone + form handler

```js
document.querySelector('#quickShareForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const file = document.querySelector('#fileInput').files[0];
    const buf = await file.arrayBuffer();
    const body = {
        tenantId, userId,
        recipientEmail: document.querySelector('#recipient').value,
        fileName: file.name, contentType: file.type || 'application/octet-stream',
        fileBytesBase64: btoa(String.fromCharCode(...new Uint8Array(buf))),
        expiresInHours: Number(document.querySelector('#expiresHours').value),
        allowPrint: document.querySelector('#allowPrint').checked
    };
    const resp = await fetch('/api/me/share', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
    const result = await resp.json();
    showResult(result);
});
```

#### Step 4: Acceptance — manual test

- Open `/me/` in a fresh browser, drop a 100 KB PDF, type email, click button → share URL appears within 2 seconds.
- Total clicks from open-tab to send-link: **3** (drop file, type email, click button).

#### Step 5: Commit.

---

### Task 4: Persistent admin session — localStorage for credentials

**Why:** today every panel asks for Tenant ID, Admin key, User ID, repeatedly. Anti-pattern: friction without benefit.

**Files:**
- Modify `src/Drm.Server/wwwroot/admin/app.js` — autofill from localStorage on load, save on "Save session" button click
- Modify `src/Drm.Server/wwwroot/admin/index.html` — add "Forget session" button

#### Step 1: Persistence helpers

```js
const STORAGE_KEY = 'drm-admin-session-v1';

function loadSession() {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try { return JSON.parse(raw); } catch { return null; }
}

function saveSession() {
    const session = {
        tenantId: tenantIdInput.value.trim(),
        adminKey: adminKeyInput.value,
        adminUserId: adminUserIdInput.value.trim()
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
}

function forgetSession() {
    localStorage.removeItem(STORAGE_KEY);
    [tenantIdInput, adminKeyInput, adminUserIdInput].forEach(i => i.value = '');
    setStatus('Session cleared', 'ok');
}
```

#### Step 2: Hook on page load

```js
document.addEventListener('DOMContentLoaded', () => {
    const s = loadSession();
    if (s) {
        tenantIdInput.value = s.tenantId ?? '';
        adminKeyInput.value = s.adminKey ?? '';
        adminUserIdInput.value = s.adminUserId ?? '';
    }
});
```

#### Step 3: Save-on-blur

```js
[tenantIdInput, adminKeyInput, adminUserIdInput].forEach(i => i.addEventListener('change', saveSession));
```

#### Step 4: Acceptance

- Refresh admin page → Tenant ID + Admin key + User ID auto-filled from previous session.
- Click "Forget session" → all three fields cleared, localStorage entry removed.

#### Step 5: Commit.

---

### Task 5: Collapsible panel groups + keyboard `/` search

**Why:** 19 panels in one scroll = cognitive overload. Group them by lifecycle phase and collapse-by-default.

**Files:**
- Modify `src/Drm.Server/wwwroot/admin/index.html` — wrap panels in `<details>` groups
- Modify `src/Drm.Server/wwwroot/admin/app.css` — collapse styles + active-section highlight
- Modify `src/Drm.Server/wwwroot/admin/app.js` — `/` keyboard shortcut focuses search bar, filters panels by title

#### Step 1: Group taxonomy (5 groups)

| Group | Panels |
|---|---|
| **Identity** | Tenant ops, Users, Groups, Devices, Personas (new) |
| **Policy** | Templates, Watermarks, Simulator |
| **Files** | Files, Containers, Transparent files, Tags |
| **Integrations** | Box, Outlook, Directory sync, SCIM, Folder watcher |
| **Operations** | Audit, SIEM, Email notifications, License, Status, Compatibility, Use cases |

#### Step 2: Wrap each section

```html
<details class="panel-group" open data-group="identity">
  <summary><span class="group-icon">👥</span> Identity</summary>
  <section class="panel" id="users">...</section>
  <section class="panel" id="groups">...</section>
  <section class="panel" id="devices">...</section>
</details>
```

#### Step 3: `/` keyboard shortcut

```js
document.addEventListener('keydown', (e) => {
    if (e.key === '/' && document.activeElement.tagName !== 'INPUT') {
        e.preventDefault();
        document.querySelector('#globalSearch').focus();
    }
});

document.querySelector('#globalSearch').addEventListener('input', (e) => {
    const q = e.target.value.toLowerCase();
    document.querySelectorAll('.panel').forEach(p => {
        const title = p.querySelector('h3, h4')?.textContent.toLowerCase() ?? '';
        p.hidden = q && !title.includes(q);
    });
});
```

#### Step 4: Acceptance

- Open admin console → only group headers visible by default; expand any group to reveal its panels.
- Hit `/`, type "watermark" → only Watermarks panel visible across all groups.

#### Step 5: Commit.

---

### Task 6: Tooltip glossary for jargon (`?` icon next to technical terms)

**Why:** "TenantId", "WatermarkTemplate", "RunMacros permission" mean nothing to a sales rep. Anti-pattern: jargon overload.

**Files:**
- Create `src/Drm.Server/wwwroot/admin/glossary.json` — term → human explanation
- Modify `src/Drm.Server/wwwroot/admin/app.js` — auto-decorate `<label>` text and code spans
- Modify `src/Drm.Server/wwwroot/admin/app.css` — `[data-help]` tooltip on hover

#### Step 1: Glossary content

```json
{
  "TenantId": "Your organisation's unique ID. The IT admin gave you this when they created your DRM account.",
  "User ID": "Your user UUID. Found in your profile page under 'Settings → Account → User ID'.",
  "WatermarkTemplate": "A reusable visible-watermark recipe (text + opacity + position). Set up once, apply to many files.",
  "RunMacros": "Permission for the recipient to run embedded Office macros (VBA code in Word/Excel/PowerPoint).",
  "TransferOwnership": "Permission for the recipient to become the new file owner. Use carefully — they can then revoke access for others.",
  "Drm:Security:AdminApiKey": "A shared secret that gates the /api/admin/* endpoints. Treat as a password.",
  "Drm:Security:TransparentTrailerSecret": "HMAC key signing the transparent-encryption trailer. Generate with `openssl rand -hex 32`.",
  "OfflineLease": "How long a viewer can open a protected file without contacting the server. Set to 0 to require live policy lookups every time.",
  "PolicyTemplate": "A reusable bundle of permissions + watermark + offline lease. Apply once to many files.",
  "ScimBearerToken": "Token your identity provider uses to call our SCIM endpoints when provisioning users."
}
```

#### Step 2: Decorator script

```js
const GLOSSARY = await fetch('/admin/glossary.json').then(r => r.json());

function decorateGlossary() {
    document.querySelectorAll('label, code').forEach((el) => {
        if (el.dataset.glossaryDecorated) return;
        const text = el.textContent.trim();
        for (const [term, def] of Object.entries(GLOSSARY)) {
            if (text.includes(term)) {
                el.insertAdjacentHTML('beforeend',
                    `<span class="help-icon" data-help="${escapeAttr(def)}">?</span>`);
                el.dataset.glossaryDecorated = '1';
                break;
            }
        }
    });
}
```

#### Step 3: Tooltip CSS (pure-CSS, no JS lib)

```css
.help-icon {
    display: inline-block;
    margin-left: 4px;
    width: 14px;
    height: 14px;
    border-radius: 999px;
    background: var(--accent);
    color: white;
    font-size: 10px;
    line-height: 14px;
    text-align: center;
    cursor: help;
    position: relative;
}
.help-icon:hover::after {
    content: attr(data-help);
    position: absolute;
    bottom: calc(100% + 6px);
    left: 50%;
    transform: translateX(-50%);
    background: #1f2937;
    color: white;
    font-size: 0.78rem;
    padding: 8px 12px;
    border-radius: 4px;
    width: 280px;
    text-align: left;
    z-index: 10;
}
```

#### Step 4: Acceptance — hover any `?` icon and a 280-px wide explanation appears within 100 ms with no network call.

#### Step 5: Commit.

---

### Task 7: Windows tray refactor — TabControl with "Quick" + "Advanced"

**Why:** today the tray is 1040 px tall and shows everything to everyone. Persona Employee should see one tab; Admin sees two.

**Files:**
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml` — wrap content in `<TabControl>` with two tabs
- Create `src/Drm.Agent.Tray.Windows/Views/QuickProtectView.xaml` — one big drop zone, recipient email, send button
- Create `src/Drm.Agent.Tray.Windows/Views/AdvancedView.xaml` — current content moved here
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs` — load persona on startup, hide Advanced tab when not Admin

#### Step 1: Quick Protect tab layout

```xml
<TabItem Header="Quick Protect">
  <Grid Margin="32">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
      <RowDefinition Height="Auto" />
      <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Text="Protect a file" FontSize="22" FontWeight="SemiBold" Margin="0,0,0,16" />

    <Border Grid.Row="1" x:Name="QuickDropZone"
            Background="#FFFFFF" BorderBrush="#D1D5DB" BorderThickness="2"
            CornerRadius="8" AllowDrop="True"
            DragEnter="QuickDropZone_DragEnter"
            DragLeave="QuickDropZone_DragLeave"
            DragOver="QuickDropZone_DragEnter"
            Drop="QuickDropZone_Drop">
      <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
        <TextBlock Text="📄" FontSize="48" HorizontalAlignment="Center" />
        <TextBlock x:Name="QuickDropHint" Text="Drag a file here or click Browse"
                   Foreground="#6B7280" FontSize="14" HorizontalAlignment="Center" Margin="0,8,0,0" />
      </StackPanel>
    </Border>

    <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,16,0,0">
      <TextBlock Text="Send to:" VerticalAlignment="Center" Margin="0,0,8,0" />
      <TextBox x:Name="QuickRecipientBox" Width="300" Height="28"
               VerticalContentAlignment="Center" />
    </StackPanel>

    <Button Grid.Row="3" x:Name="QuickSendButton" Content="Send protected file"
            Height="40" FontWeight="SemiBold" Margin="0,16,0,0"
            Click="QuickSendButton_Click" Background="#A45B13" Foreground="White" />
  </Grid>
</TabItem>
```

#### Step 2: Handler — single call to `/api/me/share`

```csharp
private async void QuickSendButton_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(quickSourcePath))
    {
        SetStatus("Drop a file first.");
        return;
    }
    var bytes = await File.ReadAllBytesAsync(quickSourcePath);
    var resp = await httpClient.PostAsJsonAsync("/api/me/share", new {
        tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID"),
        userId = ParseRequiredGuid(UserIdBox.Text, "User ID"),
        recipientEmail = QuickRecipientBox.Text.Trim(),
        fileName = Path.GetFileName(quickSourcePath),
        contentType = GuessContentType(quickSourcePath),
        fileBytesBase64 = Convert.ToBase64String(bytes),
        expiresInHours = 168,
        allowPrint = false
    });
    var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>();
    System.Windows.Clipboard.SetText(result!.ShareUrl);
    SetStatus($"✅ Sent. Share URL copied to clipboard. Expires {result.ExpiresAtUtc:O}.");
}
```

#### Step 3: Hide Advanced tab for Employee persona

```csharp
private async Task LoadPersonaAsync()
{
    var resp = await httpClient.GetAsync(
        $"/api/me/persona?tenantId={tenantId}&userId={userId}");
    var persona = await resp.Content.ReadFromJsonAsync<PersonaResponse>();
    AdvancedTab.Visibility = persona!.CanAdmin ? Visibility.Visible : Visibility.Collapsed;
}
```

#### Step 4: Acceptance

- Launch tray as Employee user → one tab visible, drop file, type email, click Send → share URL in clipboard.
- Total user actions: **3** (drop, type, click).

#### Step 5: Commit.

---

### Task 8: Windows shell extension — right-click "Protect with DRM"

**Why:** the most-cited FinalCode pattern; the path to "non-technical user doesn't need to open any DRM app." Aligns with research's #1 anti-pattern fix (workflow integration > separate tool).

**Files:**
- Create `src/Drm.Agent.Shell.Windows/Drm.Agent.Shell.Windows.csproj`
- Create `src/Drm.Agent.Shell.Windows/ProtectContextMenuHandler.cs` — implements `IShellExtInit`, `IContextMenu`
- Create `src/Drm.Agent.Shell.Windows/install.ps1` — `regsvr32` + `HKCR\*\shellex\ContextMenuHandlers\Drm`

#### Step 1: COM in-proc handler (C# with `[ComVisible]`)

```csharp
[ComVisible(true)]
[Guid("DRM00001-1111-2222-3333-444444444444")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ProtectContextMenuHandler : IShellExtInit, IContextMenu
{
    private string? selectedFile;

    public int Initialize(IntPtr pidlFolder, IntPtr lpdobj, IntPtr hKeyProgID)
    {
        // Read selected file path from IDataObject.
        selectedFile = ReadFirstSelectedPath(lpdobj);
        return selectedFile is null ? -1 : 0;
    }

    public int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)
    {
        InsertMenu(hMenu, indexMenu, MF_BYPOSITION, idCmdFirst,
            "🔒 Protect with DRM…");
        InsertMenu(hMenu, indexMenu + 1, MF_BYPOSITION, idCmdFirst + 1,
            "🔒 Protect with DRM (Transparent)");
        return 2;
    }

    public void InvokeCommand(ref CMINVOKECOMMANDINFO ici)
    {
        var cmd = (uint)ici.lpVerb;
        var trayPath = ResolveTrayExe();
        if (cmd == 0)
            Process.Start(trayPath, $"--quick-protect \"{selectedFile}\"");
        else
            Process.Start(trayPath, $"--transparent-protect \"{selectedFile}\"");
    }
}
```

#### Step 2: PowerShell installer

```powershell
# install.ps1
$dll = Resolve-Path "Drm.Agent.Shell.Windows.dll"
regsvr32 /s $dll
$clsid = "{DRM00001-1111-2222-3333-444444444444}"
New-Item -Path "HKCR:\*\shellex\ContextMenuHandlers\Drm" -Value $clsid -Force
```

#### Step 3: Tray accepts `--quick-protect` and `--transparent-protect` CLI args

```csharp
// MainWindow.xaml.cs
private void PrefillFromCommandLine()
{
    var args = Environment.GetCommandLineArgs();
    var quickPath = TryGetCommandLineValue("--quick-protect", args);
    if (!string.IsNullOrWhiteSpace(quickPath))
    {
        QuickTab.IsSelected = true;
        quickSourcePath = quickPath;
        QuickDropHint.Text = Path.GetFileName(quickPath);
    }
}
```

#### Step 4: Acceptance

- Install the shell extension on a Windows VM.
- Right-click any file in Explorer → see two new entries near the top: "🔒 Protect with DRM…" and "🔒 Protect with DRM (Transparent)".
- Click "Protect with DRM…" → tray opens with file pre-loaded on the Quick tab.

#### Step 5: Commit.

---

### Task 9: 60-second onboarding tour for `/me/`

**Why:** Digify's "no training needed, started in minutes" claim — operationalize as a one-time tour.

**Files:**
- Create `src/Drm.Server/wwwroot/me/onboarding.html` — overlay markup with 4 stops
- Modify `src/Drm.Server/wwwroot/me/app.js` — show tour on first visit (localStorage gate)

#### Step 1: 4-stop tour

| # | Anchor | Copy |
|---|---|---|
| 1 | Drop zone | "Drop any file here. We'll protect it before sharing." |
| 2 | Recipient field | "Type one email. The recipient gets a verification link — no DRM client needed on their side." |
| 3 | Advanced details | "Want to expire after 24 h or block printing? Open Advanced." |
| 4 | Send button | "Click Send. The share URL lands in your clipboard." |

#### Step 2: Behaviour

```js
const TOUR_KEY = 'drm-tour-completed-v1';

if (!localStorage.getItem(TOUR_KEY)) {
    startTour();
}

function startTour() {
    // Render full-screen overlay with arrow + copy.
    // "Next" / "Skip" buttons advance.
    // On completion → localStorage.setItem(TOUR_KEY, '1').
}
```

#### Step 3: Acceptance

- First visit to `/me/` → tour overlay appears.
- Complete tour → localStorage gate set, never appears again on this browser.
- Click profile menu → "Replay tour" option.

#### Step 4: Commit.

---

### Task 10: CLI `drm share` — one-line protected send

**Why:** power users (researchers, automation scripts) want `drm share file.pdf --to bob@x.com`.

**Files:**
- Create `src/Drm.Cli/Commands/ShareCommand.cs`
- Modify `src/Drm.Cli/Program.cs` — register `share` verb
- Test: `tests/Drm.Cli.Tests/ShareCommandTests.cs`

#### Step 1: Failing test

```csharp
[Fact]
public async Task Share_command_calls_quick_share_endpoint_and_prints_url()
{
    var stub = new StubHttpHandler(/* echoes /api/me/share */);
    var exit = await CliRunner.RunAsync(new[] {
        "share", "test.pdf", "--to", "bob@x.com",
        "--tenant", "11111111-1111-1111-1111-111111111111",
        "--user", "22222222-2222-2222-2222-222222222222",
        "--expires-hours", "24"
    }, stdout, stub);
    exit.Should().Be(0);
    stdout.ToString().Should().Contain("http://").And.Contain("/share/");
}
```

#### Step 2: Command

```csharp
public sealed class ShareCommand
{
    public async Task<int> ExecuteAsync(ShareOptions opt, TextWriter stdout, HttpClient http, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(opt.SourcePath, ct);
        var resp = await http.PostAsJsonAsync("/api/me/share", new { /* ... */ });
        var result = await resp.Content.ReadFromJsonAsync<QuickShareResponse>(ct);
        stdout.WriteLine(result!.ShareUrl);
        return 0;
    }
}
```

#### Step 3: Acceptance

```bash
drm share quarterly-results.xlsx --to cfo@company.com --expires-hours 24
# https://drm.company.com/share/?token=abc...
```

#### Step 4: Commit.

---

### Task 11: `Drm.Viewer.Windows` first-run F1 help overlay

**Why:** even after `Open`, users miss the toolbar buttons (Copy / Print / Export / Print WM). Add a one-time overlay that points at each control.

**Files:**
- Create `src/Drm.Viewer.Windows/HelpOverlay.xaml` — semi-transparent overlay with 4 callouts
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs` — show on first run, also on F1

#### Step 1: Layout

```xml
<Grid x:Name="HelpOverlayRoot" Background="#CC000000" Visibility="Collapsed">
    <!-- 4 callouts pointing at Copy, Print, Export, PrintWM controls -->
    <TextBlock Text="Press F1 anytime to see this again."
               Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,40" />
    <Button Content="Got it" HorizontalAlignment="Right" VerticalAlignment="Top"
            Margin="0,16,16,0" Click="DismissHelpOverlay" />
</Grid>
```

#### Step 2: Trigger

```csharp
private void Window_KeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.F1) HelpOverlayRoot.Visibility = Visibility.Visible;
}

protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    if (Properties.Settings.Default.FirstRun)
    {
        HelpOverlayRoot.Visibility = Visibility.Visible;
        Properties.Settings.Default.FirstRun = false;
        Properties.Settings.Default.Save();
    }
}
```

#### Step 3: Acceptance

- First launch of viewer → overlay visible.
- Dismiss → never auto-shows again.
- F1 anywhere → overlay reappears.

#### Step 4: Commit.

---

### Task 12: Tests + README + final polish

#### Step 1: Run full suite

```bash
dotnet test tests/Drm.Server.Tests
dotnet test tests/Drm.Cli.Tests
dotnet test tests/Drm.Agent.Core.Tests
```

Expected: all green, ≥ 240 tests.

#### Step 2: README section

Append a "Personas & Quick Share" section to README.md documenting:
- the four personas
- `/me/` landing page URL
- `drm share` CLI verb
- Windows shell extension installation (`install.ps1`)
- Glossary file location for tenant-specific overrides

#### Step 3: Commit + roadmap update

```bash
git commit -m "feat: easy-to-use redesign (Phase 5AS): personas + Quick Share + shell extension"
```

Update `docs/superpowers/plans/2026-05-17-finalcode-parity-roadmap.md` to mark Phase 5AS shipped.

---

## Acceptance criteria for the whole phase

| Measurement | Target | How to verify |
|---|---|---|
| **Time to send first protected file (cold start)** | ≤ 60 s | Stopwatch test from `/me/` open → "Sent" |
| **Number of user actions** | 3 | drop, type, click |
| **Required GUID typings for Employee persona** | 0 | Persona derives identity from session |
| **Admin panels visible by default** | ≤ 5 group headers | Collapsed `<details>` |
| **Right-click in Explorer reaches Quick Protect** | Yes | Shell ext installed |
| **First-run overlay shown** | Once per user per surface | localStorage + Settings |
| **Tooltip glossary covers ≥ 10 terms** | Yes | `glossary.json` ≥ 10 entries |
| **CLI one-liner works** | `drm share file --to email` returns URL on stdout | Integration test |
| **Tests** | ≥ 240 total, 0 failing | `dotnet test` |
| **UX scorecard** | ≥ 88/100 | Self-rate using the rubric above |

---

## Self-review checklist

- [ ] Every task has admin-console AND Windows-desktop deliverable (or documents why not)
- [ ] Every endpoint added is tenant-scoped (TenantId in PK and filter)
- [ ] Persona logic fails closed (default `Employee`, not `Admin`)
- [ ] Quick-share endpoint validates email + file size + content type
- [ ] Shell extension is documented as `regsvr32`-installed; not auto-installed by the tray
- [ ] No new top-level config secrets — reuses existing admin key
- [ ] CLI follows existing verb pattern (no breaking changes)
- [ ] Tour and F1 overlay are dismissible and never block work
- [ ] Glossary is i18n-ready (JSON keyed by term)

---

## Execution

After saving this plan, two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh implementer per task, two-stage review (spec + code quality) per task. Use `superpowers:subagent-driven-development`.
2. **Inline Execution** — execute tasks in this session with `superpowers:executing-plans` batching checkpoints.

Which approach?

---

## Sources

- [Kiteworks — Top 5 DRM Requirements](https://www.kiteworks.com/digital-rights-management/top-5-drm-requirements/) — "ease of use" is #1
- [Seclore — Bridging Security & Usability Gap](https://www.seclore.com/blog/feature-update-bridging-the-security-and-usability-gap/) — 30% faster, browser editor, pinch-to-zoom
- [CapLinked FileProtect](https://www.caplinked.com/digital-rights-management-fileprotect/) — 3-step protect → audience → enable DRM
- [Locklizard Safeguard](https://www.locklizard.com/rights-management/) — single "Publish" button, transparent key management
- [Digify](https://digify.com/) — "no training needed, started in minutes"
- [UXmatters — Secure UX Lifecycle](https://www.uxmatters.com/mt/archives/2025/03/secure-ux-building-cybersecurity-and-privacy-into-the-ux-lifecycle.php) — 5 anti-patterns, persona patterns, real-time status badges
- [Medium — User Rights Management Case Study](https://medium.com/anothercircus/user-rights-management-redlink-ux-ui-case-study-part-i-8206885208b2) — two-pool IA, progressive disclosure
- [CloudNuro — Top 10 IRM Tools 2025](https://www.cloudnuro.ai/blog/top-10-information-rights-management-irm-tools-for-data-security-in-2025) — Virtru "easy", Fasoo "extensive training", market adoption barriers
- [DevOpsSchool — Top 10 DRM Comparison](https://www.devopsschool.com/blog/top-10-digital-rights-management-drm-features-pros-cons-comparison/) — feature/usability landscape
- Internal: FinalCode iCata catalog (`/tmp/fc/page-01.jpg` … `page-16.jpg`) — right-click, drag-drop, Explorer-style browse patterns
