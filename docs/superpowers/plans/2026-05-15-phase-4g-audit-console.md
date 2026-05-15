# Phase 4G Audit Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose admin audit search and CSV export in the `/admin/` management console.

**Architecture:** Keep the existing `/api/admin/audit` and `/api/admin/audit.csv` backend unchanged. Add a console section that lists recent tenant audit events, filters by exact event type, and exports CSV through `fetch` so `X-DRM-Admin-Key` is preserved. Cover the static asset contract before changing UI files.

**Tech Stack:** .NET 10 minimal APIs, vanilla JS console, static HTML/CSS, xUnit, FluentAssertions, Playwright CLI for browser smoke.

---

## File Structure

- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert audit UI labels and JS references exist.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Audit navigation and audit event controls/table.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: fetch JSON audit events and CSV blobs with the admin header.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add an audit filter row layout.
- Modify `README.md`: document console audit viewing/export.

## Tasks

### Task 1: Audit Console Tests

- [x] **Step 1: Write failing console asset tests**

Update `Admin_console_index_is_served` in `tests/Drm.Server.Tests/ManagementConsoleTests.cs` to assert:

```csharp
html.Should().Contain("Audit events");
html.Should().Contain("Event type filter");
html.Should().Contain("Export CSV");
```

Update `Admin_console_assets_are_served` to assert:

```csharp
js.Should().Contain("/api/admin/audit");
js.Should().Contain("/api/admin/audit.csv");
js.Should().Contain("refreshAuditEvents");
js.Should().Contain("downloadAuditCsv");
```

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because the console does not yet include audit UI or JS wiring.

### Task 2: Audit Console UI

- [x] **Step 1: Implement HTML controls**

In `src/Drm.Server/wwwroot/admin/index.html`, add an `Audit` nav link and this section before `#status`:

```html
<section class="panel" id="audit">
  <div class="section-head">
    <div>
      <p class="eyebrow">Activity</p>
      <h3>Audit events</h3>
    </div>
    <div class="button-row">
      <button id="refreshAuditEvents" type="button">Refresh audit</button>
      <button id="downloadAuditCsv" type="button">Export CSV</button>
    </div>
  </div>

  <div class="audit-filter-row">
    <input id="auditEventType" autocomplete="off" placeholder="Event type filter">
  </div>

  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Time</th>
          <th>Event type</th>
          <th>Reason</th>
          <th>File ID</th>
          <th>User ID</th>
        </tr>
      </thead>
      <tbody id="auditEventsBody">
        <tr>
          <td colspan="5" class="empty">Refresh to load audit events.</td>
        </tr>
      </tbody>
    </table>
  </div>
</section>
```

- [x] **Step 2: Implement JS behavior**

In `src/Drm.Server/wwwroot/admin/app.js`, add:

```javascript
const auditEventsBody = document.querySelector("#auditEventsBody");

document.querySelector("#refreshAuditEvents").addEventListener("click", () => {
  refreshAuditEvents();
});

document.querySelector("#downloadAuditCsv").addEventListener("click", () => {
  downloadAuditCsv();
});

async function refreshAuditEvents() {
  const events = await apiFetch(buildAuditUrl("/api/admin/audit"));
  renderAuditEvents(events);
  setStatus(`${events.length} audit event${events.length === 1 ? "" : "s"} loaded`, "ok");
}

async function downloadAuditCsv() {
  const tenantId = requireTenantId();
  const blob = await apiFetchBlob(buildAuditUrl("/api/admin/audit.csv"));
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = objectUrl;
  link.download = `drm-audit-${tenantId}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(objectUrl);
  setStatus("Audit CSV exported", "ok");
}

function buildAuditUrl(path) {
  const tenantId = requireTenantId();
  const eventType = document.querySelector("#auditEventType").value.trim();
  const params = new URLSearchParams({ tenantId, eventType });
  return `${path}?${params.toString()}`;
}

async function apiFetchBlob(url, options = {}) {
  const adminKey = requireAdminKey();
  const response = await fetch(url, {
    ...options,
    headers: {
      "X-DRM-Admin-Key": adminKey,
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    setStatus(`Request failed: ${response.status}`, "error");
    throw new Error(`Request failed with HTTP ${response.status}`);
  }

  return response.blob();
}

function renderAuditEvents(events) {
  if (!events.length) {
    auditEventsBody.innerHTML = '<tr><td colspan="5" class="empty">No audit events found.</td></tr>';
    return;
  }

  auditEventsBody.innerHTML = events.map((auditEvent) => `
    <tr>
      <td>${escapeHtml(formatDate(auditEvent.createdAtUtc))}</td>
      <td>${escapeHtml(auditEvent.eventType)}</td>
      <td>${escapeHtml(auditEvent.reasonCode)}</td>
      <td><code>${escapeHtml(auditEvent.fileId)}</code></td>
      <td><code>${escapeHtml(auditEvent.userId)}</code></td>
    </tr>
  `).join("");
}
```

- [x] **Step 3: Implement CSS layout**

In `src/Drm.Server/wwwroot/admin/app.css`, add:

```css
.button-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.audit-filter-row {
  display: grid;
  grid-template-columns: minmax(220px, 360px);
}
```

Include `.audit-filter-row` and `.button-row` in the mobile rule.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Documentation, Verification, Commit

- [x] **Step 1: Update README**

Add:

```markdown
## Phase 4G Audit Console

The `/admin/` console now lists tenant audit events from `/api/admin/audit`, supports exact event-type filtering, and exports CSV through `/api/admin/audit.csv` while preserving the admin API key header.
```

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Live smoke**

Run a temp server and verify:

```bash
curl -fsS http://127.0.0.1:5080/admin/ | grep "Audit events"
curl -fsS http://127.0.0.1:5080/admin/app.js | grep "/api/admin/audit.csv"
```

Then create an admin action and confirm `/api/admin/audit` and `/api/admin/audit.csv` return that event.

- [x] **Step 4: Browser smoke**

Use Playwright CLI to open `/admin/`, snapshot the page, and confirm the Audit section is visible with no console errors.

- [x] **Step 5: Commit**

Run:

```bash
git add README.md src/Drm.Server/wwwroot/admin tests/Drm.Server.Tests/ManagementConsoleTests.cs docs/superpowers/plans/2026-05-15-phase-4g-audit-console.md
git commit -m "feat: add audit viewer to management console"
```

## Self-Review

- Spec coverage: Adds audit JSON list, event-type filtering, and CSV export access to management console.
- Security note: CSV export uses `fetch` so protected admin endpoints still receive `X-DRM-Admin-Key`.
- Placeholder scan: No TBD/TODO placeholders.
