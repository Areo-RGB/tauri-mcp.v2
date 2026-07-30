const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("mcphub", {
  invoke: (command, args = {}) => ipcRenderer.invoke("mcphub:invoke", command, args),
  openFile: (options = {}) => ipcRenderer.invoke("mcphub:open-file", options),
});
