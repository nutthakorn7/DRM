const state = {
  tenantId: sessionStorage.getItem("drm:tenantId") || "",
  adminKey: sessionStorage.getItem("drm:adminKey") || "",
  adminUserId: sessionStorage.getItem("drm:adminUserId") || ""
};

const tenantIdInput = document.querySelector("#tenantId");
const adminKeyInput = document.querySelector("#adminKey");
const adminUserIdInput = document.querySelector("#adminUserId");
const connectionState = document.querySelector("#connectionState");
const usersBody = document.querySelector("#usersBody");
const groupMembersBody = document.querySelector("#groupMembersBody");
const devicesBody = document.querySelector("#devicesBody");
const policyTemplatesBody = document.querySelector("#policyTemplatesBody");
const filesBody = document.querySelector("#filesBody");
const siemWebhooksBody = document.querySelector("#siemWebhooksBody");
const auditEventsBody = document.querySelector("#auditEventsBody");
const healthOutput = document.querySelector("#healthOutput");

tenantIdInput.value = state.tenantId;
adminKeyInput.value = state.adminKey;
adminUserIdInput.value = state.adminUserId;

document.querySelector("#saveSession").addEventListener("click", () => {
  state.tenantId = tenantIdInput.value.trim();
  state.adminKey = adminKeyInput.value.trim();
  state.adminUserId = adminUserIdInput.value.trim();
  sessionStorage.setItem("drm:tenantId", state.tenantId);
  sessionStorage.setItem("drm:adminKey", state.adminKey);
  sessionStorage.setItem("drm:adminUserId", state.adminUserId);
  setStatus("Session saved", "ok");
});

document.querySelector("#refreshUsers").addEventListener("click", () => {
  refreshUsers();
});

document.querySelector("#checkHealth").addEventListener("click", async () => {
  const response = await fetch("/healthz");
  healthOutput.textContent = JSON.stringify(await response.json(), null, 2);
});

document.querySelector("#createUserForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    userId: document.querySelector("#newUserId").value.trim(),
    email: document.querySelector("#newUserEmail").value.trim(),
    displayName: document.querySelector("#newUserDisplayName").value.trim()
  };

  await apiFetch("/api/admin/users", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  await refreshUsers();
});

document.querySelector("#createGroupForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    groupId: document.querySelector("#newGroupId").value.trim(),
    name: document.querySelector("#newGroupName").value.trim()
  };

  await apiFetch("/api/admin/groups", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  setStatus("Group created", "ok");
});

document.querySelector("#addGroupMemberForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const groupId = document.querySelector("#memberGroupId").value.trim();
  const body = {
    tenantId: requireTenantId(),
    userId: document.querySelector("#memberUserId").value.trim()
  };

  await apiFetch(`/api/admin/groups/${encodeURIComponent(groupId)}/members`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshGroupMembers(groupId);
});

document.querySelector("#listGroupMembers").addEventListener("click", () => {
  refreshGroupMembers(document.querySelector("#memberGroupId").value.trim());
});

document.querySelector("#refreshDevices").addEventListener("click", () => {
  refreshDevices();
});

document.querySelector("#refreshPolicyTemplates").addEventListener("click", () => {
  refreshPolicyTemplates();
});

document.querySelector("#createPolicyTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const offlineLeaseValue = document.querySelector("#templateOfflineLease").value.trim();
  const body = {
    tenantId: requireTenantId(),
    templateId: document.querySelector("#templateId").value.trim(),
    name: document.querySelector("#templateName").value.trim(),
    permissions: document.querySelector("#templatePermissions").value.trim(),
    watermarkTemplate: document.querySelector("#templateWatermark").value.trim(),
    offlineLeaseMinutes: offlineLeaseValue ? Number(offlineLeaseValue) : 0,
    allowPrint: document.querySelector("#templateAllowPrint").checked
  };

  await apiFetch("/api/admin/policy-templates", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  await refreshPolicyTemplates();
});

document.querySelector("#refreshFiles").addEventListener("click", () => {
  refreshFiles();
});

document.querySelector("#refreshAuditEvents").addEventListener("click", () => {
  refreshAuditEvents();
});

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

document.querySelector("#downloadAuditCsv").addEventListener("click", () => {
  downloadAuditCsv();
});

document.querySelector("#grantForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const fileId = document.querySelector("#grantFileId").value.trim();
  const body = {
    tenantId: requireTenantId(),
    subjectType: document.querySelector("#grantSubjectType").value,
    subjectId: document.querySelector("#grantSubjectId").value.trim(),
    permissions: document.querySelector("#grantPermissions").value.trim()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/grants`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  setStatus("Grant saved", "ok");
});

filesBody.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-revoke-file-id]");
  if (!button) {
    return;
  }

  await revokeFile(button.dataset.revokeFileId);
});

async function refreshUsers() {
  const tenantId = requireTenantId();
  const users = await apiFetch(`/api/admin/users?tenantId=${encodeURIComponent(tenantId)}`);
  renderUsers(users);
  setStatus(`${users.length} user${users.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshGroupMembers(groupId) {
  if (!groupId) {
    setStatus("Group ID required", "error");
    throw new Error("Group ID required");
  }

  const tenantId = requireTenantId();
  const members = await apiFetch(`/api/admin/groups/${encodeURIComponent(groupId)}/members?tenantId=${encodeURIComponent(tenantId)}`);
  renderGroupMembers(members);
  setStatus(`${members.length} member${members.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshDevices() {
  const tenantId = requireTenantId();
  const params = new URLSearchParams({ tenantId });
  const status = document.querySelector("#deviceStatusFilter").value.trim();
  const userId = document.querySelector("#deviceUserFilter").value.trim();
  if (status) {
    params.set("status", status);
  }

  if (userId) {
    params.set("userId", userId);
  }

  const devices = await apiFetch(`/api/admin/devices?${params.toString()}`);
  renderDevices(devices);
  setStatus(`${devices.length} device${devices.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshPolicyTemplates() {
  const tenantId = requireTenantId();
  const templates = await apiFetch(`/api/admin/policy-templates?tenantId=${encodeURIComponent(tenantId)}`);
  renderPolicyTemplates(templates);
  setStatus(`${templates.length} template${templates.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshFiles() {
  const tenantId = requireTenantId();
  const query = document.querySelector("#fileQuery").value.trim();
  const url = `/api/admin/files?tenantId=${encodeURIComponent(tenantId)}&q=${encodeURIComponent(query)}`;
  const files = await apiFetch(url);
  renderFiles(files);
  setStatus(`${files.length} file${files.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshSiemWebhooks() {
  const tenantId = requireTenantId();
  const webhooks = await apiFetch(`/api/admin/siem-webhooks?tenantId=${encodeURIComponent(tenantId)}`);
  renderSiemWebhooks(webhooks);
  setStatus(`${webhooks.length} SIEM webhook${webhooks.length === 1 ? "" : "s"} loaded`, "ok");
}

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

async function revokeFile(fileId) {
  const body = {
    tenantId: requireTenantId(),
    adminUserId: requireAdminUserId()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/revoke`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshFiles();
  setStatus("File revoked", "ok");
}

async function apiFetch(url, options = {}) {
  const adminKey = requireAdminKey();
  const response = await fetch(url, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-DRM-Admin-Key": adminKey,
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    setStatus(`Request failed: ${response.status}`, "error");
    throw new Error(`Request failed with HTTP ${response.status}`);
  }

  if (response.status === 204) {
    return null;
  }

  return response.json();
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

function renderUsers(users) {
  if (!users.length) {
    usersBody.innerHTML = '<tr><td colspan="3" class="empty">No users in this tenant.</td></tr>';
    return;
  }

  usersBody.innerHTML = users.map((user) => `
    <tr>
      <td>${escapeHtml(user.email)}</td>
      <td>${escapeHtml(user.displayName)}</td>
      <td><code>${escapeHtml(user.userId)}</code></td>
    </tr>
  `).join("");
}

function renderGroupMembers(members) {
  if (!members.length) {
    groupMembersBody.innerHTML = '<tr><td colspan="2" class="empty">No members in this group.</td></tr>';
    return;
  }

  groupMembersBody.innerHTML = members.map((member) => `
    <tr>
      <td><code>${escapeHtml(member.groupId)}</code></td>
      <td><code>${escapeHtml(member.userId)}</code></td>
    </tr>
  `).join("");
}

function renderDevices(devices) {
  if (!devices.length) {
    devicesBody.innerHTML = '<tr><td colspan="7" class="empty">No agent devices found.</td></tr>';
    return;
  }

  devicesBody.innerHTML = devices.map((device) => `
    <tr>
      <td>${escapeHtml(device.hostname)}</td>
      <td><code>${escapeHtml(device.deviceId)}</code></td>
      <td><code>${escapeHtml(device.userId)}</code></td>
      <td>${escapeHtml(device.operatingSystem)}</td>
      <td>${escapeHtml(device.agentVersion)}</td>
      <td>${escapeHtml(device.status)}</td>
      <td>${escapeHtml(formatDate(device.lastHeartbeatAtUtc))}</td>
    </tr>
  `).join("");
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

function renderFiles(files) {
  if (!files.length) {
    filesBody.innerHTML = '<tr><td colspan="7" class="empty">No protected files found.</td></tr>';
    return;
  }

  filesBody.innerHTML = files.map((file) => `
    <tr>
      <td><code>${escapeHtml(file.fileId)}</code></td>
      <td><code>${escapeHtml(file.ownerUserId)}</code></td>
      <td>${escapeHtml(file.contentType)}</td>
      <td>${escapeHtml(file.permissions)}</td>
      <td>${renderRevokedBadge(file.revoked)}</td>
      <td>${escapeHtml(formatDate(file.expiresAtUtc))}</td>
      <td>${file.revoked ? "" : `<button class="danger" type="button" data-revoke-file-id="${escapeHtml(file.fileId)}">Revoke</button>`}</td>
    </tr>
  `).join("");
}

function requireTenantId() {
  const tenantId = tenantIdInput.value.trim();
  if (!tenantId) {
    setStatus("Tenant ID required", "error");
    throw new Error("Tenant ID required");
  }

  return tenantId;
}

function requireAdminKey() {
  const adminKey = adminKeyInput.value.trim();
  if (!adminKey) {
    setStatus("Admin API key required", "error");
    throw new Error("Admin API key required");
  }

  return adminKey;
}

function requireAdminUserId() {
  const adminUserId = adminUserIdInput.value.trim();
  if (!adminUserId) {
    setStatus("Admin user ID required", "error");
    throw new Error("Admin user ID required");
  }

  return adminUserId;
}

function buildAuditUrl(path) {
  const tenantId = requireTenantId();
  const eventType = document.querySelector("#auditEventType").value.trim();
  const params = new URLSearchParams({ tenantId, eventType });
  return `${path}?${params.toString()}`;
}

function setStatus(message, mode) {
  connectionState.textContent = message;
  connectionState.className = `status-line ${mode || ""}`.trim();
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function formatDate(value) {
  if (!value) {
    return "";
  }

  return new Date(value).toLocaleString();
}

function renderRevokedBadge(revoked) {
  return revoked
    ? '<span class="badge revoked">Revoked</span>'
    : '<span class="badge">Active</span>';
}

function renderEnabledBadge(enabled) {
  return enabled
    ? '<span class="badge">Enabled</span>'
    : '<span class="badge disabled">Disabled</span>';
}
