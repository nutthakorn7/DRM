# Phase 4H SIEM Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose existing SIEM webhook create/list operations in the `/admin/` management console.

**Architecture:** Keep `/api/admin/siem-webhooks` backend behavior unchanged. Add a console section where operators register HTTPS SIEM webhook URLs, choose enabled/disabled state, list configured webhooks, and see created timestamps. Cover static UI/API references before changing production assets.

**Tech Stack:** .NET 10 minimal APIs, vanilla JS console, static HTML/CSS, xUnit, FluentAssertions, Playwright CLI for browser smoke.

---

## File Structure

- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert SIEM UI labels and JS references exist.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add SIEM navigation and webhook controls/table.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: call `/api/admin/siem-webhooks` to create/list webhooks.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add webhook form layout using existing console patterns.
- Modify `README.md`: document SIEM console management.

## Tasks

### Task 1: SIEM Console Tests

- [x] **Step 1: Write failing console asset tests**

Update `Admin_console_index_is_served` in `tests/Drm.Server.Tests/ManagementConsoleTests.cs` to assert:

```csharp
html.Should().Contain("SIEM webhooks");
html.Should().Contain("Webhook URL");
html.Should().Contain("Enabled");
```

Update `Admin_console_assets_are_served` to assert:

```csharp
js.Should().Contain("/api/admin/siem-webhooks");
js.Should().Contain("refreshSiemWebhooks");
js.Should().Contain("createSiemWebhookForm");
```

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because SIEM webhook UI and JS wiring do not exist.

### Task 2: SIEM Console UI

- [x] **Step 1: Implement HTML controls**

In `src/Drm.Server/wwwroot/admin/index.html`, add an `SIEM` nav link and this section before `#audit`:

```html
<section class="panel" id="siem">
  <div class="section-head">
    <div>
      <p class="eyebrow">Integrations</p>
      <h3>SIEM webhooks</h3>
    </div>
    <button id="refreshSiemWebhooks" type="button">Refresh webhooks</button>
  </div>

  <form class="webhook-row" id="createSiemWebhookForm">
    <input id="siemWebhookId" autocomplete="off" placeholder="Webhook ID">
    <input id="siemWebhookUrl" autocomplete="off" placeholder="Webhook URL">
    <label class="check-row">
      <input id="siemWebhookEnabled" type="checkbox" checked>
      <span>Enabled</span>
    </label>
    <button type="submit">Create webhook</button>
  </form>

  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Webhook ID</th>
          <th>Webhook URL</th>
          <th>Status</th>
          <th>Created</th>
        </tr>
      </thead>
      <tbody id="siemWebhooksBody">
        <tr>
          <td colspan="4" class="empty">Refresh to load SIEM webhooks.</td>
        </tr>
      </tbody>
    </table>
  </div>
</section>
```

- [x] **Step 2: Implement JS behavior**

In `src/Drm.Server/wwwroot/admin/app.js`, add:

```javascript
const siemWebhooksBody = document.querySelector("#siemWebhooksBody");

document.querySelector("#refreshSiemWebhooks").addEventListener("click", () => {
  refreshSiemWebhooks();
});

document.querySelector("#createSiemWebhookForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    webhookId: document.querySelector("#siemWebhookId").value.trim(),
    url: document.querySelector("#siemWebhookUrl").value.trim(),
    enabled: document.querySelector("#siemWebhookEnabled").checked
  };

  await apiFetch("/api/admin/siem-webhooks", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  document.querySelector("#siemWebhookEnabled").checked = true;
  await refreshSiemWebhooks();
});

async function refreshSiemWebhooks() {
  const tenantId = requireTenantId();
  const webhooks = await apiFetch(`/api/admin/siem-webhooks?tenantId=${encodeURIComponent(tenantId)}`);
  renderSiemWebhooks(webhooks);
  setStatus(`${webhooks.length} SIEM webhook${webhooks.length === 1 ? "" : "s"} loaded`, "ok");
}

function renderSiemWebhooks(webhooks) {
  if (!webhooks.length) {
    siemWebhooksBody.innerHTML = '<tr><td colspan="4" class="empty">No SIEM webhooks in this tenant.</td></tr>';
    return;
  }

  siemWebhooksBody.innerHTML = webhooks.map((webhook) => `
    <tr>
      <td><code>${escapeHtml(webhook.webhookId)}</code></td>
      <td>${escapeHtml(webhook.url)}</td>
      <td>${renderEnabledBadge(webhook.enabled)}</td>
      <td>${escapeHtml(formatDate(webhook.createdAtUtc))}</td>
    </tr>
  `).join("");
}

function renderEnabledBadge(enabled) {
  return enabled
    ? '<span class="badge">Enabled</span>'
    : '<span class="badge disabled">Disabled</span>';
}
```

- [x] **Step 3: Implement CSS layout**

In `src/Drm.Server/wwwroot/admin/app.css`, add:

```css
.webhook-row {
  display: grid;
  grid-template-columns: minmax(180px, 0.8fr) minmax(260px, 1.6fr) minmax(120px, 0.4fr) auto;
  gap: 10px;
}

.badge.disabled {
  color: var(--muted);
  background: #ededed;
}
```

Include `.webhook-row` in the mobile rule.

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
## Phase 4H SIEM Console

The `/admin/` console now creates and lists SIEM webhook integrations through `/api/admin/siem-webhooks`. Operators can set webhook ID, HTTPS URL, and enabled state from the management UI.
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

Run a temp server, create `https://1.1.1.1/events` as a disabled webhook through the admin API, list webhooks, and verify `/admin/` includes `SIEM webhooks`.

- [x] **Step 4: Browser smoke**

Use Playwright CLI to open `/admin/`, snapshot the page, and confirm the SIEM section is visible with no console errors.

- [x] **Step 5: Commit**

Run:

```bash
git add README.md src/Drm.Server/wwwroot/admin tests/Drm.Server.Tests/ManagementConsoleTests.cs docs/superpowers/plans/2026-05-15-phase-4h-siem-console.md
git commit -m "feat: add siem webhooks to management console"
```

## Self-Review

- Spec coverage: Adds SIEM webhook create/list operations to the management console.
- Security note: API calls still go through `apiFetch`, preserving `X-DRM-Admin-Key`.
- Placeholder scan: No TBD/TODO placeholders.
