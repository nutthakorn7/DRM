(() => {
  let verificationSessionToken = "";

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
    document.getElementById("documentTitle").textContent = "Verified metadata session";
    document.getElementById("fileIdValue").textContent = payload.fileId || "-";
    document.getElementById("contentTypeValue").textContent = payload.contentType || "-";
    document.getElementById("guestEmailValue").textContent = payload.guestEmail || "-";
    document.getElementById("sessionExpiresValue").textContent = formatDate(payload.sessionExpiresAtUtc);
    document.getElementById("shareExpiresValue").textContent = formatDate(payload.shareLinkExpiresAtUtc);
    document.getElementById("watermarkValue").textContent = payload.watermarkTemplate || "-";
    document.getElementById("previewWatermark").textContent = payload.watermarkTemplate || "Watermark active";
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
    const accessToken = (query.get("accessToken") || "").trim();
    const guestEmail = (query.get("guestEmail") || "").trim();

    if (tenantId) {
      setValue("tenantId", tenantId);
    }

    if (accessToken) {
      setValue("accessToken", accessToken);
    }

    if (guestEmail) {
      setValue("guestEmail", guestEmail);
    }

    if (tenantId || accessToken || guestEmail) {
      setStatus("Share details loaded from link. Send verification code to continue.", "");
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
