// /me/ — Quick Share landing page for non-technical users.
// One form, three actions (drop, type, click), one share URL out.

const SESSION_KEY = "drm-me-session-v1";
const TOUR_KEY = "drm-me-tour-completed-v1";
const ROLE_PICKER_KEY = "drm-me-role-picked-v1";

const ROLE_DETAILS = [
  {
    id: "Employee",
    icon: "👤",
    title: "Employee / Sales rep",
    blurb: "Send proposals, quotes, and pitch decks. The simplest defaults: 1-week expiry, no print, one recipient at a time."
  },
  {
    id: "KnowledgeWorker",
    icon: "📚",
    title: "Lawyer, HR, finance analyst",
    blurb: "Send sensitive docs and revoke them if engagement ends. You manage your own files plus see who opened what."
  },
  {
    id: "Executive",
    icon: "🎯",
    title: "Executive / board member",
    blurb: "Distribute board decks and IR material with no-print + no-photo. Tenant-wide audit view."
  },
  {
    id: "Admin",
    icon: "🛠",
    title: "IT admin",
    blurb: "Configure tenants, watermark templates, integrations, audit, and DRM policy. You'll spend most time in /admin/."
  }
];

const tenantIdInput = document.querySelector("#tenantId");
const userIdInput = document.querySelector("#userId");
const sessionLabel = document.querySelector("#sessionLabel");
const personaBadge = document.querySelector("#personaBadge");
const adminLink = document.querySelector("#adminLink");
const dropZone = document.querySelector("#dropZone");
const fileInput = document.querySelector("#fileInput");
const fileSummary = document.querySelector("#fileSummary");
const recipientInput = document.querySelector("#recipient");
const expiresHoursInput = document.querySelector("#expiresHours");
const allowPrintInput = document.querySelector("#allowPrint");
const sendButton = document.querySelector("#sendButton");
const result = document.querySelector("#result");
const shareUrlInput = document.querySelector("#shareUrl");
const expiresAtSpan = document.querySelector("#expiresAt");
const fileIdSummary = document.querySelector("#fileIdSummary");
const errorBox = document.querySelector("#errorBox");
const sessionDetails = document.querySelector("#sessionDetails");
const anotherFileLink = document.querySelector("#anotherFileLink");
const copyBtn = document.querySelector("#copyBtn");

let pickedFile = null;

function loadSession() {
  try { return JSON.parse(localStorage.getItem(SESSION_KEY) ?? "null"); }
  catch { return null; }
}

function saveSession() {
  localStorage.setItem(SESSION_KEY, JSON.stringify({
    tenantId: tenantIdInput.value.trim(),
    userId: userIdInput.value.trim()
  }));
  updateSessionLabel();
}

function updateSessionLabel() {
  const t = tenantIdInput.value.trim();
  const u = userIdInput.value.trim();
  if (t && u) {
    sessionLabel.textContent = `tenant ${t.slice(0, 8)}… · user ${u.slice(0, 8)}…`;
    sessionDetails.removeAttribute("open");
  } else {
    sessionLabel.textContent = "(not configured)";
    sessionDetails.setAttribute("open", "");
  }
}

function showError(message) {
  errorBox.textContent = message;
  errorBox.hidden = false;
  setTimeout(() => { errorBox.hidden = true; }, 6000);
}

async function loadPersona() {
  const t = tenantIdInput.value.trim();
  const u = userIdInput.value.trim();
  if (!t || !u) { personaBadge.textContent = "Sign-in needed"; return; }
  try {
    const resp = await fetch(`/api/me/persona?tenantId=${encodeURIComponent(t)}&userId=${encodeURIComponent(u)}`);
    if (!resp.ok) { personaBadge.textContent = "Persona unknown"; return; }
    const persona = await resp.json();
    personaBadge.textContent = persona.persona;
    adminLink.hidden = !persona.canAdmin;
    if (persona.canAdmin) {
      adminLink.href = "/admin/";
    }
  } catch (err) {
    personaBadge.textContent = "Offline";
  }
}

async function loadRecentRecipients() {
  const t = tenantIdInput.value.trim();
  const u = userIdInput.value.trim();
  const datalist = document.querySelector("#recentRecipientsList");
  const hint = document.querySelector("#recentHint");
  if (!t || !u || !datalist) return;
  try {
    const resp = await fetch(
      `/api/me/recent-recipients?tenantId=${encodeURIComponent(t)}&userId=${encodeURIComponent(u)}&limit=8`);
    if (!resp.ok) return;
    const recents = await resp.json();
    datalist.innerHTML = recents
      .map((r) => `<option value="${r.email}">${r.email} · ${r.useCount} send${r.useCount === 1 ? "" : "s"}</option>`)
      .join("");
    if (recents.length && !recipientInput.value) {
      hint.hidden = false;
      hint.textContent = `Last used: ${recents[0].email} — start typing to pick from ${recents.length} recent recipient${recents.length === 1 ? "" : "s"}.`;
    } else {
      hint.hidden = true;
    }
  } catch { /* ignore */ }
}

// Drop zone wiring
dropZone.addEventListener("click", () => fileInput.click());
dropZone.addEventListener("dragenter", (e) => { e.preventDefault(); dropZone.classList.add("is-over"); });
dropZone.addEventListener("dragover", (e) => { e.preventDefault(); });
dropZone.addEventListener("dragleave", () => dropZone.classList.remove("is-over"));
dropZone.addEventListener("drop", (e) => {
  e.preventDefault();
  dropZone.classList.remove("is-over");
  if (e.dataTransfer.files.length) {
    fileInput.files = e.dataTransfer.files;
    updateFileSummary();
  }
});
fileInput.addEventListener("change", updateFileSummary);

function updateFileSummary() {
  pickedFile = fileInput.files[0] ?? null;
  if (!pickedFile) { fileSummary.textContent = ""; return; }
  fileSummary.textContent = `📎 ${pickedFile.name} · ${formatBytes(pickedFile.size)}`;
}

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

async function fileToBase64(file) {
  const buf = await file.arrayBuffer();
  const bytes = new Uint8Array(buf);
  // Chunk to avoid call-stack blowup on large files.
  const chunkSize = 32768;
  let binary = "";
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}

// Submit
document.querySelector("#quickShareForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  errorBox.hidden = true;

  const tenantId = tenantIdInput.value.trim();
  const userId = userIdInput.value.trim();
  if (!tenantId || !userId) {
    showError("Enter Tenant ID and User ID in the sign-in panel first.");
    sessionDetails.setAttribute("open", "");
    return;
  }
  if (!pickedFile) {
    showError("Drop or pick a file first.");
    return;
  }
  const recipient = recipientInput.value.trim();
  if (!recipient || !recipient.includes("@")) {
    showError("Enter a recipient email address.");
    recipientInput.focus();
    return;
  }

  sendButton.disabled = true;
  sendButton.textContent = "Sending…";
  try {
    saveSession();
    const fileBytesBase64 = await fileToBase64(pickedFile);
    const resp = await fetch("/api/me/share", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        tenantId, userId,
        recipientEmail: recipient,
        fileName: pickedFile.name,
        contentType: pickedFile.type || "application/octet-stream",
        fileBytesBase64,
        expiresInHours: Number(expiresHoursInput.value || "168"),
        allowPrint: allowPrintInput.checked
      })
    });
    if (!resp.ok) {
      const body = await resp.text();
      throw new Error(`Server returned ${resp.status}: ${body}`);
    }
    const data = await resp.json();
    shareUrlInput.value = data.shareUrl;
    expiresAtSpan.textContent = new Date(data.expiresAtUtc).toLocaleString();
    fileIdSummary.textContent = data.fileId;
    result.hidden = false;
    result.scrollIntoView({ behavior: "smooth", block: "nearest" });
  } catch (err) {
    showError(err.message ?? String(err));
  } finally {
    sendButton.disabled = false;
    sendButton.textContent = "Send protected file";
  }
});

copyBtn.addEventListener("click", async () => {
  shareUrlInput.select();
  try {
    await navigator.clipboard.writeText(shareUrlInput.value);
    copyBtn.textContent = "Copied ✓";
    setTimeout(() => { copyBtn.textContent = "Copy link"; }, 1500);
  } catch { document.execCommand("copy"); }
});

anotherFileLink.addEventListener("click", (e) => {
  e.preventDefault();
  result.hidden = true;
  pickedFile = null;
  fileInput.value = "";
  fileSummary.textContent = "";
  recipientInput.value = "";
  recipientInput.focus();
});

[tenantIdInput, userIdInput].forEach((el) => {
  el.addEventListener("change", () => { saveSession(); loadPersona(); loadRecentRecipients(); });
});

// Bootstrap
(function bootstrap() {
  const s = loadSession();
  if (s) {
    tenantIdInput.value = s.tenantId ?? "";
    userIdInput.value = s.userId ?? "";
  }
  updateSessionLabel();
  loadPersona();
  loadRecentRecipients();
  if (!maybeShowRolePicker()) {
    maybeShowTour();
  }
})();

function maybeShowRolePicker() {
  if (localStorage.getItem(ROLE_PICKER_KEY)) return false;

  const overlay = document.createElement("div");
  overlay.className = "role-picker-overlay";
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.55);z-index:9998;display:flex;align-items:center;justify-content:center;padding:24px";
  const card = document.createElement("div");
  card.style.cssText = "background:#fff;padding:28px;border-radius:12px;max-width:680px;width:100%;box-shadow:0 10px 30px rgba(0,0,0,0.3)";
  card.innerHTML = `
    <h2 style="margin:0 0 6px 0;font-size:1.4rem">Which best describes you?</h2>
    <p style="margin:0 0 18px 0;color:#6b7280">This personalises the UI hints on this device. Your actual permissions are set by your IT admin and won't change here.</p>
    <div class="role-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:12px"></div>
    <button type="button" class="skip" style="margin-top:14px;padding:6px 12px;background:transparent;border:none;color:#6b7280;cursor:pointer">Skip for now</button>`;
  const grid = card.querySelector(".role-grid");
  for (const role of ROLE_DETAILS) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.dataset.role = role.id;
    btn.style.cssText = "text-align:left;padding:14px;border:1px solid #e5e7eb;border-radius:8px;background:#fff;cursor:pointer;font-family:inherit";
    btn.innerHTML = `
      <div style="font-size:24px;line-height:1">${role.icon}</div>
      <div style="margin-top:6px;font-weight:600;color:#111827">${role.title}</div>
      <div style="margin-top:4px;font-size:0.85rem;color:#6b7280;line-height:1.4">${role.blurb}</div>`;
    btn.onmouseover = () => { btn.style.borderColor = "#a45b13"; btn.style.background = "#fff8f0"; };
    btn.onmouseout = () => { btn.style.borderColor = "#e5e7eb"; btn.style.background = "#fff"; };
    btn.onclick = () => {
      localStorage.setItem(ROLE_PICKER_KEY, role.id);
      personaBadge.textContent = role.id + " (self-declared)";
      overlay.remove();
      maybeShowTour();
    };
    grid.appendChild(btn);
  }
  card.querySelector(".skip").onclick = () => {
    localStorage.setItem(ROLE_PICKER_KEY, "skipped");
    overlay.remove();
    maybeShowTour();
  };
  overlay.appendChild(card);
  document.body.appendChild(overlay);
  return true;
}

function maybeShowTour() {
  if (localStorage.getItem(TOUR_KEY)) return;
  // Lightweight tour: highlight 4 elements briefly. We avoid an external
  // dependency by adding a single tooltip per stop with a "Next" button.
  const stops = [
    { selector: "#dropZone",      text: "Drop any file here. We'll protect it before sharing." },
    { selector: "#recipient",     text: "Type one email. The recipient gets a verification link — no DRM client on their side." },
    { selector: ".advanced",      text: "Want to expire after 24 h or block printing? Open Advanced." },
    { selector: "#sendButton",    text: "Click Send. The share URL lands in your clipboard." }
  ];

  let idx = 0;
  const overlay = document.createElement("div");
  overlay.className = "tour-overlay";
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:9999;display:flex;align-items:center;justify-content:center;pointer-events:auto;";
  const card = document.createElement("div");
  card.style.cssText = "background:#fff;padding:18px 22px;border-radius:10px;max-width:340px;text-align:center;box-shadow:0 8px 30px rgba(0,0,0,0.25);";
  overlay.appendChild(card);
  document.body.appendChild(overlay);

  function render() {
    if (idx >= stops.length) {
      localStorage.setItem(TOUR_KEY, "1");
      overlay.remove();
      return;
    }
    const stop = stops[idx];
    const anchor = document.querySelector(stop.selector);
    card.innerHTML = `
      <p style="margin:0 0 12px 0;font-size:0.95rem;color:#111827">${stop.text}</p>
      <button type="button" class="primary" style="padding:8px 18px;border-radius:6px;background:#a45b13;color:#fff;border:none;font-weight:600;cursor:pointer">${idx === stops.length - 1 ? "Finish" : "Next"}</button>
      <button type="button" class="skip" style="margin-left:8px;padding:8px 12px;background:transparent;border:none;color:#6b7280;cursor:pointer">Skip</button>`;
    if (anchor) anchor.scrollIntoView({ block: "center" });
    card.querySelector(".primary").onclick = () => { idx++; render(); };
    card.querySelector(".skip").onclick = () => {
      localStorage.setItem(TOUR_KEY, "1");
      overlay.remove();
    };
  }
  render();
}
