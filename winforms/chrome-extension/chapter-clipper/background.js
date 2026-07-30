const HOST_NAME = "com.mcphub.chapter_clipper";

async function addLog(level, message) {
  const { logs = [] } = await chrome.storage.local.get("logs");
  logs.push({ timestamp: new Date().toLocaleTimeString(), level, message });
  await chrome.storage.local.set({ logs: logs.slice(-50) });
}

function sendToMcpHub(payload) {
  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(HOST_NAME, payload, (response) => {
      if (chrome.runtime.lastError) {
        resolve({ success: false, error: `Could not connect to MCPHub: ${chrome.runtime.lastError.message}` });
        return;
      }
      resolve(response || { success: false, error: "MCPHub returned no response." });
    });
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
  if (!["process-chapters", "process-upload", "fetch-chapters", "get-playlists", "ping"].includes(message?.type)) return false;

  const payload = message.type === "ping"
    ? { action: "ping" }
    : { action: message.type === "process-chapters" ? "process" : message.type, ...message.payload };
  const label = message.type === "ping" ? "Checking MCPHub connection"
    : message.type === "fetch-chapters" ? "Fetching chapters with yt-dlp"
    : message.type === "get-playlists" ? "Loading YouTube playlists"
    : message.type === "process-upload" ? `Processing and uploading ${payload.chapters?.length || 0} chapter(s)`
    : `Sending ${payload.chapters?.length || 0} chapter(s)`;

  addLog("info", label).then(() => sendToMcpHub(payload)).then(async (response) => {
    const successMessage = message.type === "ping" ? "MCPHub is connected"
      : message.type === "fetch-chapters" ? `yt-dlp found ${response.result?.chapters?.length || 0} chapter(s)`
      : message.type === "get-playlists" ? `Loaded ${response.result?.length || 0} playlist(s)`
      : message.type === "process-upload" ? `Uploaded ${response.result?.uploaded?.clips?.length || 0} clip(s)`
      : "Processing completed";
    await addLog(response.success ? "success" : "error", response.success ? successMessage : response.error);
    respond(response);
  });
  return true;
});
