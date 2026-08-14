const code = document.getElementById("code");
const status = document.getElementById("status");

document.getElementById("pair").addEventListener("click", async () => {
  const value = code.value.replace(/\D/g, "");
  if (value.length !== 6) return show("请输入 6 位配对码。", false);
  try {
    const response = await fetch("http://127.0.0.1:17860/extension/pair", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code: value })
    });
    if (!response.ok) throw new Error("配对码无效、已使用或已过期。");
    const result = await response.json();
    await chrome.storage.local.set({ localApiToken: result.token });
    show("配对成功。打开 ChatGPT 的使用情况页面后，每 5 分钟自动同步。", true);
    code.value = "";
  } catch (error) {
    show(error.message || "无法连接本地服务。", false);
  }
});

function show(message, ok) {
  status.textContent = message;
  status.className = ok ? "ok" : "error";
}

chrome.storage.local.get([
  "localApiToken",
  "lastScheduledQueryAt",
  "lastScheduledQueryTabCount",
  "lastScheduledQuerySuccessCount"
]).then(value => {
  if (value.localApiToken) show("此浏览器已配对。", true);
  const node = document.getElementById("syncStatus");
  if (!value.lastScheduledQueryAt) return;
  const time = new Date(value.lastScheduledQueryAt).toLocaleString();
  node.textContent = `定时同步：每 5 分钟 · 上次 ${time} · ${value.lastScheduledQuerySuccessCount || 0}/${value.lastScheduledQueryTabCount || 0} 个页面成功`;
});
