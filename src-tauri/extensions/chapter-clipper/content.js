(() => {
  if (window.__MCPHUB_SOCKET_CLIPPER__) return;
  window.__MCPHUB_SOCKET_CLIPPER__ = true;
  const reported = new Set();
  const report = (key, message, level = "info") => {
    if (reported.has(key)) return;
    reported.add(key);
    void chrome.runtime.sendMessage({ type: "extension-log", level, message });
  };
  report("loaded", `Content script loaded on ${location.pathname}`);
  let fetchedVideoId = "";
  let remoteChapters = [];

  const parseTime = (text) => {
    const match = text?.trim().match(/^(?:(\d+):)?(\d+):(\d+)$/);
    return match ? Number(match[1] || 0) * 3600 + Number(match[2]) * 60 + Number(match[3]) : null;
  };
  const toast = (text) => {
    document.querySelector(".mcphub-clipper-toast")?.remove();
    const node = document.createElement("div");
    node.className = "mcphub-clipper-toast";
    node.textContent = text;
    document.body.append(node);
    return node;
  };
  const readChapters = () => {
    const rows = [...document.querySelectorAll("ytd-macro-markers-list-item-renderer, yt-list-item-view-model")];
    const duration = document.querySelector("video")?.duration || 0;
    const entries = rows.map((row) => {
      const labels = [...row.querySelectorAll("span, yt-formatted-string")].map((node) => node.textContent?.trim());
      const startTime = labels.map(parseTime).find((value) => value !== null);
      const title = labels.find((value) => value && parseTime(value) === null && value.length > 1) || "Chapter";
      return startTime === undefined ? null : { row, title, startTime };
    }).filter(Boolean);
    return entries.map((entry, index) => {
      const endTime = entries[index + 1]?.startTime ?? duration;
      return { index: index + 1, title: entry.title, startTime: entry.startTime, endTime, duration: endTime - entry.startTime };
    }).filter((chapter) => chapter.duration > 0.5);
  };
  const processChapters = async (chapters, button) => {
    button.disabled = true;
    const note = toast(`Sending ${chapters.length} chapter(s) to MCPHub…`);
    try {
      const response = await chrome.runtime.sendMessage({
        type: "process-chapters",
        payload: { url: location.href, chapters }
      });
      if (!response?.success) throw new Error(response?.error || "Unknown MCPHub error");
      note.textContent = `${response.result.clips.length} clips saved to ${response.result.outputDir}`;
    } catch (error) {
      note.textContent = `Chapter Clipper: ${error}`;
    } finally {
      button.disabled = false;
    }
  };
  const addButton = (container, chapters, floating = false) => {
    document.querySelector(".mcphub-clipper-floating")?.remove();
    if (container.querySelector(".mcphub-clipper-button")) return;
    const button = document.createElement("button");
    button.className = `mcphub-clipper-button${floating ? " mcphub-clipper-floating" : ""}`;
    button.textContent = `Download ${chapters.length} chapter${chapters.length === 1 ? "" : "s"}`;
    button.onclick = () => processChapters(chapters, button);
    container.prepend(button);
  };
  const fetchChapters = async (videoId) => {
    if (!videoId || fetchedVideoId === videoId) return;
    fetchedVideoId = videoId;
    remoteChapters = [];
    report(`fetch-${videoId}`, `Video ${videoId} detected; asking yt-dlp for chapters`);
    const response = await chrome.runtime.sendMessage({ type: "fetch-chapters", payload: { url: location.href } });
    if (new URL(location.href).searchParams.get("v") !== videoId) return;
    if (!response?.success) {
      report(`fetch-error-${videoId}`, `yt-dlp chapter lookup failed: ${response?.error || "unknown error"}`, "error");
      return;
    }
    remoteChapters = response.result?.chapters || [];
    if (!remoteChapters.length) {
      report(`empty-${videoId}`, "yt-dlp found no chapters for this video", "error");
      return;
    }
    report(`fetched-${videoId}`, `yt-dlp returned ${remoteChapters.length} chapter(s)`, "success");
    inject();
  };
  const inject = () => {
    const videoId = new URL(location.href).searchParams.get("v") || "";
    if (videoId !== fetchedVideoId) {
      document.querySelector(".mcphub-clipper-floating")?.remove();
      void fetchChapters(videoId);
    }
    const panel = document.querySelector("ytd-engagement-panel-section-list-renderer");
    if (!panel) {
      if (remoteChapters.length) addButton(document.body, remoteChapters, true);
      return;
    }
    const chapters = readChapters().length ? readChapters() : remoteChapters;
    if (!chapters.length) {
      return;
    }
    addButton(panel, chapters);
    report("ready", `Download button added for ${chapters.length} chapter(s)`, "success");
  };
  let queued = false;
  new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; inject(); });
  }).observe(document.documentElement, { childList: true, subtree: true });
  inject();
})();
