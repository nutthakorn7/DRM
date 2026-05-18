// Migrate any older sessionStorage values into localStorage on first load so
// admins don't lose their saved IDs when this update lands.
(function migrateSessionStorage() {
  ["drm:tenantId", "drm:adminKey", "drm:adminUserId"].forEach((key) => {
    const sessionValue = sessionStorage.getItem(key);
    if (sessionValue && !localStorage.getItem(key)) {
      localStorage.setItem(key, sessionValue);
    }
  });
})();

const state = {
  tenantId: localStorage.getItem("drm:tenantId") || "",
  adminKey: localStorage.getItem("drm:adminKey") || "",
  adminUserId: localStorage.getItem("drm:adminUserId") || ""
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
  localStorage.setItem("drm:tenantId", state.tenantId);
  localStorage.setItem("drm:adminKey", state.adminKey);
  localStorage.setItem("drm:adminUserId", state.adminUserId);
  setStatus("Session saved (persists across browser sessions)", "ok");
});

// Forget-session helper: clear local credentials without nuking other admin
// state (which the user may have spent time building).
const forgetSessionBtn = document.querySelector("#forgetSession");
if (forgetSessionBtn) {
  forgetSessionBtn.addEventListener("click", () => {
    state.tenantId = "";
    state.adminKey = "";
    state.adminUserId = "";
    ["drm:tenantId", "drm:adminKey", "drm:adminUserId"].forEach((k) => localStorage.removeItem(k));
    tenantIdInput.value = "";
    adminKeyInput.value = "";
    adminUserIdInput.value = "";
    setStatus("Session cleared from this browser", "ok");
  });
}

// Getting started checklist — guides first-time visitors through the
// minimum 5 setup steps and auto-checks each one as it gets done.
(function initGettingStarted() {
  const DISMISS_KEY = "drm:gettingStartedDismissed";
  const card = document.querySelector("#gettingStarted");
  if (!card) return;
  if (localStorage.getItem(DISMISS_KEY) === "1") {
    card.hidden = true;
    return;
  }

  function randomHex(byteCount) {
    const bytes = new Uint8Array(byteCount);
    crypto.getRandomValues(bytes);
    return Array.from(bytes).map(b => b.toString(16).padStart(2, "0")).join("");
  }

  function randomGuid() {
    if (crypto.randomUUID) return crypto.randomUUID();
    const hex = randomHex(16);
    return `${hex.slice(0,8)}-${hex.slice(8,12)}-4${hex.slice(13,16)}-8${hex.slice(17,20)}-${hex.slice(20,32)}`;
  }

  document.querySelector("#generateTenantId")?.addEventListener("click", () => {
    const id = randomGuid();
    tenantIdInput.value = id;
    tenantIdInput.dispatchEvent(new Event("change", { bubbles: true }));
    setStatus(`Generated Tenant ID — copy it somewhere safe.`, "ok");
    tenantIdInput.focus();
    refresh();
  });

  document.querySelector("#generateAdminKey")?.addEventListener("click", async () => {
    const key = randomHex(32);
    adminKeyInput.value = key;
    adminKeyInput.dispatchEvent(new Event("change", { bubbles: true }));
    try {
      await navigator.clipboard.writeText(key);
      setStatus(`Generated shared key (also copied to clipboard). Prefer per-admin tokens from the Access control tab.`, "ok");
    } catch {
      setStatus(`Generated shared key. Prefer per-admin tokens from the Access control tab.`, "ok");
    }
    refresh();
  });

  document.querySelectorAll("[data-jump-tab]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const tab = btn.dataset.jumpTab;
      const panelId = btn.dataset.jumpPanel;
      if (window.__drmSetActiveTab && tab) {
        window.__drmSetActiveTab(tab);
      }
      if (tab && panelId && window.__drmSetActiveSubtab) {
        window.__drmSetActiveSubtab(tab, panelId);
      }
      if (panelId) {
        requestAnimationFrame(() => {
          document.getElementById(panelId)?.scrollIntoView({ behavior: "smooth", block: "start" });
        });
      }
    });
  });

  document.querySelector("#dismissGettingStarted")?.addEventListener("click", () => {
    localStorage.setItem(DISMISS_KEY, "1");
    card.hidden = true;
    setStatus("Getting started hidden — clear localStorage to bring it back.", "ok");
  });

  function refresh() {
    const tenant = tenantIdInput.value.trim();
    const adminKey = adminKeyInput.value.trim();
    const hasTenant = tenant && tenant !== "00000000-0000-0000-0000-000000000000";
    const hasKey = adminKey.length >= 8;
    const hasUser = localStorage.getItem("drm:onboarded:user") === "1";
    const hasPolicy = localStorage.getItem("drm:onboarded:policy") === "1";
    const hasProtect = localStorage.getItem("drm:onboarded:protect") === "1";

    setStep("tenant", hasTenant);
    setStep("adminKey", hasKey);
    setStep("user", hasUser);
    setStep("policy", hasPolicy);
    setStep("protect", hasProtect);

    const allDone = hasTenant && hasKey && hasUser && hasPolicy && hasProtect;
    card.classList.toggle("is-complete", allDone);
  }

  function setStep(name, done) {
    const item = card.querySelector(`[data-step="${name}"]`);
    if (item) item.classList.toggle("is-done", !!done);
  }

  [tenantIdInput, adminKeyInput].forEach(input =>
    input?.addEventListener("input", refresh));

  // Watch for completion signals dispatched elsewhere in the app.
  window.addEventListener("drm:onboarded", (e) => {
    if (e.detail?.step) {
      localStorage.setItem(`drm:onboarded:${e.detail.step}`, "1");
      refresh();
    }
  });

  refresh();
})();

// Tab navigation: 5 tabs (overview, identity, policy, files, integrations)
// collapsing the 19-panel /admin/ console into one tab at a time.
(function initTabs() {
  const VALID_TABS = new Set(["overview", "identity", "policy", "files", "integrations"]);
  const TAB_KEY = "drm:adminActiveTab";

  function readDesiredTab() {
    const hash = (location.hash || "").replace(/^#/, "");
    if (hash.startsWith("tab-")) {
      const candidate = hash.slice(4);
      if (VALID_TABS.has(candidate)) return candidate;
    }
    const stored = localStorage.getItem(TAB_KEY);
    return VALID_TABS.has(stored) ? stored : "overview";
  }

  function panelLabel(panel) {
    const h3 = panel.querySelector("h3");
    if (h3) {
      let raw = h3.cloneNode(true);
      raw.querySelectorAll("span.help-pill, .badge, .info-pill").forEach((n) => n.remove());
      let text = raw.textContent.trim().split(/[—–·]/)[0].trim();
      // Cap at ~22 chars to keep pills compact
      if (text.length > 22) text = text.slice(0, 20).trim() + "…";
      return text || panel.id;
    }
    return panel.id;
  }

  function renderSubnav(tab) {
    const subnav = document.getElementById("subTabNav");
    if (!subnav) return;
    subnav.innerHTML = "";
    if (tab === "all") return;
    const panels = [...document.querySelectorAll(`section.panel[data-tab="${tab}"]`)];
    if (panels.length <= 1) {
      panels.forEach((p) => p.classList.remove("subtab-hidden"));
      return;
    }
    // Bump the Getting Started panel to first if present (matches its CSS order: -10)
    panels.sort((a, b) => {
      if (a.id === "gettingStarted") return -1;
      if (b.id === "gettingStarted") return 1;
      return 0;
    });
    const subKey = `drm:adminActiveSubtab:${tab}`;
    const stored = localStorage.getItem(subKey);
    const initial = panels.find((p) => p.id === stored)?.id || panels[0].id;

    panels.forEach((panel) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "subtab-link";
      btn.dataset.subtab = panel.id;
      btn.setAttribute("role", "tab");
      btn.textContent = panelLabel(panel);
      btn.addEventListener("click", () => setActiveSubtab(tab, panel.id));
      subnav.appendChild(btn);
    });
    setActiveSubtab(tab, initial, { skipScroll: true });
  }

  function setActiveSubtab(tab, panelId, { skipScroll = false } = {}) {
    const panels = [...document.querySelectorAll(`section.panel[data-tab="${tab}"]`)];
    panels.forEach((p) => p.classList.toggle("subtab-hidden", p.id !== panelId));
    document.querySelectorAll("#subTabNav .subtab-link").forEach((b) => {
      b.setAttribute("aria-selected", b.dataset.subtab === panelId ? "true" : "false");
    });
    localStorage.setItem(`drm:adminActiveSubtab:${tab}`, panelId);
  }

  function setActiveTab(name, { fromHash = false } = {}) {
    if (!VALID_TABS.has(name) && name !== "all") name = "overview";
    document.body.dataset.activeTab = name;
    document.querySelectorAll(".tab-link").forEach((btn) => {
      const isActive = btn.dataset.tabLink === name;
      btn.setAttribute("aria-selected", isActive ? "true" : "false");
    });
    if (name !== "all") localStorage.setItem(TAB_KEY, name);
    if (!fromHash && name !== "all") {
      const newHash = `#tab-${name}`;
      if (location.hash !== newHash) {
        history.replaceState(null, "", newHash);
      }
    }
    if (name === "all") {
      // Search mode: clear sub-nav, reveal everything.
      const subnav = document.getElementById("subTabNav");
      if (subnav) subnav.innerHTML = "";
      document.querySelectorAll("section.panel.subtab-hidden").forEach((p) => p.classList.remove("subtab-hidden"));
    } else {
      renderSubnav(name);
    }
  }

  document.querySelectorAll(".tab-link").forEach((btn) => {
    btn.addEventListener("click", () => setActiveTab(btn.dataset.tabLink));
  });

  // Sidebar anchors like <a href="#users"> point at panel IDs; auto-switch
  // to the owning tab before the browser scrolls to the target so the
  // panel is actually visible.
  document.querySelectorAll('aside.sidebar a[href^="#"], nav a[href^="#"]').forEach((link) => {
    link.addEventListener("click", (event) => {
      const id = link.getAttribute("href").slice(1);
      if (!id) return;
      const target = document.getElementById(id);
      const owningTab = target?.dataset.tab;
      if (owningTab && VALID_TABS.has(owningTab)) {
        setActiveTab(owningTab);
        setActiveSubtab(owningTab, id);
        requestAnimationFrame(() => target.scrollIntoView({ behavior: "smooth", block: "start" }));
        event.preventDefault();
      }
    });
  });

  window.addEventListener("hashchange", () => setActiveTab(readDesiredTab(), { fromHash: true }));
  setActiveTab(readDesiredTab(), { fromHash: true });

  window.__drmSetActiveTab = setActiveTab; // expose for search-bar use
  window.__drmSetActiveSubtab = setActiveSubtab;
})();

(function initGlossary() {
  // Plain-language tooltips for jargon-y panel headings and tab labels.
  // Each entry: panel/tab id → 1-sentence explanation a non-expert can read.
  const PANEL_TIPS = {
    transparent:     "Transparent encryption — protected docs open normally in Office for authorized users; everyone else sees gibberish.",
    containers:      "DRM containers — files wrapped in a portable .drm package for offline distribution and import.",
    "folder-watcher": "Auto-protect: watches a server folder and applies a policy to any file dropped in.",
    siem:            "Stream audit events (opens, prints, denials) to your SIEM or log platform.",
    watermarks:      "Visual labels (user, date, IP) overlaid on protected documents to deter screenshots.",
    simulator:       "Dry-run a policy against a fake user/document to see exactly which actions would be allowed.",
    directory:       "Pull users and groups from Azure AD / Entra ID, LDAP, or Google Workspace.",
    box:             "Connect a Box.com tenant so uploaded files are auto-protected with your default policy.",
    outlook:         "Outlook add-in — protect attachments at send-time without leaving the compose window.",
    notifications:   "Email or Slack alerts when policies are violated, files expire, or access is denied.",
    templates:       "Reusable policy bundles — pick one when protecting a file instead of configuring every rule.",
    devices:         "Per-device trust list. Revoke a stolen laptop and every protected file on it stops opening.",
    audit:           "Append-only log of every protect / open / print / deny event across the tenant.",
    license:         "Your DRM subscription tier, seat count, and renewal date.",
    status:          "Server health and version info — useful when filing a support ticket.",
  };

  const TAB_TIPS = {
    overview:     "At-a-glance dashboard, audit log, license, and the getting-started checklist.",
    identity:     "Users, groups, trusted devices, and directory connectors.",
    policy:       "Policy templates, watermark designs, and the policy simulator.",
    files:        "Protected files, transparent-encryption settings, and DRM containers.",
    integrations: "Outside systems: Box, Outlook, SIEM, folder watcher, and notifications.",
  };

  Object.entries(PANEL_TIPS).forEach(([id, tip]) => {
    const panel = document.getElementById(id);
    if (!panel) return;
    const h3 = panel.querySelector("h3");
    if (!h3 || h3.querySelector(".info-pill")) return;
    const pill = document.createElement("span");
    pill.className = "info-pill";
    pill.setAttribute("title", tip);
    pill.setAttribute("aria-label", tip);
    pill.textContent = "i";
    h3.appendChild(pill);
  });

  Object.entries(TAB_TIPS).forEach(([id, tip]) => {
    const btn = document.querySelector(`.tab-link[data-tab-link="${id}"]`);
    if (btn) btn.setAttribute("title", tip);
  });
})();

(function initWelcomeScreen() {
  const BOOTSTRAP_KEY = "drm:bootstrapped";
  const screen = document.getElementById("welcomeScreen");
  if (!screen) return;

  const tenantInput  = document.getElementById("tenantId");
  const adminInput   = document.getElementById("adminKey");
  const userInput    = document.getElementById("adminUserId");

  const hasStoredTenant = !!(tenantInput && tenantInput.value && tenantInput.value.trim());
  const bootstrapped    = localStorage.getItem(BOOTSTRAP_KEY) === "1";
  if (hasStoredTenant || bootstrapped) {
    screen.hidden = true;
    return;
  }
  screen.hidden = false;

  function uuid() {
    if (crypto && typeof crypto.randomUUID === "function") return crypto.randomUUID();
    const b = new Uint8Array(16); crypto.getRandomValues(b);
    b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
    const h = [...b].map(x => x.toString(16).padStart(2, "0"));
    return `${h.slice(0,4).join("")}-${h.slice(4,6).join("")}-${h.slice(6,8).join("")}-${h.slice(8,10).join("")}-${h.slice(10,16).join("")}`;
  }
  function adminKey() {
    const b = new Uint8Array(24); crypto.getRandomValues(b);
    return btoa(String.fromCharCode(...b)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }
  function fire(el) { el && el.dispatchEvent(new Event("change", { bubbles: true })); }

  function dismiss() {
    localStorage.setItem(BOOTSTRAP_KEY, "1");
    screen.style.transition = "opacity 200ms ease";
    screen.style.opacity = "0";
    setTimeout(() => { screen.hidden = true; screen.style.opacity = ""; }, 220);
  }

  const startBtn = document.getElementById("welcomeStart");
  if (startBtn) startBtn.addEventListener("click", () => {
    if (tenantInput && !tenantInput.value) { tenantInput.value = uuid(); fire(tenantInput); }
    if (adminInput  && !adminInput.value)  { adminInput.value  = adminKey(); fire(adminInput); }
    if (userInput   && !userInput.value)   { userInput.value   = uuid(); fire(userInput); }
    dismiss();
    setTimeout(() => {
      const gs = document.getElementById("gettingStarted");
      if (gs) gs.scrollIntoView({ behavior: "smooth", block: "start" });
    }, 260);
  });

  const skipBtn = document.getElementById("welcomeSkip");
  if (skipBtn) skipBtn.addEventListener("click", dismiss);
})();

(function initSettingsDrawer() {
  const trigger  = document.getElementById("settingsTrigger");
  const drawer   = document.getElementById("settingsDrawer");
  const backdrop = document.getElementById("settingsBackdrop");
  const closeBtn = document.getElementById("settingsClose");
  if (!trigger || !drawer || !backdrop) return;

  function setOpen(open) {
    drawer.dataset.open   = open ? "true" : "false";
    backdrop.dataset.open = open ? "true" : "false";
    trigger.setAttribute("aria-expanded", open ? "true" : "false");
    document.body.style.overflow = open ? "hidden" : "";
  }

  trigger.addEventListener("click", () => {
    const open = drawer.dataset.open !== "true";
    setOpen(open);
  });
  closeBtn?.addEventListener("click", () => setOpen(false));
  backdrop.addEventListener("click", () => setOpen(false));
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && drawer.dataset.open === "true") setOpen(false);
  });

  // Initialize closed but elements present in DOM so transitions work on first open.
  setOpen(false);
})();

(function initRailToggle() {
  const KEY = "drm:railCollapsed";
  const MOBILE_QUERY = window.matchMedia("(max-width: 820px)");
  const workspace = document.querySelector(".workspace");
  const toggle = document.getElementById("railToggle");
  if (!workspace || !toggle) return;

  function apply(collapsed) {
    workspace.dataset.railCollapsed = collapsed ? "true" : "false";
    toggle.setAttribute("aria-expanded", collapsed ? "false" : "true");
    toggle.setAttribute("aria-label", collapsed ? "Expand sidebar" : "Collapse sidebar");
    toggle.title = collapsed ? "Expand sidebar" : "Collapse sidebar";
    // On mobile, the workspace grid widens when expanded so the labels can fit.
    if (MOBILE_QUERY.matches) {
      workspace.dataset.railExpandedOnMobile = collapsed ? "false" : "true";
    } else {
      delete workspace.dataset.railExpandedOnMobile;
    }
  }

  // On mobile, default to collapsed if the user hasn't explicitly chosen.
  const stored = localStorage.getItem(KEY);
  const initial = stored === null
    ? MOBILE_QUERY.matches
    : stored === "1";
  apply(initial);

  toggle.addEventListener("click", () => {
    const next = workspace.dataset.railCollapsed !== "true";
    apply(next);
    localStorage.setItem(KEY, next ? "1" : "0");
  });

  // If viewport crosses the 820 boundary and the user never picked, re-apply
  // the appropriate default so the sidebar isn't stuck in a wrong state.
  MOBILE_QUERY.addEventListener?.("change", () => {
    if (localStorage.getItem(KEY) === null) apply(MOBILE_QUERY.matches);
  });
})();

// Global search: hit `/` anywhere to jump to the panel-filter input.
const globalSearch = document.querySelector("#globalSearch");
if (globalSearch) {
  document.addEventListener("keydown", (event) => {
    const inField = ["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement?.tagName ?? "");
    if (event.key === "/" && !inField) {
      event.preventDefault();
      globalSearch.focus();
      globalSearch.select();
    }
    if (event.key === "Escape" && document.activeElement === globalSearch) {
      globalSearch.value = "";
      globalSearch.dispatchEvent(new Event("input"));
      globalSearch.blur();
    }
  });
  globalSearch.addEventListener("input", () => {
    const q = globalSearch.value.trim().toLowerCase();
    if (q) {
      // While searching, show panels from every tab — search trumps tab.
      document.body.dataset.activeTab = "all";
    } else if (window.__drmSetActiveTab) {
      // Empty query — restore the user's last tab choice.
      const stored = localStorage.getItem("drm:adminActiveTab") || "overview";
      window.__drmSetActiveTab(stored, { fromHash: true });
    }
    document.querySelectorAll("section.panel").forEach((panel) => {
      if (!q) { panel.hidden = false; return; }
      const haystack = (panel.querySelector("h3, h4")?.textContent ?? "").toLowerCase() +
                       " " + (panel.querySelector(".eyebrow")?.textContent ?? "").toLowerCase() +
                       " " + (panel.id || "");
      panel.hidden = !haystack.includes(q);
    });
  });
}

// Glossary tooltips: decorate any element whose text content contains a
// term defined in /admin/glossary.<locale>.json. Decoration is idempotent
// and falls back to English when the locale-specific file is missing.
(async function initGlossary() {
  const locale = (localStorage.getItem("drm:locale")
                  ?? navigator.language?.slice(0, 2)
                  ?? "en").toLowerCase();
  const candidates = locale === "en" ? ["en"] : [locale, "en"];

  let glossary = {};
  for (const lang of candidates) {
    const paths = [
      `/admin/glossary.${lang}.json`,
      // Backwards-compat: the original release shipped a single
      // glossary.json file. Honour it as the EN baseline.
      lang === "en" ? "/admin/glossary.json" : null
    ].filter(Boolean);
    for (const path of paths) {
      try {
        const resp = await fetch(path);
        if (!resp.ok) continue;
        const localeMap = await resp.json();
        glossary = { ...localeMap, ...glossary }; // EN keys win when locale-specific is missing
      } catch { /* skip */ }
    }
  }

  function decorate() {
    if (!Object.keys(glossary).length) return;
    document.querySelectorAll("label, summary, .eyebrow, code").forEach((el) => {
      if (el.dataset.glossaryDecorated) return;
      const text = el.textContent;
      for (const [term, def] of Object.entries(glossary)) {
        if (text && text.includes(term)) {
          const icon = document.createElement("span");
          icon.className = "help-icon";
          icon.setAttribute("data-help", def);
          icon.textContent = "?";
          icon.title = def;
          el.appendChild(icon);
          el.dataset.glossaryDecorated = "1";
          break;
        }
      }
    });
  }
  decorate();
  // Re-decorate when panels expand or new rows are rendered.
  const observer = new MutationObserver(() => decorate());
  observer.observe(document.body, { childList: true, subtree: true });
})();

document.querySelector("#refreshUsers").addEventListener("click", () => {
  refreshUsers();
});

document.querySelector("#checkHealth").addEventListener("click", async () => {
  const response = await fetch("/healthz");
  healthOutput.textContent = JSON.stringify(await response.json(), null, 2);
});

// Unified status dashboard — one card, 8 traffic-light tiles.
document.querySelector("#refreshDashboard")?.addEventListener("click", refreshStatusDashboard);

document.querySelectorAll(".status-tile").forEach((tile) => {
  tile.addEventListener("click", () => {
    const jump = tile.getAttribute("data-jump");
    if (jump) {
      const target = document.getElementById(jump);
      if (target) target.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  });
});

async function refreshStatusDashboard() {
  const tenantId = tenantIdInput.value.trim();

  setStatus("Polling subsystems…", "ok");

  async function probe(subsystem, request) {
    const tile = document.querySelector(`.status-tile[data-subsystem="${subsystem}"]`);
    if (!tile) return;
    tile.classList.remove("is-ok", "is-warn", "is-err");
    try {
      const result = await request();
      tile.classList.add(result.level);
      tile.querySelector(".detail").textContent = result.detail;
    } catch (err) {
      tile.classList.add("is-err");
      tile.querySelector(".detail").textContent = err?.message ?? "error";
    }
  }

  await Promise.all([
    probe("health", async () => {
      const r = await fetch("/healthz");
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      return { level: "is-ok", detail: "healthy" };
    }),
    probe("directory", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/admin/directory/config?tenantId=${encodeURIComponent(tenantId)}`, {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (r.status === 404) return { level: "is-warn", detail: "not configured" };
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const c = await r.json();
      return { level: c.lastSyncStatus === "ok" ? "is-ok" : "is-warn", detail: c.lastSyncStatus ?? "unknown" };
    }),
    probe("box", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/admin/box/config?tenantId=${encodeURIComponent(tenantId)}`, {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (r.status === 404) return { level: "is-warn", detail: "not configured" };
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const c = await r.json();
      return { level: c.enabled ? "is-ok" : "is-warn", detail: c.enabled ? `${c.lastWebhookEventCount} events` : "disabled" };
    }),
    probe("outlook", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/admin/outlook/config?tenantId=${encodeURIComponent(tenantId)}`, {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (r.status === 404) return { level: "is-warn", detail: "not configured" };
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const c = await r.json();
      return { level: c.enabled ? "is-ok" : "is-warn", detail: c.enabled ? `${c.lifetimeProtectedCount} protected` : "disabled" };
    }),
    probe("folder-watcher", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/admin/folder-watcher/config?tenantId=${encodeURIComponent(tenantId)}`, {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (r.status === 404) return { level: "is-warn", detail: "not configured" };
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const c = await r.json();
      const level = c.enabled && c.lastReportStatus === "ok" ? "is-ok"
                    : c.enabled ? "is-warn" : "is-warn";
      return { level, detail: c.enabled ? `${c.lastFilesProtected ?? 0} protected` : "disabled" };
    }),
    probe("license", async () => {
      const r = await fetch("/api/admin/license", {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const c = await r.json();
      return { level: "is-ok", detail: `${c.enabledTiers.length} tier${c.enabledTiers.length === 1 ? "" : "s"} · ${c.paidEncrypterCount} paid` };
    }),
    probe("audit", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/audit?tenantId=${encodeURIComponent(tenantId)}&limit=1`, {
        headers: { "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const list = await r.json();
      return { level: "is-ok", detail: list.length > 0 ? "events flowing" : "no events yet" };
    }),
    probe("containers", async () => {
      if (!tenantId) return { level: "is-warn", detail: "no tenant" };
      const r = await fetch(`/api/admin/secure-containers?tenantId=${encodeURIComponent(tenantId)}`, {
        headers: { ...adminAuthHeader(adminKeyInput.value), "X-DRM-Tenant-Id": tenantIdInput.value }
      });
      if (!r.ok) return { level: "is-err", detail: `HTTP ${r.status}` };
      const list = await r.json();
      return { level: "is-ok", detail: `${list.length} container${list.length === 1 ? "" : "s"}` };
    })
  ]);

  setStatus("Dashboard refreshed", "ok");
}

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
  window.dispatchEvent(new CustomEvent("drm:onboarded", { detail: { step: "user" } }));
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
  window.dispatchEvent(new CustomEvent("drm:onboarded", { detail: { step: "policy" } }));
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
  showPolicyPushToast(`Watermark template ${templateId.slice(0, 8)}… updated. Active viewer sessions will pick up the new pattern on the next file open.`);
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

// Transparent files
document.querySelector("#refreshTransparentFiles").addEventListener("click", refreshTransparentFiles);
document.querySelector("#registerTransparentForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = {
    tenantId: requireTenantId(),
    fileId: document.querySelector("#transparentFileId").value.trim(),
    ownerUserId: document.querySelector("#transparentOwner").value.trim(),
    originalFileName: document.querySelector("#transparentFileName").value.trim(),
    contentType: document.querySelector("#transparentContentType").value.trim() || null,
    policyTemplateId: null
  };
  await apiFetch("/api/admin/transparent-files", {
    method: "POST",
    body: JSON.stringify(body)
  });
  setStatus("Transparent file registered", "ok");
  event.target.reset();
  await refreshTransparentFiles();
});

async function refreshTransparentFiles() {
  const tenantId = requireTenantId();
  const files = await apiFetch(`/api/admin/transparent-files?tenantId=${encodeURIComponent(tenantId)}`);
  const body = document.querySelector("#transparentFilesBody");
  if (!files.length) {
    body.innerHTML = '<tr><td colspan="6" class="empty">No transparent files registered yet.</td></tr>';
    setStatus("No transparent files", "ok");
    return;
  }
  body.innerHTML = files.map((f) => `
    <tr>
      <td><code>${escapeHtml(f.fileId)}</code></td>
      <td>${escapeHtml(f.originalFileName)}</td>
      <td>${escapeHtml(f.contentType || "")}</td>
      <td><code>${escapeHtml(f.ownerUserId)}</code></td>
      <td>${escapeHtml(formatDate(f.registeredAtUtc))}</td>
      <td><button class="danger" type="button" data-deregister-transparent="${escapeHtml(f.fileId)}">Deregister</button></td>
    </tr>
  `).join("");
  body.querySelectorAll("[data-deregister-transparent]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const fileId = btn.dataset.deregisterTransparent;
      await apiFetch(`/api/admin/transparent-files/${encodeURIComponent(fileId)}?tenantId=${encodeURIComponent(tenantId)}`, {
        method: "DELETE"
      });
      await refreshTransparentFiles();
      setStatus("Transparent file deregistered", "ok");
    });
  });
  setStatus(`${files.length} transparent file${files.length === 1 ? "" : "s"} loaded`, "ok");
}

// Folder watcher
document.querySelector("#folderWatcherForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const pathsText = document.querySelector("#folderWatcherPaths").value;
  const watchedFolders = pathsText
    .split(/\r?\n|,/)
    .map((p) => p.trim())
    .filter(Boolean)
    .map((p) => ({ path: p, policyTemplateId: null }));
  const body = {
    tenantId: requireTenantId(),
    watchedFolders,
    enabled: document.querySelector("#folderWatcherEnabled").checked
  };
  await apiFetch("/api/admin/folder-watcher/config", {
    method: "PUT",
    body: JSON.stringify(body)
  });
  setStatus("Folder watcher config saved", "ok");
  await loadFolderWatcherConfig();
});

document.querySelector("#loadFolderWatcher").addEventListener("click", loadFolderWatcherConfig);

async function loadFolderWatcherConfig() {
  try {
    const config = await apiFetch(`/api/admin/folder-watcher/config?tenantId=${encodeURIComponent(requireTenantId())}`);
    document.querySelector("#folderWatcherEnabled").checked = !!config.enabled;
    document.querySelector("#folderWatcherPaths").value =
      (config.watchedFolders ?? []).map((f) => f.path).join("\n");
    document.querySelector("#folderWatcherStatus").textContent = JSON.stringify({
      enabled: config.enabled,
      hostname: config.hostname,
      lastReportStatus: config.lastReportStatus,
      lastReportAtUtc: config.lastReportAtUtc,
      lastFilesProtected: config.lastFilesProtected,
      updatedAtUtc: config.updatedAtUtc
    }, null, 2);
    setStatus("Folder watcher config loaded", "ok");
  } catch (err) {
    document.querySelector("#folderWatcherStatus").textContent = `No config yet: ${err.message}`;
  }
}

document.querySelector("#refreshFolderWatcherEvents").addEventListener("click", async () => {
  const events = await apiFetch(`/api/admin/folder-watcher/events?tenantId=${encodeURIComponent(requireTenantId())}`);
  const tbody = document.querySelector("#folderWatcherEventsBody");
  if (!events.length) {
    tbody.innerHTML = '<tr><td colspan="7" class="empty">No folder watcher events yet.</td></tr>';
    setStatus("No folder watcher events", "ok");
    return;
  }
  tbody.innerHTML = events.map((e) => `
    <tr>
      <td>${escapeHtml(formatDate(e.occurredAtUtc))}</td>
      <td>${escapeHtml(e.hostname)}</td>
      <td>${escapeHtml(e.folderPath)}</td>
      <td>${escapeHtml(e.fileName)}</td>
      <td>${formatBytes(e.fileSize)}</td>
      <td>${escapeHtml(e.status)}</td>
      <td>${e.fileId ? `<code>${escapeHtml(e.fileId)}</code>` : ""}</td>
    </tr>
  `).join("");
  setStatus(`${events.length} folder watcher event${events.length === 1 ? "" : "s"} loaded`, "ok");
});

// Secure containers
document.querySelector("#refreshSecureContainers").addEventListener("click", refreshSecureContainers);

async function refreshSecureContainers() {
  const tenantId = requireTenantId();
  const items = await apiFetch(`/api/admin/secure-containers?tenantId=${encodeURIComponent(tenantId)}`);
  const body = document.querySelector("#secureContainersBody");
  if (!items.length) {
    body.innerHTML = '<tr><td colspan="7" class="empty">No secure containers yet.</td></tr>';
    setStatus("No secure containers", "ok");
    return;
  }
  body.innerHTML = items.map((c) => `
    <tr>
      <td>${escapeHtml(c.displayName)}</td>
      <td><code>${escapeHtml(c.containerId)}</code></td>
      <td><code>${escapeHtml(c.ownerUserId)}</code></td>
      <td>${c.fileCount}</td>
      <td>${formatBytes(c.totalBytes)}</td>
      <td>${escapeHtml(formatDate(c.createdAtUtc))}</td>
      <td><button class="danger" type="button" data-delete-container="${escapeHtml(c.containerId)}">Delete</button></td>
    </tr>
  `).join("");
  body.querySelectorAll("[data-delete-container]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const cid = btn.dataset.deleteContainer;
      await apiFetch(`/api/admin/secure-containers/${encodeURIComponent(cid)}?tenantId=${encodeURIComponent(tenantId)}`, {
        method: "DELETE"
      });
      await refreshSecureContainers();
      setStatus("Container deleted", "ok");
    });
  });
  setStatus(`${items.length} secure container${items.length === 1 ? "" : "s"} loaded`, "ok");
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
  const templateId = document.querySelector("#applyPolicyTemplateId").value.trim();
  const body = {
    tenantId: requireTenantId(),
    templateId,
    adminUserId: requireAdminUserId()
  };

  await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/apply-policy-template`, {
    method: "POST",
    body: JSON.stringify(body)
  });

  await refreshFiles();
  setStatus("Policy template applied", "ok");
  showPolicyPushToast(`Template ${templateId.slice(0, 8)}… applied. The new policy will be enforced on the next file open — viewers do not need to reload anything.`);
}

function showPolicyPushToast(message) {
  const existing = document.querySelector(".policy-push-toast");
  if (existing) existing.remove();
  const toast = document.createElement("div");
  toast.className = "policy-push-toast";
  toast.textContent = message;
  document.body.appendChild(toast);
  setTimeout(() => toast.remove(), 6000);
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

function adminAuthHeader(credential) {
  return credential.startsWith("drm_admin_")
    ? { "X-DRM-Admin-Token": credential }
    : { "X-DRM-Admin-Key": credential };
}

async function apiFetch(url, options = {}) {
  const adminKey = requireAdminKey();
  // Pass tenant ID via X-DRM-Tenant-Id so the server can cross-check it
  // against whatever the request body carries. Body-side tenantId is still
  // accepted today; the header is the long-term canonical source.
  const tenantHeader = (tenantIdInput?.value || "").trim();
  const response = await fetch(url, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...adminAuthHeader(adminKey),
      ...(tenantHeader ? { "X-DRM-Tenant-Id": tenantHeader } : {}),
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
  const tenantHeader = (tenantIdInput?.value || "").trim();
  const response = await fetch(url, {
    ...options,
    headers: {
      ...adminAuthHeader(adminKey),
      ...(tenantHeader ? { "X-DRM-Tenant-Id": tenantHeader } : {}),
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    setStatus(`Request failed: ${response.status}`, "error");
    throw new Error(`Request failed with HTTP ${response.status}`);
  }

  return response.blob();
}

// Empty-state helper for table bodies. Renders a centered card with icon,
// title, hint, and optional CTA inside a full-width <td>. Use whenever a list
// is empty so new users see something better than "No items.".
function emptyStateRow(colspan, opts) {
  const cta = opts.cta
    ? `<button type="button" class="primary"${opts.ctaData ? ' ' + opts.ctaData : ''} style="margin-top:10px">${opts.cta}</button>`
    : "";
  return `<tr><td colspan="${colspan}" style="padding:0">
    <div class="empty-state">
      <div class="empty-icon" aria-hidden="true">${opts.icon}</div>
      <div class="empty-title">${opts.title}</div>
      <div class="empty-hint">${opts.hint}</div>
      ${cta}
    </div>
  </td></tr>`;
}

function renderUsers(users) {
  if (!users.length) {
    usersBody.innerHTML = emptyStateRow(3, {
      icon: "👥",
      title: "No users yet",
      hint: "Add your first user with the form above. Users must exist before files can be protected for them.",
    });
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
    groupMembersBody.innerHTML = emptyStateRow(2, {
      icon: "👥",
      title: "No members in this group",
      hint: "Use the Add member form above to bring users into this group.",
    });
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
    devicesBody.innerHTML = emptyStateRow(8, {
      icon: "💻",
      title: "No agent devices registered",
      hint: "Devices appear here once the DRM agent checks in. Install the agent and run it once to see it listed.",
    });
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
    policyTemplatesBody.innerHTML = emptyStateRow(6, {
      icon: "📋",
      title: "No policy templates yet",
      hint: "Templates bundle permissions, watermark, and offline-lease into a reusable preset. Create one above so users can apply it when protecting files.",
    });
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
    filesBody.innerHTML = emptyStateRow(7, {
      icon: "📄",
      title: "No protected files yet",
      hint: "Files appear here once a user protects a document via the Send page, the DRM agent, or the API.",
    });
    return;
  }

  const tenantId = tenantIdInput.value.trim();
  filesBody.innerHTML = files.map((file) => `
    <tr>
      <td><code>${escapeHtml(file.fileId)}</code></td>
      <td><code>${escapeHtml(file.ownerUserId)}</code></td>
      <td>${escapeHtml(file.contentType)}</td>
      <td>${escapeHtml(file.permissions)}</td>
      <td>${renderRevokedBadge(file.revoked)}</td>
      <td>${escapeHtml(formatDate(file.expiresAtUtc))}</td>
      <td>
        <a class="zip-link" href="/api/admin/files/${encodeURIComponent(file.fileId)}/convert/zip?tenantId=${encodeURIComponent(tenantId)}" download="drm-${escapeHtml(file.fileId)}.zip">ZIP</a>
        ${file.revoked ? "" : ` <button class="danger" type="button" data-revoke-file-id="${escapeHtml(file.fileId)}">Revoke</button>`}
      </td>
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

// Admin Identity Management
document.querySelector("#refreshAdminIdentity").addEventListener("click", () => {
  loadWhoAmI();
  refreshRoles().then(() => refreshAdmins());
});

document.querySelector("#refreshRoles").addEventListener("click", () => {
  refreshRoles();
});

document.querySelector("#createAdminForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const email = document.querySelector("#newAdminEmail").value.trim();
  const displayName = document.querySelector("#newAdminDisplayName").value.trim();
  const roleId = document.querySelector("#newAdminRoleId").value.trim();
  const tokenLabel = document.querySelector("#newAdminTokenLabel").value.trim();
  const tenantScopeRaw = document.querySelector("#newAdminTenantScope").value.trim();

  if (!roleId) {
    setStatus("Select a role before creating an admin", "error");
    return;
  }

  const body = {
    email,
    displayName: displayName || null,
    roleId,
    tokenLabel: tokenLabel || null,
    tokenExpiresAtUtc: null,
    tenantScope: tenantScopeRaw || null
  };

  const created = await apiFetch("/api/admin/identity/admins", {
    method: "POST",
    body: JSON.stringify(body)
  });

  const output = document.querySelector("#createAdminOutput");
  output.hidden = false;
  output.textContent = [
    "Admin created — save this token now, it will not be shown again.",
    "",
    `Admin ID : ${created.adminUserId}`,
    `Email    : ${created.email}`,
    `Role     : ${created.roleName}`,
    `Scope    : ${created.tenantScope || "Global"}`,
    `Token ID : ${created.tokenId}`,
    `Token    : ${created.token}`,
    created.tokenExpiresAtUtc
      ? `Expires  : ${new Date(created.tokenExpiresAtUtc).toLocaleString()}`
      : "Expires  : never"
  ].join("\n");

  event.target.reset();
  await refreshAdmins();
  setStatus(`Admin ${email} created`, "ok");
});

async function loadWhoAmI() {
  try {
    const identity = await apiFetch("/api/admin/identity/whoami");
    const output = document.querySelector("#whoamiOutput");
    if (output) output.textContent = JSON.stringify(identity, null, 2);
    setSharedKeyBanner(identity.sharedKeyFallback === true);
    setStatus("Session loaded", "ok");
  } catch (err) {
    const output = document.querySelector("#whoamiOutput");
    if (output) output.textContent = `Not authenticated: ${err.message}`;
  }
}

function setSharedKeyBanner(visible) {
  let banner = document.querySelector("#sharedKeyDeprecationBanner");
  if (visible && !banner) {
    banner = document.createElement("div");
    banner.id = "sharedKeyDeprecationBanner";
    banner.style.cssText =
      "background:#7c2d12;color:#fff;padding:10px 16px;font-size:13px;display:flex;align-items:center;gap:10px;";
    banner.innerHTML =
      '<strong>Deprecation warning:</strong> This session is authenticated with the shared API key (X-DRM-Admin-Key). ' +
      'Migrate to per-admin tokens (X-DRM-Admin-Token) before upgrading to a future release that removes shared-key support.';
    document.body.insertBefore(banner, document.body.firstChild);
  } else if (!visible && banner) {
    banner.remove();
  }
}

async function refreshRoles() {
  const roles = await apiFetch("/api/admin/identity/roles");
  const rolesBody = document.querySelector("#rolesBody");
  if (!roles.length) {
    rolesBody.innerHTML = '<tr><td colspan="4" class="empty">No roles found.</td></tr>';
    setStatus("No roles", "ok");
    return;
  }
  rolesBody.innerHTML = roles.map((r) => `
    <tr>
      <td>${escapeHtml(r.name)}</td>
      <td><code>${escapeHtml(r.id)}</code></td>
      <td><small>${escapeHtml(r.permissions.join(", "))}</small></td>
      <td>${r.isSystem ? '<span class="badge">System</span>' : ""}</td>
    </tr>
  `).join("");

  const select = document.querySelector("#newAdminRoleId");
  if (select) {
    const currentVal = select.value;
    select.innerHTML = '<option value="">Select role…</option>' +
      roles.map((r) => `<option value="${escapeHtml(r.id)}">${escapeHtml(r.name)}</option>`).join("");
    if (currentVal) select.value = currentVal;
  }

  setStatus(`${roles.length} role${roles.length === 1 ? "" : "s"} loaded`, "ok");
}

async function refreshAdmins() {
  const admins = await apiFetch("/api/admin/identity/admins");
  const adminsBody = document.querySelector("#adminsBody");
  if (!admins.length) {
    adminsBody.innerHTML = '<tr><td colspan="8" class="empty">No admin users found.</td></tr>';
    setStatus("No admins", "ok");
    return;
  }
  adminsBody.innerHTML = admins.map((a) => `
    <tr>
      <td>${escapeHtml(a.email)}</td>
      <td>${escapeHtml(a.displayName || "")}</td>
      <td>${escapeHtml(a.roleName)}</td>
      <td>${a.tenantScope ? `<code title="${escapeHtml(a.tenantScope)}">${escapeHtml(a.tenantScope.slice(0, 8))}…</code>` : '<span class="badge">Global</span>'}</td>
      <td>${renderEnabledBadge(!a.disabled)}</td>
      <td>${a.activeTokenCount}</td>
      <td>${escapeHtml(formatDate(a.lastUsedAtUtc) || "—")}</td>
      <td>
        ${a.disabled
          ? `<button type="button" data-enable-admin="${escapeHtml(a.id)}">Enable</button>`
          : `<button type="button" class="danger" data-disable-admin="${escapeHtml(a.id)}">Disable</button>`}
        <button type="button" data-rotate-token-admin="${escapeHtml(a.id)}">Rotate token</button>
      </td>
    </tr>
  `).join("");

  adminsBody.querySelectorAll("[data-disable-admin]").forEach((btn) => {
    btn.addEventListener("click", () => disableAdminUser(btn.dataset.disableAdmin));
  });
  adminsBody.querySelectorAll("[data-enable-admin]").forEach((btn) => {
    btn.addEventListener("click", () => enableAdminUser(btn.dataset.enableAdmin));
  });
  adminsBody.querySelectorAll("[data-rotate-token-admin]").forEach((btn) => {
    btn.addEventListener("click", () => rotateAdminToken(btn.dataset.rotateTokenAdmin));
  });

  setStatus(`${admins.length} admin${admins.length === 1 ? "" : "s"} loaded`, "ok");
}

async function disableAdminUser(adminId) {
  await apiFetch(`/api/admin/identity/admins/${encodeURIComponent(adminId)}/disable`, {
    method: "POST"
  });
  await refreshAdmins();
  setStatus("Admin disabled", "ok");
}

async function enableAdminUser(adminId) {
  await apiFetch(`/api/admin/identity/admins/${encodeURIComponent(adminId)}/enable`, {
    method: "POST"
  });
  await refreshAdmins();
  setStatus("Admin enabled", "ok");
}

async function rotateAdminToken(adminId) {
  const result = await apiFetch(`/api/admin/identity/admins/${encodeURIComponent(adminId)}/rotate-token`, {
    method: "POST",
    body: JSON.stringify({})
  });
  const output = document.querySelector("#rotateTokenOutput");
  if (output) {
    output.hidden = false;
    output.textContent = [
      "New token issued — save it now, it will not be shown again.",
      "",
      `Token ID : ${result.tokenId}`,
      `Token    : ${result.token}`,
      result.expiresAtUtc
        ? `Expires  : ${new Date(result.expiresAtUtc).toLocaleString()}`
        : "Expires  : never"
    ].join("\n");
  }
  await refreshAdmins();
  setStatus("Token rotated — copy the new token above", "ok");
}

// ── Tenants (operator-level) ────────────────────────────────────────────────

async function apiFetchGlobal(url, options = {}) {
  const adminKey = requireAdminKey();
  const response = await fetch(url, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...adminAuthHeader(adminKey),
      ...(options.headers || {})
    }
  });
  if (!response.ok) {
    setStatus(`Request failed: ${response.status}`, "error");
    throw new Error(`Request failed with HTTP ${response.status}`);
  }
  return response.status === 204 ? null : response.json();
}

async function refreshTenants() {
  const tbody = document.getElementById("tenantsBody");
  tbody.innerHTML = '<tr><td colspan="7" class="empty">Loading…</td></tr>';
  try {
    const tenants = await apiFetchGlobal("/api/admin/tenants");
    if (!tenants.length) {
      tbody.innerHTML = '<tr><td colspan="7" class="empty">No tenants yet. Create one above.</td></tr>';
      return;
    }
    tbody.innerHTML = tenants.map(t => {
      const statusLabel = t.status === 0
        ? '<span class="badge badge-ok">Active</span>'
        : '<span class="badge badge-error">Suspended</span>';
      const seatLabel = t.maxEncrypters != null ? t.maxEncrypters : '∞';
      const toggleLabel = t.status === 0 ? "Suspend" : "Activate";
      const toggleStatus = t.status === 0 ? 1 : 0;
      const createdDate = new Date(t.createdAtUtc).toLocaleDateString();
      return `<tr>
        <td><code>${esc(t.name)}</code></td>
        <td>${esc(t.displayName)}</td>
        <td>${statusLabel}</td>
        <td>${seatLabel}</td>
        <td><code class="copy-id" title="Click to copy" style="cursor:pointer" onclick="navigator.clipboard.writeText('${esc(t.tenantId)}')">${esc(t.tenantId)}</code></td>
        <td>${createdDate}</td>
        <td>
          <button type="button" class="ghost" onclick="toggleTenantStatus('${esc(t.tenantId)}', ${toggleStatus})">${toggleLabel}</button>
        </td>
      </tr>`;
    }).join("");
    setStatus(`Loaded ${tenants.length} tenant(s)`, "ok");
  } catch (e) {
    tbody.innerHTML = '<tr><td colspan="7" class="empty">Failed to load tenants.</td></tr>';
  }
}

async function toggleTenantStatus(tenantId, newStatus) {
  try {
    await apiFetchGlobal(`/api/admin/tenants/${encodeURIComponent(tenantId)}`, {
      method: "PATCH",
      body: JSON.stringify({ status: newStatus })
    });
    setStatus(newStatus === 1 ? "Tenant suspended" : "Tenant activated", "ok");
    await refreshTenants();
  } catch (e) {
    // error already shown by apiFetchGlobal
  }
}

function esc(s) {
  return String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

document.getElementById("refreshTenants")?.addEventListener("click", refreshTenants);

document.getElementById("createTenantForm")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = document.getElementById("newTenantName").value.trim();
  const displayName = document.getElementById("newTenantDisplayName").value.trim() || undefined;
  const tenantId = document.getElementById("newTenantId").value.trim() || undefined;
  const maxEncryptersRaw = document.getElementById("newTenantMaxEncrypters").value.trim();
  const maxEncrypters = maxEncryptersRaw ? parseInt(maxEncryptersRaw, 10) : undefined;

  const output = document.getElementById("createTenantOutput");
  output.hidden = false;
  output.textContent = "Creating…";

  try {
    const result = await apiFetchGlobal("/api/admin/tenants", {
      method: "POST",
      body: JSON.stringify({ name, displayName, tenantId, maxEncrypters })
    });
    output.textContent = `Created: ${result.tenantId}`;
    document.getElementById("newTenantName").value = "";
    document.getElementById("newTenantDisplayName").value = "";
    document.getElementById("newTenantId").value = "";
    document.getElementById("newTenantMaxEncrypters").value = "";
    setStatus("Tenant created", "ok");
    await refreshTenants();
  } catch (e) {
    output.textContent = "Failed to create tenant.";
  }
});
