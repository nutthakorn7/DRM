(() => {
  let verificationSessionToken = "";
  let previewSessionToken = "";
  let previewWired = false;
  let previewObjectUrl = null;

  const startForm = document.getElementById("verificationStartForm");
  const confirmForm = document.getElementById("verificationConfirmForm");
  const viewerStatus = document.getElementById("viewerStatus");
  const startStep = document.getElementById("startStep");
  const confirmStep = document.getElementById("confirmStep");

  applyQueryPrefill();
  startForm.addEventListener("submit", startVerification);
  confirmForm.addEventListener("submit", confirmVerification);

  async function startVerification(event) {
    event.preventDefault();
    verificationSessionToken = "";

    const payload = {
      tenantId: valueOf("tenantId"),
      accessToken: valueOf("accessToken"),
      guestEmail: valueOf("guestEmail")
    };

    if (!payload.tenantId || !payload.accessToken || !payload.guestEmail) {
      setStatus("Enter tenant ID, access token, and guest email.", "error");
      return;
    }

    setStatus("Sending verification code...", "");
    const response = await postJson("/api/share-links/verification/start", payload);
    if (!response.ok) {
      await renderError(response, "Unable to start verification.");
      return;
    }

    const result = await response.json();
    setValue("verificationId", result.verificationId);
    startStep.classList.add("complete");
    confirmStep.classList.add("active");
    setStatus("Verification code sent. Enter the code to open the viewer session.", "ok");
  }

  async function confirmVerification(event) {
    event.preventDefault();

    const payload = {
      tenantId: valueOf("tenantId"),
      verificationId: valueOf("verificationId"),
      code: valueOf("verificationCode")
    };

    if (!payload.tenantId || !payload.verificationId || !payload.code) {
      setStatus("Enter tenant ID, verification ID, and code.", "error");
      return;
    }

    setStatus("Confirming verification code...", "");
    const response = await postJson("/api/share-links/verification/confirm", payload);
    if (!response.ok) {
      await renderError(response, "Unable to confirm verification.");
      return;
    }

    const result = await response.json();
    verificationSessionToken = result.verificationSessionToken || "";
    confirmStep.classList.add("complete");
    await openViewerSession();
  }

  async function openViewerSession() {
    if (!verificationSessionToken) {
      setStatus("Verification session is missing. Confirm the code again.", "error");
      return;
    }

    setStatus("Opening viewer session...", "");
    const response = await postJson("/api/share-links/viewer/session", {
      tenantId: valueOf("tenantId"),
      verificationSessionToken
    });

    // Keep the (reusable, session-window) token for the in-browser preview's
    // content-key call before clearing the verification handle.
    previewSessionToken = verificationSessionToken;
    verificationSessionToken = "";

    if (!response.ok) {
      await renderError(response, "Unable to open viewer session.");
      return;
    }

    const result = await response.json();
    renderViewerSession(result);
    setStatus("Viewer session ready. Document actions remain disabled for this shell.", "ok");
  }

  async function postJson(url, payload) {
    try {
      return await fetch(url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
      });
    } catch {
      return {
        ok: false,
        status: 0,
        json: async () => ({ reasonCode: "network_error" })
      };
    }
  }

  async function renderError(response, fallbackMessage) {
    if (response.status === 404) {
      setStatus("Share not found or no longer available.", "error");
      return;
    }

    const body = await response.json();
    setStatus(body.reasonCode || fallbackMessage, "error");
  }

  function renderViewerSession(payload) {
    document.querySelector(".preview-details")?.setAttribute("data-has-session", "true");
    document.getElementById("documentTitle").textContent = "Verified — open the attached file";
    document.getElementById("fileIdValue").textContent = payload.fileId || "-";
    document.getElementById("contentTypeValue").textContent = payload.contentType || "-";
    document.getElementById("guestEmailValue").textContent = payload.guestEmail || "-";
    document.getElementById("sessionExpiresValue").textContent = formatDate(payload.sessionExpiresAtUtc);
    document.getElementById("shareExpiresValue").textContent = formatDate(payload.shareLinkExpiresAtUtc);
    document.getElementById("watermarkValue").textContent = payload.watermarkTemplate || "-";
    document.getElementById("previewWatermark").textContent = payload.watermarkTemplate || "Watermark active";

    // Stage 10 — sender hint stays as the default copy because the
    // verifier intentionally doesn't return the sender email to the
    // recipient session (leaking sender metadata would be a privacy
    // regression).

    // Stage 11 R1 — render permission badges from the bitfield-string.
    // The server emits Permissions as the C# flagged-enum ToString
    // output ("View, Print" or "View, Print, ExportOriginal"). Split
    // on comma+optional-whitespace and flip data-state="allowed" on
    // each matching badge. Badges not in the granted list stay at
    // their default data-state="denied".
    const grantedRaw = (payload.permissions || "").trim();
    const granted = new Set(
      grantedRaw ? grantedRaw.split(/\s*,\s*/).filter(Boolean) : []
    );
    document.querySelectorAll(".perm-badge").forEach(badge => {
      const perm = badge.getAttribute("data-perm");
      badge.setAttribute("data-state", granted.has(perm) ? "allowed" : "denied");
    });

    // Increment 2 — offer in-browser preview ONLY for View-only shares
    // (granted is exactly {View}). Matches the server-side content-key gate.
    maybeEnableInBrowserPreview(granted);
  }

  function maybeEnableInBrowserPreview(granted) {
    const section = document.getElementById("inbrowserPreview");
    if (!section) return;
    const viewOnly = granted.size === 1 && granted.has("View");
    section.hidden = !viewOnly;
    if (viewOnly && !previewWired) {
      previewWired = true;
      document.getElementById("drmxFileInput").addEventListener("change", onDrmxSelected);
    }
  }

  async function onDrmxSelected(event) {
    const file = event.target.files && event.target.files[0];
    if (!file) return;
    if (!window.DrmxPreview) {
      setPreviewStatus("Preview module failed to load — use the desktop viewer.", "error");
      return;
    }
    if (!previewSessionToken) {
      setPreviewStatus("Session expired — verify the code again to preview.", "error");
      return;
    }

    setPreviewStatus("Decrypting in your browser…", "");
    let buffer;
    try {
      buffer = await file.arrayBuffer();
    } catch {
      setPreviewStatus("Could not read the selected file.", "error");
      return;
    }

    const response = await postJson("/api/share-links/viewer/content-key", {
      tenantId: valueOf("tenantId"),
      verificationSessionToken: previewSessionToken
    });
    if (!response.ok) {
      await renderPreviewError(response);
      return;
    }
    const payload = await response.json();

    let result;
    try {
      const key = window.DrmxPreview.base64ToBytes(payload.fileKeyBase64);
      result = await window.DrmxPreview.decryptDrmx(buffer, key);
    } catch (err) {
      setPreviewStatus(err.message || "Decryption failed.", "error");
      return;
    }

    const contentType = result.contentType || payload.contentType || "";
    if (!/pdf/i.test(contentType)) {
      setPreviewStatus(
        "In-browser preview supports PDF files only — open this file in the desktop viewer.", "");
      return;
    }

    const blob = new Blob([result.bytes], { type: "application/pdf" });
    document.getElementById("inbrowserWatermark").textContent = payload.watermarkTemplate || "zcrDRM";
    if (previewObjectUrl) URL.revokeObjectURL(previewObjectUrl); // release the prior preview blob
    previewObjectUrl = URL.createObjectURL(blob);
    document.getElementById("previewIframe").src = previewObjectUrl;
    document.getElementById("inbrowserFrame").hidden = false;
    setPreviewStatus("Decrypted in your browser and rendered below.", "ok");
  }

  async function renderPreviewError(response) {
    if (response.status === 404) {
      setPreviewStatus("This share has no previewable content here — use the desktop viewer.", "error");
      return;
    }
    const body = await response.json().catch(() => ({}));
    setPreviewStatus(body.reasonCode || "Could not fetch the preview key.", "error");
  }

  function setPreviewStatus(message, tone) {
    const el = document.getElementById("previewStatus");
    if (!el) return;
    el.textContent = message;
    el.className = tone ? `status ${tone}` : "status";
  }

  function valueOf(id) {
    return document.getElementById(id).value.trim();
  }

  function setValue(id, value) {
    document.getElementById(id).value = value || "";
  }

  function setStatus(message, tone) {
    viewerStatus.textContent = message;
    viewerStatus.className = tone ? `status ${tone}` : "status";
  }

  function applyQueryPrefill() {
    const query = new URLSearchParams(window.location.search);
    const tenantId = (query.get("tenantId") || "").trim();
    // Accept either `accessToken` (full URL builder) or `token` (legacy
    // short URL) so recipients of either link shape are pre-filled.
    const accessToken = (query.get("accessToken") || query.get("token") || "").trim();
    const guestEmail = (query.get("guestEmail") || "").trim();

    if (tenantId) setValue("tenantId", tenantId);
    if (accessToken) setValue("accessToken", accessToken);
    if (guestEmail) setValue("guestEmail", guestEmail);

    // If tenant + token both came from the URL, collapse the advanced block
    // and show a small "loaded" badge so the recipient sees a tight,
    // one-decision form: "Send verification code".
    const advanced = document.getElementById("shareDetailsAdvanced");
    const badge = document.getElementById("prefillBadge");
    if (tenantId && accessToken && advanced) {
      advanced.open = false;
      if (badge) badge.hidden = false;
      setStatus(
        guestEmail
          ? `Share details loaded for ${guestEmail}. Click Send verification code to continue.`
          : "Share details loaded. Confirm your email and send verification code.",
        ""
      );
      return;
    }

    if (tenantId || accessToken || guestEmail) {
      // Partial prefill — keep the advanced block expanded so the recipient
      // can spot what's missing.
      if (advanced) advanced.open = true;
      setStatus("Share details partially loaded. Fill in any missing fields and send verification code.", "");
    }
  }

  function formatDate(value) {
    if (!value) {
      return "-";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleString();
  }
})();
