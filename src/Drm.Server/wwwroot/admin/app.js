const state = {
  tenantId: sessionStorage.getItem("drm:tenantId") || "",
  adminKey: sessionStorage.getItem("drm:adminKey") || ""
};

const tenantIdInput = document.querySelector("#tenantId");
const adminKeyInput = document.querySelector("#adminKey");
const connectionState = document.querySelector("#connectionState");
const usersBody = document.querySelector("#usersBody");
const healthOutput = document.querySelector("#healthOutput");

tenantIdInput.value = state.tenantId;
adminKeyInput.value = state.adminKey;

document.querySelector("#saveSession").addEventListener("click", () => {
  state.tenantId = tenantIdInput.value.trim();
  state.adminKey = adminKeyInput.value.trim();
  sessionStorage.setItem("drm:tenantId", state.tenantId);
  sessionStorage.setItem("drm:adminKey", state.adminKey);
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

async function refreshUsers() {
  const tenantId = requireTenantId();
  const users = await apiFetch(`/api/admin/users?tenantId=${encodeURIComponent(tenantId)}`);
  renderUsers(users);
  setStatus(`${users.length} user${users.length === 1 ? "" : "s"} loaded`, "ok");
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
