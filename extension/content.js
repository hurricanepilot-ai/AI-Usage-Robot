(() => {
  "use strict";

  let debounceTimer;
  let lastPayload = "";

  async function capture(force = false) {
    const quota = globalThis.AIUsageRobotParser.parseDocument(document, new Date());
    if (!quota) return { ok: false, error: "QUOTA_NOT_VISIBLE" };

    const fingerprint = JSON.stringify({ ...quota, collectedAt: undefined, rawText: undefined });
    if (!force && fingerprint === lastPayload) return { ok: true, unchanged: true };

    try {
      const result = await chrome.runtime.sendMessage({ type: "quotaCapture", quota });
      if (result && result.ok) lastPayload = fingerprint;
      return result;
    } catch {
      return { ok: false, error: "UPLOAD_FAILED" };
    }
  }

  function scheduleCapture() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => capture(false), 750);
  }

  new MutationObserver(scheduleCapture).observe(document.documentElement, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["aria-label", "aria-expanded"]
  });

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (!message || message.type !== "captureNow") return false;
    capture(true).then(sendResponse);
    return true;
  });

  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") capture(true);
  });
  window.addEventListener("focus", () => capture(true));
  window.addEventListener("pageshow", () => capture(true));

  scheduleCapture();
})();
