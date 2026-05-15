# Phase 4F Policy Template Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose existing admin policy-template create/list APIs in the `/admin/` management console.

**Architecture:** Keep the existing `/api/admin/policy-templates` backend unchanged. Add console controls that create templates, list tenant templates, and render policy fields operators need: name, permissions, watermark template, offline lease, and print allowance. Cover the console asset contract with tests before changing HTML/JS/CSS.

**Tech Stack:** .NET 10 minimal APIs, vanilla JS console, static HTML/CSS, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert the console includes policy-template UI labels and JS API references.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add navigation and a policy templates section with create/list controls.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: wire create/list actions to `/api/admin/policy-templates`.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add a template form layout that works with the existing console grid.
- Modify `README.md`: document that the console manages policy templates.

## Tasks

### Task 1: Console Policy Template Tests

- [x] **Step 1: Write the failing asset tests**

Update `tests/Drm.Server.Tests/ManagementConsoleTests.cs` so `Admin_console_index_is_served` asserts:

```csharp
html.Should().Contain("Policy templates");
html.Should().Contain("Watermark template");
html.Should().Contain("Offline lease");
html.Should().Contain("Allow print");
```

Update `Admin_console_assets_are_served` so it asserts:

```csharp
js.Should().Contain("/api/admin/policy-templates");
js.Should().Contain("refreshPolicyTemplates");
js.Should().Contain("createPolicyTemplateForm");
```

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because the console does not yet include policy template UI or JS wiring.

### Task 2: Console Policy Template UI

- [x] **Step 1: Implement HTML controls**

In `src/Drm.Server/wwwroot/admin/index.html`, add a `Templates` navigation link and a `#templates` section with:

```html
<section class="panel" id="templates">
  <div class="section-head">
    <div>
      <p class="eyebrow">Policy</p>
      <h3>Policy templates</h3>
    </div>
    <button id="refreshPolicyTemplates" type="button">Refresh templates</button>
  </div>

  <form class="template-row" id="createPolicyTemplateForm">
    <input id="templateId" autocomplete="off" placeholder="Template ID">
    <input id="templateName" autocomplete="off" placeholder="Name">
    <input id="templatePermissions" autocomplete="off" placeholder="Permissions, e.g. View, Print">
    <input id="templateWatermark" autocomplete="off" placeholder="Watermark template">
    <input id="templateOfflineLease" type="number" min="0" max="527040" autocomplete="off" placeholder="Offline lease minutes">
    <label class="check-row">
      <input id="templateAllowPrint" type="checkbox">
      <span>Allow print</span>
    </label>
    <button type="submit">Create template</button>
  </form>

  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Name</th>
          <th>Template ID</th>
          <th>Permissions</th>
          <th>Watermark template</th>
          <th>Offline lease</th>
          <th>Allow print</th>
        </tr>
      </thead>
      <tbody id="policyTemplatesBody">
        <tr>
          <td colspan="6" class="empty">Refresh to load policy templates.</td>
        </tr>
      </tbody>
    </table>
  </div>
</section>
```

- [x] **Step 2: Implement JS behavior**

In `src/Drm.Server/wwwroot/admin/app.js`, add:

```javascript
const policyTemplatesBody = document.querySelector("#policyTemplatesBody");

document.querySelector("#refreshPolicyTemplates").addEventListener("click", () => {
  refreshPolicyTemplates();
});

document.querySelector("#createPolicyTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    templateId: document.querySelector("#templateId").value.trim(),
    name: document.querySelector("#templateName").value.trim(),
    permissions: document.querySelector("#templatePermissions").value.trim(),
    watermarkTemplate: document.querySelector("#templateWatermark").value.trim(),
    offlineLeaseMinutes: Number(document.querySelector("#templateOfflineLease").value || 0),
    allowPrint: document.querySelector("#templateAllowPrint").checked
  };

  await apiFetch("/api/admin/policy-templates", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  await refreshPolicyTemplates();
});

async function refreshPolicyTemplates() {
  const tenantId = requireTenantId();
  const templates = await apiFetch(`/api/admin/policy-templates?tenantId=${encodeURIComponent(tenantId)}`);
  renderPolicyTemplates(templates);
  setStatus(`${templates.length} template${templates.length === 1 ? "" : "s"} loaded`, "ok");
}

function renderPolicyTemplates(templates) {
  if (!templates.length) {
    policyTemplatesBody.innerHTML = '<tr><td colspan="6" class="empty">No policy templates in this tenant.</td></tr>';
    return;
  }

  policyTemplatesBody.innerHTML = templates.map((template) => `
    <tr>
      <td>${escapeHtml(template.name)}</td>
      <td><code>${escapeHtml(template.templateId)}</code></td>
      <td>${escapeHtml(template.permissions)}</td>
      <td>${escapeHtml(template.watermarkTemplate)}</td>
      <td>${escapeHtml(`${template.offlineLeaseMinutes} min`)}</td>
      <td>${template.allowPrint ? "Yes" : "No"}</td>
    </tr>
  `).join("");
}
```

- [x] **Step 3: Implement CSS layout**

In `src/Drm.Server/wwwroot/admin/app.css`, add:

```css
.template-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(180px, 1fr));
  gap: 10px;
}

.check-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 39px;
}

.check-row input {
  width: auto;
  min-height: auto;
}
```

Also include `.template-row` in the mobile grid rule.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

- [x] **Step 5: Add explicit favicon link after browser smoke**

Playwright smoke found a `/favicon.ico` 404. Add this link to `src/Drm.Server/wwwroot/admin/index.html` and cover it with `ManagementConsoleTests`:

```html
<link rel="icon" href="data:,">
```

### Task 3: Documentation, Verification, Commit

- [x] **Step 1: Update README**

Add a Phase 4F note:

```markdown
## Phase 4F Policy Template Console

The `/admin/` console now creates and lists policy templates through `/api/admin/policy-templates`. Operators can set template name, permissions, watermark template, offline lease minutes, and print allowance from the management UI.
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

Run the server with a temporary SQLite database and `DRM_ADMIN_API_KEY`, then verify:

```bash
curl -fsS http://127.0.0.1:5080/admin/ | grep "Policy templates"
curl -fsS http://127.0.0.1:5080/admin/app.js | grep "/api/admin/policy-templates"
```

Expected: both checks pass.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md src/Drm.Server/wwwroot/admin tests/Drm.Server.Tests/ManagementConsoleTests.cs docs/superpowers/plans/2026-05-15-phase-4f-policy-template-console.md
git commit -m "feat: add policy templates to management console"
```

## Self-Review

- Spec coverage: Exposes existing policy-template create/list APIs in the management console.
- Security note: Console calls remain protected by `X-DRM-Admin-Key` through `apiFetch`.
- Placeholder scan: No TBD/TODO placeholders.
