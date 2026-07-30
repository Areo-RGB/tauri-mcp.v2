const SOCKET_URL = "ws://127.0.0.1:32145";

async function addLog(level, message) {
  const { logs = [] } = await chrome.storage.local.get("logs");
  logs.push({ timestamp: new Date().toLocaleTimeString(), level, message });
  await chrome.storage.local.set({ logs: logs.slice(-50) });
}

function sendToMcpHub(payload, timeoutMs = 60_000) {
  return new Promise((resolve) => {
    const socket = new WebSocket(SOCKET_URL);
    let settled = false;
    const finish = (response) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      resolve(response);
      socket.close();
    };
    const timeout = setTimeout(() => finish({ success: false, error: "MCPHub did not respond before the request timed out." }), timeoutMs);
    socket.addEventListener("open", () => socket.send(JSON.stringify(payload)));
    socket.addEventListener("message", (event) => {
      try { finish(JSON.parse(event.data)); }
      catch { finish({ success: false, error: "MCPHub returned an invalid response." }); }
    });
    socket.addEventListener("error", () => finish({ success: false, error: "Could not connect to MCPHub on 127.0.0.1:32145." }));
  });
}

chrome.runtime.onMessage.addListener((message, _sender, respond) => {
  if (message?.type === "extension-log") {
    addLog(message.level || "info", message.message || "Extension event").then(() => respond({ success: true }));
    return true;
  }
  if (message?.type === "get-logs") {
    chrome.storage.local.get({ logs: [] }).then(respond);
    return true;
  }
  if (message?.type === "clear-logs") {
    chrome.storage.local.set({ logs: [] }).then(() => respond({ success: true }));
    return true;
  }
  if (!["process-chapters", "fetch-chapters", "ping"].includes(message?.type)) return false;

  const payload = message.type === "ping"
    ? { action: "ping" }
    : { action: message.type === "fetch-chapters" ? "fetch-chapters" : "process", ...message.payload };
  const label = message.type === "ping"
    ? "Checking MCPHub connection"
    : message.type === "fetch-chapters"
      ? "Fetching chapters with yt-dlp"
      : `Sending ${payload.chapters?.length || 0} chapter(s)`;
  addLog("info", label)
    .then(() => sendToMcpHub(payload, message.type === "process-chapters" ? 1_800_000 : 60_000))
    .then(async (response) => {
      const successMessage = message.type === "ping"
        ? "MCPHub is connected"
        : message.type === "fetch-chapters"
          ? `yt-dlp found ${response.result?.chapters?.length || 0} chapter(s)`
          : "Processing completed";
      await addLog(response.success ? "success" : "error", response.success ? successMessage : response.error);
      respond(response);
    });
  return true;
});
