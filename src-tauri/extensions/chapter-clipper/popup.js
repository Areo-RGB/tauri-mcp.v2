const statusNode = document.querySelector("#status");
const logsNode = document.querySelector("#logs");

async function renderLogs() {
  const { logs = [] } = await chrome.runtime.sendMessage({ type: "get-logs" });
  logsNode.replaceChildren();
  if (!logs.length) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No activity yet";
    logsNode.append(empty);
    return;
  }
  for (const entry of [...logs].reverse()) {
    const item = document.createElement("li");
    const time = document.createElement("time"); time.textContent = entry.timestamp;
    const level = document.createElement("span"); level.className = `level ${entry.level}`; level.textContent = entry.level;
    const message = document.createElement("span"); message.textContent = entry.message;
    item.append(time, level, message);
    logsNode.append(item);
  }
}

async function checkConnection() {
  statusNode.className = "";
  statusNode.textContent = "Checking…";
  const response = await chrome.runtime.sendMessage({ type: "ping" });
  statusNode.className = response.success ? "connected" : "offline";
  statusNode.textContent = response.success ? "Connected" : "Offline";
  await renderLogs();
}

document.querySelector("#check").addEventListener("click", checkConnection);
document.querySelector("#clear").addEventListener("click", async () => {
  await chrome.runtime.sendMessage({ type: "clear-logs" });
  await renderLogs();
});
renderLogs();
checkConnection();
