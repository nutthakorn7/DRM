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
const deviceHealthSummary = document.querySelector("#deviceHealthSummary");
const devicesBody = document.querySelector("#devicesBody");
const policyTemplatesBody = document.querySelector("#policyTemplatesBody");
const watermarkTemplatesBody = document.querySelector("#watermarkTemplatesBody");
const simulatorOutput = document.querySelector("#simulatorOutput");
const filesBody = document.querySelector("#filesBody");
const commandsBody = document.querySelector("#commandsBody");
const shareLinksBody = document.querySelector("#shareLinksBody");
const shareLinkCreatedOutput = document.querySelector("#shareLinkCreatedOutput");
const siemWebhooksBody = document.querySelector("#siemWebhooksBody");
const auditEventsBody = document.querySelector("#auditEventsBody");
const healthOutput = document.querySelector("#healthOutput");
const syncOutput = document.querySelector("#syncOutput");
const boxOutput = document.querySelector("#boxOutput");
const boxEventsBody = document.querySelector("#boxEventsBody");
const outlookOutput = document.querySelector("#outlookOutput");
const outlookEventsBody = document.querySelector("#outlookEventsBody");

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

document.querySelector("#refreshWatermarkTemplates").addEventListener("click", () => {
  refreshWatermarkTemplates();
});

document.querySelector("#createPolicyTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const offlineLeaseValue = document.querySelector("#templateOfflineLease").value.trim();
  const basePerms = document.querySelector("#templatePermissions").value.trim();
  const extras = [];
  if (document.querySelector("#templateAllowMacros").checked) extras.push("RunMacros");
  if (document.querySelector("#templateAllowTransferOwnership").checked) extras.push("TransferOwnership");
  const combinedPerms = extras.length
    ? (basePerms ? `${basePerms}, ${extras.join(", ")}` : extras.join(", "))
    : basePerms;
  const body = {
    tenantId: requireTenantId(),
    templateId: document.querySelector("#templateId").value.trim(),
    name: document.querySelector("#templateName").value.trim(),
    permissions: combinedPerms,
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

document.querySelector("#createWatermarkTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    watermarkTemplateId: document.querySelector("#watermarkTemplateId").value.trim(),
    name: document.querySelector("#watermarkTemplateName").value.trim(),
    pattern: document.querySelector("#watermarkTemplatePattern").value.trim()
  };

  await apiFetch("/api/admin/watermark-templates", {
    method: "POST",
    body: JSON.stringify(body)
  });

  event.target.reset();
  await refreshWatermarkTemplates();
});

document.querySelector("#updateWatermarkTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const templateId = document.querySelector("#acTemplateId").value.trim();
  if (!templateId) {
    setStatus("Anti-capture update requires template ID", "err");
    return;
  }
  const body = {
    tenantId: requireTenantId(),
    name: document.querySelector("#acName").value.trim(),
    pattern: document.querySelector("#acPattern").value.trim(),
    opacityPercent: Number(document.querySelector("#acOpacity").value),
    densityTiles: Number(document.querySelector("#acDensity").value),
    diagonalAngleDegrees: Number(document.querySelector("#acAngle").value),
    includeUserId: document.querySelector("#acIncludeUser").checked,
    includeTimestamp: document.querySelector("#acIncludeTimestamp").checked,
    includeIpAddress: document.querySelector("#acIncludeIp").checked,
    includeSessionId: document.querySelector("#acIncludeSession").checked,
    rollingEnabled: document.querySelector("#acRolling").checked,
    printWatermarkEnabled: document.querySelector("#acPrintEnabled").checked,
    printWatermarkPattern: document.querySelector("#acPrintPattern").value.trim(),
    printWatermarkOpacityPercent: Number(document.querySelector("#acPrintOpacity").value || "33"),
    printWatermarkPosition: document.querySelector("#acPrintPosition").value
  };

  await apiFetch(`/api/admin/watermark-templates/${encodeURIComponent(templateId)}`, {
    method: "PUT",
    body: JSON.stringify(body)
  });

  setStatus("Anti-capture settings updated", "ok");
  await refreshWatermarkTemplates();
});

document.querySelector("#simulatePolicyForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  await simulatePolicy();
});

document.querySelector("#refreshFiles").addEventListener("click", () => {
  refreshFiles();
});

document.querySelector("#refreshCommands").addEventListener("click", () => {
  refreshCommands();
});

document.querySelector("#refreshShareLinks").addEventListener("click", () => {
  refreshShareLinks();
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

document.querySelector("#directorySyncConfigForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    entraTenantId: document.querySelector("#dirEntraTenantId").value.trim(),
    clientId: document.querySelector("#dirClientId").value.trim(),
    clientSecret: document.querySelector("#dirClientSecret").value.trim()
  };
  await apiFetch("/api/admin/directory/config", {
    method: "PUT",
    body: JSON.stringify(body)
  });
  setStatus("Directory config saved", "ok");
});

document.querySelector("#triggerSync").addEventListener("click", async () => {
  syncOutput.textContent = "Syncing…";
  const body = { tenantId: requireTenantId() };
  const result = await apiFetch("/api/admin/directory/sync", {
    method: "POST",
    body: JSON.stringify(body)
  });
  syncOutput.textContent = result
    ? JSON.stringify(result, null, 2)
    : "Sync returned no result.";
});

document.querySelector("#boxConfigForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    clientId: document.querySelector("#boxClientId").value.trim(),
    clientSecret: document.querySelector("#boxClientSecret").value,
    enterpriseId: document.querySelector("#boxEnterpriseId").value.trim(),
    webhookSecret: document.querySelector("#boxWebhookSecret").value,
    enabled: document.querySelector("#boxEnabled").checked
  };
  await apiFetch("/api/admin/box/config", { method: "PUT", body: JSON.stringify(body) });
  setStatus("Box config saved", "ok");
});

document.querySelector("#refreshBoxConfig").addEventListener("click", async () => {
  try {
    const config = await apiFetch(`/api/admin/box/config?tenantId=${encodeURIComponent(requireTenantId())}`);
    if (config) {
      document.querySelector("#boxClientId").value = config.clientId ?? "";
      document.querySelector("#boxEnterpriseId").value = config.enterpriseId ?? "";
      document.querySelector("#boxEnabled").checked = !!config.enabled;
      boxOutput.textContent = JSON.stringify({
        enabled: config.enabled,
        lastConnectionStatus: config.lastConnectionStatus,
        lastConnectionAtUtc: config.lastConnectionAtUtc,
        lastWebhookEventCount: config.lastWebhookEventCount,
        updatedAtUtc: config.updatedAtUtc
      }, null, 2);
    }
    setStatus("Box config loaded", "ok");
  } catch (err) {
    boxOutput.textContent = `No config: ${err.message}`;
  }
});

document.querySelector("#testBoxConnection").addEventListener("click", async () => {
  boxOutput.textContent = "Testing Box connection…";
  const body = { tenantId: requireTenantId() };
  const result = await apiFetch("/api/admin/box/test-connection", {
    method: "POST",
    body: JSON.stringify(body)
  });
  boxOutput.textContent = JSON.stringify(result, null, 2);
  setStatus(result.success ? "Box connection OK" : "Box connection failed", result.success ? "ok" : "error");
});

document.querySelector("#refreshBoxEvents").addEventListener("click", async () => {
  const events = await apiFetch(`/api/admin/box/events?tenantId=${encodeURIComponent(requireTenantId())}`);
  if (!events.length) {
    boxEventsBody.innerHTML = '<tr><td colspan="5" class="empty">No Box webhook events received yet.</td></tr>';
    setStatus("No Box events", "ok");
    return;
  }
  boxEventsBody.innerHTML = events.map((e) => `
    <tr>
      <td>${escapeHtml(formatDate(e.receivedAtUtc))}</td>
      <td>${escapeHtml(e.eventType)}</td>
      <td>${escapeHtml(e.sourceItemName)}</td>
      <td><code>${escapeHtml(e.sourceItemId)}</code></td>
      <td>${escapeHtml(e.createdByEmail ?? "")}</td>
    </tr>
  `).join("");
  setStatus(`${events.length} Box event${events.length === 1 ? "" : "s"} loaded`, "ok");
});

// Outlook integration handlers
document.querySelector("#outlookConfigForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    enabled: document.querySelector("#outlookEnabled").checked,
    autoEncryptOutgoingAttachments: document.querySelector("#outlookAutoEncrypt").checked,
    minAttachmentSizeKb: Number(document.querySelector("#outlookMinSize").value || "0"),
    skipDomainsCsv: document.querySelector("#outlookSkipDomains").value.trim(),
    defaultPolicyTemplateId: document.querySelector("#outlookDefaultTemplate").value.trim() || null
  };
  await apiFetch("/api/admin/outlook/config", { method: "PUT", body: JSON.stringify(body) });
  setStatus("Outlook config saved", "ok");
});

document.querySelector("#refreshOutlookConfig").addEventListener("click", async () => {
  // Render manifest URL with the current origin so admins can copy it.
  document.querySelector("#outlookManifestUrl").textContent = `${location.origin}/outlook-addin/manifest.xml`;
  try {
    const config = await apiFetch(`/api/admin/outlook/config?tenantId=${encodeURIComponent(requireTenantId())}`);
    if (config) {
      document.querySelector("#outlookEnabled").checked = !!config.enabled;
      document.querySelector("#outlookAutoEncrypt").checked = !!config.autoEncryptOutgoingAttachments;
      document.querySelector("#outlookMinSize").value = config.minAttachmentSizeKb ?? 0;
      document.querySelector("#outlookSkipDomains").value = config.skipDomainsCsv ?? "";
      document.querySelector("#outlookDefaultTemplate").value = config.defaultPolicyTemplateId ?? "";
      outlookOutput.textContent = JSON.stringify({
        enabled: config.enabled,
        autoEncrypt: config.autoEncryptOutgoingAttachments,
        minSizeKb: config.minAttachmentSizeKb,
        lifetimeProtected: config.lifetimeProtectedCount,
        updatedAtUtc: config.updatedAtUtc
      }, null, 2);
    }
    setStatus("Outlook config loaded", "ok");
  } catch (err) {
    outlookOutput.textContent = `No config: ${err.message}`;
  }
});

document.querySelector("#refreshOutlookEvents").addEventListener("click", async () => {
  const events = await apiFetch(`/api/admin/outlook/events?tenantId=${encodeURIComponent(requireTenantId())}`);
  if (!events.length) {
    outlookEventsBody.innerHTML = '<tr><td colspan="7" class="empty">No Outlook events yet.</td></tr>';
    setStatus("No Outlook events", "ok");
    return;
  }
  outlookEventsBody.innerHTML = events.map((e) => `
    <tr>
      <td>${escapeHtml(formatDate(e.occurredAtUtc))}</td>
      <td>${escapeHtml(e.senderEmail)}</td>
      <td>${escapeHtml(e.recipientCsv)}</td>
      <td>${escapeHtml(e.attachmentName)}</td>
      <td>${formatBytes(e.attachmentSizeBytes)}</td>
      <td>${escapeHtml(e.status)}</td>
      <td>${e.protectedFileId ? `<code>${escapeHtml(e.protectedFileId)}</code>` : ""}</td>
    </tr>
  `).join("");
  setStatus(`${events.length} Outlook event${events.length === 1 ? "" : "s"} loaded`, "ok");
});

function formatBytes(bytes) {
  if (!bytes) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

document.querySelector("#loadNotificationConfig").addEventListener("click", async () => {
  const config = await apiFetch(`/api/admin/notification-config?tenantId=${requireTenantId()}`);
  if (config) {
    document.querySelector("#notifyAdminEmails").value = config.adminEmailsCsv ?? "";
    document.querySelector("#notifyOnExternalShareViewed").checked = config.notifyOnExternalShareViewed;
    document.querySelector("#notifyOnFileRevoked").checked = config.notifyOnFileRevoked;
    document.querySelector("#notifyOnAccessDenied").checked = config.notifyOnAccessDenied;
    document.querySelector("#notifyOnShareLinkCreated").checked = config.notifyOnShareLinkCreated;
  }
});

document.querySelector("#notificationConfigForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    adminEmailsCsv: document.querySelector("#notifyAdminEmails").value.trim(),
    notifyOnExternalShareViewed: document.querySelector("#notifyOnExternalShareViewed").checked,
    notifyOnFileRevoked: document.querySelector("#notifyOnFileRevoked").checked,
    notifyOnAccessDenied: document.querySelector("#notifyOnAccessDenied").checked,
    notifyOnShareLinkCreated: document.querySelector("#notifyOnShareLinkCreated").checked
  };
  await apiFetch("/api/admin/notification-config", {
    method: "PUT",
    body: JSON.stringify(body)
  });
  setStatus("Notification config saved", "ok");
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

document.querySelector("#applyPolicyTemplateForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  await applyPolicyTemplate();
  event.target.reset();
});

document.querySelector("#deleteCopyForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  await deleteProtectedCopy();
  event.target.reset();
});

document.querySelector("#createShareLinkForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  await createShareLink();
});

document.querySelector("#protectWizardForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  await runProtectWizard();
});

// File tagging
let activeTagFilter = null;

document.querySelector("#addTagForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const fileId = document.querySelector("#tagFileId").value.trim();
  const tag = document.querySelector("#tagValue").value.trim();
  if (!fileId || !tag) {
    setStatus("File ID and tag required", "error");
    return;
  }
  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/tags`, {
    method: "POST",
    body: JSON.stringify({ tenantId: requireTenantId(), tag })
  });
  document.querySelector("#tagValue").value = "";
  setStatus(`Tag "${tag}" added to ${fileId.slice(0, 8)}…`, "ok");
  await refreshTagChips();
});

document.querySelector("#refreshTags").addEventListener("click", refreshTagChips);

async function refreshTagChips() {
  const summaries = await apiFetch(`/api/admin/tags?tenantId=${encodeURIComponent(requireTenantId())}`);
  const host = document.querySelector("#tagChips");
  if (!summaries.length) {
    host.innerHTML = '<span class="hint">No tags yet.</span>';
    return;
  }
  host.innerHTML = summaries.map((s) => {
    const cls = activeTagFilter === s.tag ? "tag-chip active" : "tag-chip";
    return `<span class="${cls}" data-tag="${escapeHtml(s.tag)}">${escapeHtml(s.tag)} · ${s.fileCount}</span>`;
  }).join("");
  host.querySelectorAll("[data-tag]").forEach((el) => {
    el.addEventListener("click", () => {
      const tag = el.dataset.tag;
      activeTagFilter = activeTagFilter === tag ? null : tag;
      refreshTagChips();
      refreshFiles();
    });
  });
}

// License
document.querySelector("#loadLicense").addEventListener("click", async () => {
  const license = await apiFetch("/api/admin/license");
  const host = document.querySelector("#licenseTiers");
  const chips = license.enabledTiers.map((t) =>
    `<span class="license-chip">${escapeHtml(t)}</span>`).join("");
  host.innerHTML = `${chips}<div class="license-summary">${license.paidEncrypterCount} paid encrypters · <strong>${license.freeViewerCount}</strong> free viewers (×9)</div>`;
  setStatus(`${license.enabledTiers.length} tier${license.enabledTiers.length === 1 ? "" : "s"} enabled`, "ok");
});

filesBody.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-revoke-file-id]");
  if (!button) {
    return;
  }

  await revokeFile(button.dataset.revokeFileId);
});

shareLinksBody.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-revoke-share-link-id]");
  if (!button) {
    return;
  }

  await revokeShareLink(button.dataset.shareLinkFileId, button.dataset.revokeShareLinkId);
});

devicesBody.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-disable-device-id]");
  if (!button) {
    return;
  }

  await disableDevice(button.dataset.disableDeviceId);
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

  await refreshDeviceHealth();
  const devices = await apiFetch(`/api/admin/devices?${params.toString()}`);
  renderDevices(devices);
  setStatus(`${devices.length} device${devices.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshDeviceHealth() {
  const tenantId = requireTenantId();
  const staleAfterMinutes = document.querySelector("#deviceStaleAfterMinutes").value.trim() || "15";
  const params = new URLSearchParams({ tenantId, staleAfterMinutes });
  const health = await apiFetch(`/api/admin/devices/health?${params.toString()}`);
  renderDeviceHealth(health);
}

async function refreshPolicyTemplates() {
  const tenantId = requireTenantId();
  const templates = await apiFetch(`/api/admin/policy-templates?tenantId=${encodeURIComponent(tenantId)}`);
  renderPolicyTemplates(templates);
  setStatus(`${templates.length} template${templates.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshWatermarkTemplates() {
  const tenantId = requireTenantId();
  const templates = await apiFetch(`/api/admin/watermark-templates?tenantId=${encodeURIComponent(tenantId)}`);
  renderWatermarkTemplates(templates);
  setStatus(`${templates.length} watermark template${templates.length === 1 ? "" : "s"} loaded`, "ok");
}

async function simulatePolicy() {
  const body = {
    tenantId: requireTenantId(),
    fileId: document.querySelector("#simulateFileId").value.trim(),
    userId: document.querySelector("#simulateUserId").value.trim(),
    deviceId: document.querySelector("#simulateDeviceId").value.trim(),
    requestedPermission: document.querySelector("#simulatePermission").value.trim()
  };

  const decision = await apiFetch("/api/admin/policy-simulator", {
    method: "POST",
    body: JSON.stringify(body)
  });

  renderSimulation(decision);
  setStatus(`Simulation ${decision.allowed ? "allowed" : "denied"}: ${decision.reasonCode}`, decision.allowed ? "ok" : "error");
}

async function refreshFiles() {
  const tenantId = requireTenantId();
  const query = document.querySelector("#fileQuery").value.trim();
  const url = `/api/admin/files?tenantId=${encodeURIComponent(tenantId)}&q=${encodeURIComponent(query)}`;
  let files = await apiFetch(url);
  if (activeTagFilter) {
    const taggedIds = new Set(await apiFetch(
      `/api/admin/files-by-tag?tenantId=${encodeURIComponent(tenantId)}&tag=${encodeURIComponent(activeTagFilter)}`));
    files = files.filter((f) => taggedIds.has(f.fileId));
  }
  renderFiles(files);
  const suffix = activeTagFilter ? ` (filter: ${activeTagFilter})` : "";
  setStatus(`${files.length} file${files.length === 1 ? "" : "s"} loaded${suffix}`, "ok");
}

async function refreshCommands() {
  const fileId = document.querySelector("#commandFileId").value.trim();
  const params = new URLSearchParams({ tenantId: requireTenantId() });
  const deviceId = document.querySelector("#commandDeviceId").value.trim();
  if (deviceId) {
    params.set("deviceId", deviceId);
  }

  const commands = await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/commands?${params.toString()}`);
  renderCommands(commands);
  setStatus(`${commands.length} command${commands.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshShareLinks() {
  const fileId = document.querySelector("#shareLinksFileId").value.trim();
  const tenantId = requireTenantId();
  const links = await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/share-links?tenantId=${encodeURIComponent(tenantId)}`);
  renderShareLinks(links);
  setStatus(`${links.length} share link${links.length === 1 ? "" : "s"} loaded`, "ok");
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

async function applyPolicyTemplate() {
  const fileId = document.querySelector("#applyTemplateFileId").value.trim();
  const body = {
    tenantId: requireTenantId(),
    templateId: document.querySelector("#applyPolicyTemplateId").value.trim(),
    adminUserId: requireAdminUserId()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/apply-policy-template`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshFiles();
  setStatus("Policy template applied", "ok");
}

async function deleteProtectedCopy() {
  const fileId = document.querySelector("#deleteCopyFileId").value.trim();
  const body = {
    tenantId: requireTenantId(),
    deviceId: document.querySelector("#deleteCopyDeviceId").value.trim(),
    adminUserId: requireAdminUserId()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/commands/delete-protected-copy`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  setStatus("Delete command queued", "ok");
}

async function runProtectWizard() {
  const fileId = document.querySelector("#wizardFileId").value.trim();
  const policyTemplateId = document.querySelector("#wizardPolicyTemplateId").value.trim();
  const recipientType = document.querySelector("#wizardRecipientType").value;
  const recipientId = document.querySelector("#wizardRecipientId").value.trim();
  const permissions = document.querySelector("#wizardPermissions").value.trim() || "View";
  const guestEmail = document.querySelector("#wizardGuestEmail").value.trim();
  const shareExpiresValue = document.querySelector("#wizardShareExpires").value;
  const shareMaxUses = Number(document.querySelector("#wizardShareMaxUses").value || "1");
  const output = document.querySelector("#protectWizardOutput");
  const steps = [];

  if (!fileId) {
    setStatus("File ID required for wizard", "error");
    output.textContent = "ERR: File ID is required.";
    return;
  }

  const tenantId = requireTenantId();
  const adminUserId = requireAdminUserId();

  try {
    if (policyTemplateId) {
      await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/apply-policy-template`, {
        method: "POST",
        body: JSON.stringify({ tenantId, templateId: policyTemplateId, adminUserId })
      });
      steps.push(`✓ Applied policy template ${policyTemplateId}`);
    } else {
      steps.push("· Skipped policy template (none specified)");
    }

    if (recipientType && recipientId) {
      await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/grants`, {
        method: "POST",
        body: JSON.stringify({ tenantId, subjectType: recipientType, subjectId: recipientId, permissions })
      });
      steps.push(`✓ Granted ${permissions} to ${recipientType} ${recipientId}`);
    } else {
      steps.push("· Skipped recipient grant (none specified)");
    }

    if (guestEmail && shareExpiresValue) {
      const shareExpires = new Date(shareExpiresValue);
      if (Number.isNaN(shareExpires.getTime())) {
        throw new Error("Invalid share expiry date");
      }
      const created = await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/share-links`, {
        method: "POST",
        body: JSON.stringify({
          tenantId,
          adminUserId,
          guestEmail,
          expiresAtUtc: shareExpires.toISOString(),
          maxUses: shareMaxUses
        })
      });
      steps.push(`✓ Share link created for ${guestEmail}`);
      steps.push(`  token: ${created.shareToken || created.token || "(see Share Links table)"}`);
    } else {
      steps.push("· Skipped share link (email + expiry both required)");
    }

    output.textContent = steps.join("\n");
    setStatus("Protect wizard finished", "ok");
    await refreshFiles();
  } catch (error) {
    steps.push(`✗ ${error.message}`);
    output.textContent = steps.join("\n");
    throw error;
  }
}

async function createShareLink() {
  const fileId = document.querySelector("#shareLinkFileId").value.trim();
  const expiresAtValue = document.querySelector("#shareLinkExpiresAt").value;
  const expiresAt = new Date(expiresAtValue);
  if (!expiresAtValue || Number.isNaN(expiresAt.getTime())) {
    setStatus("Share link expiry required", "error");
    throw new Error("Share link expiry required");
  }

  const body = {
    tenantId: requireTenantId(),
    adminUserId: requireAdminUserId(),
    guestEmail: document.querySelector("#shareLinkGuestEmail").value.trim(),
    expiresAtUtc: expiresAt.toISOString(),
    maxUses: Number(document.querySelector("#shareLinkMaxUses").value || "1")
  };

  const created = await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/share-links`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  document.querySelector("#shareLinksFileId").value = fileId;
  renderCreatedShareLink(created);
  await refreshShareLinks();
  setStatus("External share link created", "ok");
}

async function revokeShareLink(fileId, shareLinkId) {
  const body = {
    tenantId: requireTenantId(),
    adminUserId: requireAdminUserId()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/share-links/${encodeURIComponent(shareLinkId)}/revoke`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshShareLinks();
  setStatus("External share link revoked", "ok");
}

async function disableDevice(deviceId) {
  const body = {
    tenantId: requireTenantId(),
    adminUserId: requireAdminUserId(),
    reason: "admin_disabled"
  };

  await apiFetch(`/api/admin/devices/${encodeURIComponent(deviceId)}/disable`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshDevices();
  setStatus("Device disabled", "ok");
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
    devicesBody.innerHTML = '<tr><td colspan="8" class="empty">No agent devices found.</td></tr>';
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
      <td>${isDeviceDisabled(device) ? "" : `<button class="danger" type="button" data-disable-device-id="${escapeHtml(device.deviceId)}">Disable</button>`}</td>
    </tr>
  `).join("");
}

function renderDeviceHealth(health) {
  deviceHealthSummary.innerHTML = `
    <div class="metric">
      <span>Total</span>
      <strong>${escapeHtml(health.total)}</strong>
    </div>
    <div class="metric">
      <span>Online</span>
      <strong>${escapeHtml(health.online)}</strong>
    </div>
    <div class="metric">
      <span>Stale</span>
      <strong>${escapeHtml(health.stale)}</strong>
    </div>
    <div class="metric">
      <span>Never seen</span>
      <strong>${escapeHtml(health.neverSeen)}</strong>
    </div>
    <div class="metric">
      <span>Disabled</span>
      <strong>${escapeHtml(health.disabled)}</strong>
    </div>
    <div class="metric wide">
      <span>Newest heartbeat</span>
      <strong>${escapeHtml(formatDate(health.newestHeartbeatAtUtc) || "-")}</strong>
    </div>
  `;
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

function renderWatermarkTemplates(templates) {
  if (!templates.length) {
    watermarkTemplatesBody.innerHTML = '<tr><td colspan="5" class="empty">No watermark templates in this tenant.</td></tr>';
    return;
  }

  watermarkTemplatesBody.innerHTML = templates.map((template) => `
    <tr>
      <td>${escapeHtml(template.name)}</td>
      <td><code>${escapeHtml(template.watermarkTemplateId)}</code></td>
      <td>${escapeHtml(template.pattern)}</td>
      <td>${escapeHtml(formatAntiCapture(template))}</td>
      <td>${escapeHtml(formatDate(template.createdAtUtc))}</td>
    </tr>
  `).join("");
}

function formatAntiCapture(template) {
  const parts = [
    `op ${template.opacityPercent}%`,
    `tiles ${template.densityTiles}`,
    `${template.diagonalAngleDegrees}°`
  ];
  const flags = [];
  if (template.includeUserId) flags.push("user");
  if (template.includeTimestamp) flags.push("ts");
  if (template.includeIpAddress) flags.push("ip");
  if (template.includeSessionId) flags.push("sid");
  if (template.rollingEnabled) flags.push("rolling");
  if (template.printWatermarkEnabled) flags.push(`print(${template.printWatermarkPosition})`);
  if (flags.length) parts.push(flags.join("+"));
  return parts.join(" · ");
}

function renderSimulation(decision) {
  simulatorOutput.textContent = JSON.stringify({
    allowed: decision.allowed,
    allowedPermissions: decision.allowedPermissions,
    reasonCode: decision.reasonCode,
    watermarkTemplate: decision.watermarkTemplate,
    offlineLeaseExpiresAtUtc: decision.offlineLeaseExpiresAtUtc,
    simulated: decision.simulated
  }, null, 2);
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

function renderCreatedShareLink(link) {
  shareLinkCreatedOutput.textContent = JSON.stringify({
    shareLinkId: link.shareLinkId,
    guestEmail: link.guestEmail,
    expiresAtUtc: link.expiresAtUtc,
    accessToken: link.accessToken,
    shareUrl: link.shareUrl
  }, null, 2);
}

function renderCommands(commands) {
  if (!commands.length) {
    commandsBody.innerHTML = '<tr><td colspan="7" class="empty">No commands found for this file.</td></tr>';
    return;
  }

  commandsBody.innerHTML = commands.map((command) => `
    <tr>
      <td><code>${escapeHtml(command.commandId)}</code></td>
      <td><code>${escapeHtml(command.deviceId)}</code></td>
      <td>${escapeHtml(command.commandType)}</td>
      <td>${escapeHtml(command.status)}</td>
      <td>${escapeHtml(command.reasonCode)}</td>
      <td>${escapeHtml(formatDate(command.createdAtUtc))}</td>
      <td>${escapeHtml(formatDate(command.completedAtUtc) || "-")}</td>
    </tr>
  `).join("");
}

function renderShareLinks(links) {
  if (!links.length) {
    shareLinksBody.innerHTML = '<tr><td colspan="7" class="empty">No external share links found for this file.</td></tr>';
    return;
  }

  shareLinksBody.innerHTML = links.map((link) => `
    <tr>
      <td><code>${escapeHtml(link.shareLinkId)}</code></td>
      <td>${escapeHtml(link.guestEmail)}</td>
      <td>${escapeHtml(`${link.usedCount}/${link.maxUses}`)}</td>
      <td>${renderShareLinkStatus(link)}</td>
      <td>${escapeHtml(formatDate(link.expiresAtUtc))}</td>
      <td>${escapeHtml(formatDate(link.createdAtUtc))}</td>
      <td>${isShareLinkInactive(link) ? "" : `<button class="danger" type="button" data-share-link-file-id="${escapeHtml(link.fileId)}" data-revoke-share-link-id="${escapeHtml(link.shareLinkId)}">Revoke</button>`}</td>
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

function renderShareLinkStatus(link) {
  if (link.revoked) {
    return '<span class="badge revoked">Revoked</span>';
  }

  if (new Date(link.expiresAtUtc) <= new Date()) {
    return '<span class="badge disabled">Expired</span>';
  }

  return '<span class="badge">Active</span>';
}

function isShareLinkInactive(link) {
  return link.revoked || new Date(link.expiresAtUtc) <= new Date();
}

function isDeviceDisabled(device) {
  return device.status === "disabled" || Boolean(device.disabledAtUtc);
}
