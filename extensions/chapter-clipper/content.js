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
  let playlists = [];
  let selectedPlaylistId = "";
  let playlistsLoading = false;
  let playlistLoadAttempted = false;
  let processing = false;

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
      return { row: entry.row, index: index + 1, title: entry.title, startTime: entry.startTime, endTime, duration: endTime - entry.startTime };
    }).filter((chapter) => chapter.duration > 0.5);
  };
  const loadPlaylists = async () => {
    if (playlistsLoading || playlistLoadAttempted) return;
    playlistLoadAttempted = true;
    playlistsLoading = true;
    try {
      const response = await chrome.runtime.sendMessage({ type: "get-playlists" });
      if (!response?.success) throw new Error(response?.error || "Could not load playlists");
      playlists = response.result || [];
      const saved = await chrome.storage.local.get("selectedPlaylistId");
      selectedPlaylistId = playlists.some((playlist) => playlist.id === saved.selectedPlaylistId)
        ? saved.selectedPlaylistId
        : playlists[0]?.id || "";
      if (selectedPlaylistId) await chrome.storage.local.set({ selectedPlaylistId });
      playlistsLoading = false;
      document.querySelectorAll(".mcphub-playlist-select").forEach((select) => select.remove());
      inject();
    } catch (error) {
      report("playlist-error", `YouTube playlist lookup failed: ${error}`, "error");
      window.setTimeout(() => {
        playlistLoadAttempted = false;
        void loadPlaylists();
      }, 10_000);
    } finally {
      playlistsLoading = false;
    }
  };
  const processChapters = async (chapters, button) => {
    if (!selectedPlaylistId) {
      toast("Select a YouTube playlist first.");
      return;
    }
    if (processing) {
      toast("Another chapter upload is already running.");
      return;
    }
    processing = true;
    document.querySelectorAll(".mcphub-chapter-upload, .mcphub-clipper-button").forEach((control) => { control.disabled = true; });
    const previousLabel = button.textContent;
    button.textContent = "Working…";
    const note = toast(`Downloading, cutting, and uploading ${chapters.length} chapter(s)…`);
    try {
      const response = await chrome.runtime.sendMessage({
        type: "process-upload",
        payload: {
          url: location.href,
          playlistId: selectedPlaylistId,
          chapters: chapters.map(({ row, ...chapter }) => chapter)
        }
      });
      if (!response?.success) throw new Error(response?.error || "Unknown MCPHub error");
      const uploaded = response.result?.uploaded?.clips || [];
      note.textContent = `${uploaded.length} clip${uploaded.length === 1 ? "" : "s"} uploaded to ${playlists.find((playlist) => playlist.id === selectedPlaylistId)?.title || "playlist"}`;
    } catch (error) {
      note.textContent = `Chapter Clipper: ${error}`;
    } finally {
      processing = false;
      document.querySelectorAll(".mcphub-chapter-upload, .mcphub-clipper-button").forEach((control) => { control.disabled = false; });
      button.textContent = previousLabel;
    }
  };
  const addButton = (container, chapters, floating = false) => {
    document.querySelector(".mcphub-clipper-floating")?.remove();
    if (container.querySelector(".mcphub-clipper-button")) return;
    const button = document.createElement("button");
    button.className = `mcphub-clipper-button${floating ? " mcphub-clipper-floating" : ""}`;
    button.textContent = `Upload all ${chapters.length}`;
    button.onclick = () => processChapters(chapters, button);
    container.prepend(button);
  };
  const addPlaylistSelect = (panel) => {
    if (panel.querySelector(".mcphub-playlist-select")) return;
    const transcript = [...panel.querySelectorAll("button, tp-yt-paper-tab, yt-tab-shape")]
      .find((node) => node.textContent?.trim().toLowerCase() === "transcript");
    const anchor = transcript?.parentElement || panel.querySelector("#header, #tabs-container, [role='tablist']");
    if (!anchor) return;
    const select = document.createElement("select");
    select.className = "mcphub-playlist-select";
    select.title = "Upload chapter clips to this YouTube playlist";
    select.setAttribute("aria-label", "Upload playlist");
    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = playlistsLoading ? "Loading playlists…" : "Select playlist";
    select.append(placeholder);
    for (const playlist of playlists) {
      const option = document.createElement("option");
      option.value = playlist.id;
      option.textContent = playlist.title;
      select.append(option);
    }
    select.value = selectedPlaylistId;
    select.disabled = playlistsLoading || !playlists.length;
    select.onchange = async (event) => {
      event.stopPropagation();
      selectedPlaylistId = select.value;
      await chrome.storage.local.set({ selectedPlaylistId });
    };
    select.onclick = (event) => event.stopPropagation();
    transcript ? transcript.insertAdjacentElement("afterend", select) : anchor.append(select);
  };
  const addChapterButtons = (chapters) => {
    for (const chapter of chapters) {
      if (!chapter.row || chapter.row.querySelector(".mcphub-chapter-upload")) continue;
      const target = chapter.row.querySelector("#endpoint, a") || chapter.row;
      const button = document.createElement("button");
      button.className = "mcphub-chapter-upload";
      button.type = "button";
      button.textContent = "Upload";
      button.title = `Download, cut, and upload ${chapter.title}`;
      button.setAttribute("aria-label", button.title);
      button.onclick = (event) => {
        event.preventDefault();
        event.stopPropagation();
        void processChapters([chapter], button);
      };
      target.append(button);
    }
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
    const localChapters = readChapters();
    const chapters = localChapters.length ? localChapters : remoteChapters;
    if (!chapters.length) {
      return;
    }
    void loadPlaylists();
    addPlaylistSelect(panel);
    addChapterButtons(localChapters);
    addButton(panel, chapters);
    report("ready", `Upload controls added for ${chapters.length} chapter(s)`, "success");
  };
  let queued = false;
  new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; inject(); });
  }).observe(document.documentElement, { childList: true, subtree: true });
  inject();
})();
