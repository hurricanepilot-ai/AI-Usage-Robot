const SYNC_ALARM = "chatgpt-quota-sync";
let eventListenerStarted = false;

chrome.runtime.onInstalled.addListener(details => {
  ensurePeriodicSync();
  startEventListener();
  if (details.reason === "install") chrome.runtime.openOptionsPage();
});

chrome.runtime.onStartup.addListener(() => {
  ensurePeriodicSync();
  startEventListener();
});
chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === SYNC_ALARM) requestQuotaFromAllTabs();
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== "quotaCapture" || sender.url?.startsWith("https://chatgpt.com/") !== true)
    return false;

  uploadQuota(message.quota)
    .then(result => sendResponse(result))
    .catch(error => sendResponse({ ok: false, error: error.message }));
  return true;
});

function ensurePeriodicSync() {
  chrome.alarms.create(SYNC_ALARM, {
    delayInMinutes: 1,
    periodInMinutes: 5
  });
}

async function requestQuotaFromAllTabs() {
  const tabs = await chrome.tabs.query({ url: "https://chatgpt.com/*" });
  const results = await Promise.allSettled(tabs.map(tab =>
    chrome.tabs.sendMessage(tab.id, { type: "captureNow" })));

  const successCount = results.filter(result =>
    result.status === "fulfilled" && result.value?.ok).length;

  await chrome.storage.local.set({
    lastScheduledQueryAt: new Date().toISOString(),
    lastScheduledQueryTabCount: tabs.length,
    lastScheduledQuerySuccessCount: successCount
  });
}

async function uploadQuota(quota) {
  const { localApiToken } = await chrome.storage.local.get("localApiToken");
  if (!localApiToken) return { ok: false, error: "NOT_PAIRED" };

  try {
    const response = await fetch("http://127.0.0.1:17860/api/chatgpt/quota", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Authorization": `Bearer ${localApiToken}`
      },
      body: JSON.stringify(quota)
    });
    const result = { ok: response.ok, status: response.status, at: new Date().toISOString() };
    await chrome.storage.local.set({ lastCaptureResult: result });
    return result;
  } catch {
    const result = { ok: false, error: "SERVICE_OFFLINE", at: new Date().toISOString() };
    await chrome.storage.local.set({ lastCaptureResult: result });
    return result;
  }
}

ensurePeriodicSync();
startEventListener();

async function startEventListener() {
  if (eventListenerStarted) return;
  eventListenerStarted = true;
  while (eventListenerStarted) {
    const { localApiToken } = await chrome.storage.local.get("localApiToken");
    if (!localApiToken) {
      await delay(3000);
      continue;
    }

    try {
      const response = await fetch("http://127.0.0.1:17860/api/extension/events", {
        headers: { "Authorization": `Bearer ${localApiToken}` },
        cache: "no-store"
      });
      if (!response.ok || !response.body) throw new Error(`EVENT_STREAM_${response.status}`);

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      while (eventListenerStarted) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const messages = buffer.split("\n\n");
        buffer = messages.pop() || "";
        for (const message of messages) {
          if (message.includes("event: sync") && message.includes("data: chatgpt-sync"))
            await requestQuotaFromAllTabs();
        }
      }
    } catch {
      await chrome.storage.local.set({ extensionEventStreamStatus: "reconnecting" });
      await delay(3000);
    }
  }
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}
