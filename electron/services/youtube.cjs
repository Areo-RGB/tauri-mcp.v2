const crypto = require("node:crypto");
const fs = require("node:fs");
const http = require("node:http");
const https = require("node:https");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { WebSocketServer } = require("ws");
const { findExecutable, runProcess } = require("../lib/process.cjs");

const YOUTUBE_YT_DLP = "C:\\Users\\paul\\projects\\YouTube\\backend\\yt-dlp.exe";
const YOUTUBE_COOKIES = "C:\\Users\\paul\\projects\\YouTube\\backend\\cookies.txt";
const DEFAULT_DRIVE_DIR = "G:\\My Drive\\video-drives";
const CHROME_EXECUTABLE = "C:\\Users\\paul\\AppData\\Local\\Google\\Chrome\\Application\\chrome.exe";
const AUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth";
const TOKEN_URL = "https://oauth2.googleapis.com/token";
const API_URL = "https://www.googleapis.com/youtube/v3";
const UPLOAD_URL = "https://www.googleapis.com/upload/youtube/v3";
const SCOPES = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube";

function safeMediaName(value, fallback) {
  const cleaned = String(value)
    .replace(/[<>:"\\/|?*\u0000-\u001f]/g, "")
    .trim()
    .replace(/[\s_-]+/g, "_")
    .slice(0, 80)
    .replace(/^[._-]+|[._-]+$/g, "");
  return cleaned || fallback;
}

function parseErrorBody(body) {
  try {
    const parsed = JSON.parse(body);
    return parsed?.error?.message || body.trim();
  } catch {
    return body.trim();
  }
}

async function responseText(response, label) {
  const body = await response.text();
  if (!response.ok) throw new Error(`${label} failed (${response.status}): ${parseErrorBody(body)}`);
  return body;
}

async function apiJson(url, options, label) {
  const response = await fetch(url, options);
  const body = await responseText(response, label);
  return body ? JSON.parse(body) : {};
}

function moveDirectory(source, destination) {
  try {
    fs.renameSync(source, destination);
  } catch {
    fs.cpSync(source, destination, { recursive: true, errorOnExist: true });
    fs.rmSync(source, { recursive: true, force: true });
  }
}

function uploadFile(location, token, filePath) {
  return new Promise((resolve, reject) => {
    const url = new URL(location);
    const transport = url.protocol === "https:" ? https : http;
    const size = fs.statSync(filePath).size;
    const request = transport.request(url, {
      method: "PUT",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "video/mp4",
        "Content-Length": String(size),
      },
    }, (response) => {
      const chunks = [];
      response.on("data", (chunk) => chunks.push(chunk));
      response.on("end", () => {
        const body = Buffer.concat(chunks).toString("utf8");
        const status = response.statusCode ?? 0;
        if (status < 200 || status >= 300) {
          reject(new Error(`YouTube upload failed (${status}): ${parseErrorBody(body)}`));
          return;
        }
        try {
          resolve(JSON.parse(body));
        } catch (error) {
          reject(new Error(`YouTube returned invalid upload data: ${error.message}`));
        }
      });
    });
    request.setTimeout(60 * 60 * 1000, () => request.destroy(new Error("YouTube upload timed out.")));
    request.once("error", reject);
    fs.createReadStream(filePath).once("error", reject).pipe(request);
  });
}

class YouTubeService {
  constructor({ shell, tokenPath, videosPath }) {
    this.shell = shell;
    this.tokenPath = tokenPath;
    this.videosPath = videosPath;
    this.logs = [];
    this.latestVideo = null;
    this.socket = null;
  }

  oauthConfig() {
    const clientId = process.env.GOOGLE_CLIENT_ID?.trim();
    const clientSecret = process.env.GOOGLE_CLIENT_SECRET?.trim();
    if (!clientId) throw new Error("GOOGLE_CLIENT_ID is not configured. Add it to .env.");
    if (!clientSecret) throw new Error("GOOGLE_CLIENT_SECRET is not configured. Add it to .env.");
    return { clientId, clientSecret };
  }

  loadToken() {
    try {
      return JSON.parse(fs.readFileSync(this.tokenPath, "utf8"));
    } catch {
      throw new Error("YouTube is not connected. Connect a Google account first.");
    }
  }

  saveToken(token) {
    fs.mkdirSync(path.dirname(this.tokenPath), { recursive: true });
    fs.writeFileSync(this.tokenPath, JSON.stringify(token, null, 2), "utf8");
  }

  async refreshToken(token) {
    const { clientId, clientSecret } = this.oauthConfig();
    const body = new URLSearchParams({
      client_id: clientId,
      client_secret: clientSecret,
      refresh_token: token.refresh_token,
      grant_type: "refresh_token",
    });
    const response = await fetch(TOKEN_URL, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body,
    });
    const refreshed = JSON.parse(await responseText(response, "YouTube token refresh"));
    const next = {
      access_token: refreshed.access_token,
      refresh_token: refreshed.refresh_token || token.refresh_token,
      token_type: refreshed.token_type || token.token_type,
      expires_at: Math.floor(Date.now() / 1000) + refreshed.expires_in,
    };
    this.saveToken(next);
    return next;
  }

  async accessToken() {
    const token = this.loadToken();
    if (token.expires_at > Math.floor(Date.now() / 1000) + 60) return token.access_token;
    return (await this.refreshToken(token)).access_token;
  }

  async openBrowser(url) {
    if (process.platform === "win32" && fs.existsSync(CHROME_EXECUTABLE)) {
      spawn(CHROME_EXECUTABLE, [url], { detached: true, stdio: "ignore", windowsHide: false }).unref();
      return;
    }
    await this.shell.openExternal(url);
  }

  async authenticate() {
    const { clientId, clientSecret } = this.oauthConfig();
    const state = crypto.randomBytes(36).toString("base64url");
    const server = http.createServer();
    await new Promise((resolve, reject) => {
      server.once("error", reject);
      server.listen(0, "127.0.0.1", resolve);
    });
    const address = server.address();
    const redirectUri = `http://127.0.0.1:${address.port}/`;
    const codePromise = new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        server.close();
        reject(new Error("Timed out waiting for Google OAuth. Try connecting again."));
      }, 300_000);
      server.on("request", (request, response) => {
        const callback = new URL(request.url, redirectUri);
        if (callback.searchParams.get("state") !== state) {
          response.end("OAuth state did not match. You can close this tab.");
          return;
        }
        const error = callback.searchParams.get("error");
        if (error) {
          response.end("YouTube connection was cancelled. You can close this tab.");
          clearTimeout(timer);
          server.close();
          reject(new Error(`Google OAuth was not completed: ${error}`));
          return;
        }
        const code = callback.searchParams.get("code");
        if (!code) {
          response.end("The callback did not include an authorization code.");
          return;
        }
        response.end("YouTube is connected. You can close this tab and return to MCPHub.");
        clearTimeout(timer);
        server.close();
        resolve(code);
      });
    });
    const authUrl = new URL(AUTH_URL);
    authUrl.search = new URLSearchParams({
      client_id: clientId,
      redirect_uri: redirectUri,
      response_type: "code",
      scope: SCOPES,
      access_type: "offline",
      prompt: "consent",
      state,
    }).toString();
    await this.openBrowser(authUrl.toString());
    const code = await codePromise;
    const body = new URLSearchParams({
      code,
      client_id: clientId,
      client_secret: clientSecret,
      redirect_uri: redirectUri,
      grant_type: "authorization_code",
    });
    const response = await fetch(TOKEN_URL, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body,
    });
    const token = JSON.parse(await responseText(response, "Google OAuth token exchange"));
    if (!token.refresh_token) throw new Error("Google did not return a refresh token. Try connecting again.");
    this.saveToken({
      access_token: token.access_token,
      refresh_token: token.refresh_token,
      token_type: token.token_type || "Bearer",
      expires_at: Math.floor(Date.now() / 1000) + token.expires_in,
    });
    return this.authStatus();
  }

  async authStatus() {
    let token;
    try {
      token = await this.accessToken();
    } catch (error) {
      if (error.message.startsWith("YouTube is not connected")) {
        return { connected: false, channelTitle: null };
      }
      throw error;
    }
    const url = new URL(`${API_URL}/channels`);
    url.search = new URLSearchParams({ part: "snippet", mine: "true" });
    const value = await apiJson(url, {
      headers: { Authorization: `Bearer ${token}` },
    }, "YouTube channel lookup");
    return {
      connected: true,
      channelTitle: value.items?.[0]?.snippet?.title ?? null,
    };
  }

  disconnect() {
    if (fs.existsSync(this.tokenPath)) fs.rmSync(this.tokenPath);
  }

  async playlists() {
    const token = await this.accessToken();
    const playlists = [];
    let pageToken = "";
    do {
      const url = new URL(`${API_URL}/playlists`);
      url.search = new URLSearchParams({
        part: "snippet,contentDetails,status",
        mine: "true",
        maxResults: "50",
        ...(pageToken ? { pageToken } : {}),
      });
      const value = await apiJson(url, {
        headers: { Authorization: `Bearer ${token}` },
      }, "YouTube playlist lookup");
      playlists.push(...(value.items ?? []).map((item) => ({
        id: item.id,
        title: item.snippet?.title ?? "",
        description: item.snippet?.description ?? "",
        privacyStatus: item.status?.privacyStatus ?? "private",
        itemCount: item.contentDetails?.itemCount ?? 0,
      })));
      pageToken = value.nextPageToken ?? "";
    } while (pageToken);
    return playlists;
  }

  async createPlaylist({ title, description, privacyStatus }) {
    const token = await this.accessToken();
    const privacy = ["private", "unlisted", "public"].includes(privacyStatus) ? privacyStatus : "private";
    const url = new URL(`${API_URL}/playlists`);
    url.search = new URLSearchParams({ part: "snippet,status" });
    const item = await apiJson(url, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        snippet: { title: title.trim(), description: description.trim() },
        status: { privacyStatus: privacy },
      }),
    }, "YouTube playlist creation");
    return {
      id: item.id ?? "",
      title: item.snippet?.title ?? "",
      description: item.snippet?.description ?? "",
      privacyStatus: item.status?.privacyStatus ?? privacy,
      itemCount: 0,
    };
  }

  async youtubeExecutable() {
    if (fs.existsSync(YOUTUBE_YT_DLP)) return YOUTUBE_YT_DLP;
    return findExecutable(["yt-dlp.exe", "yt-dlp"]);
  }

  async ffmpegExecutable() {
    return findExecutable(["ffmpeg.exe", "ffmpeg"]);
  }

  accessArgs() {
    return [
      ...(fs.existsSync(YOUTUBE_COOKIES) ? ["--cookies", YOUTUBE_COOKIES] : []),
      "--add-header",
      "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36",
    ];
  }

  async mediaCommand(executable, args, label, timeoutMs = 30 * 60 * 1000) {
    const result = await runProcess(executable, args, { timeoutMs });
    if (result.code !== 0) {
      const stderr = result.stderr.toString("utf8").trim();
      throw new Error(`${label} failed${stderr ? `: ${stderr}` : ""}`);
    }
    return result.stdout.toString("utf8").trim();
  }

  async videoInfo({ url }) {
    const trimmed = String(url ?? "").trim();
    if (!/^https?:\/\//i.test(trimmed)) throw new Error("Enter a valid YouTube URL.");
    const executable = await this.youtubeExecutable();
    if (!executable) throw new Error("yt-dlp was not found. Install it or place yt-dlp.exe in the YouTube backend folder.");
    const raw = await this.mediaCommand(executable, [
      "--dump-single-json",
      "--skip-download",
      "--no-playlist",
      "--no-warnings",
      ...this.accessArgs(),
      trimmed,
    ], "yt-dlp metadata");
    const value = JSON.parse(raw);
    const duration = Number(value.duration || 0);
    return {
      id: value.id ?? "",
      title: value.title ?? "YouTube video",
      duration,
      uploader: value.uploader ?? "",
      thumbnail: value.thumbnail ?? "",
      chapters: (value.chapters ?? []).map((chapter, position) => {
        const startTime = Number(chapter.start_time);
        const endTime = Number(chapter.end_time ?? duration);
        return {
          index: position + 1,
          title: chapter.title ?? "Chapter",
          startTime,
          endTime,
          duration: endTime - startTime,
        };
      }).filter((chapter) => chapter.endTime > chapter.startTime),
    };
  }

  outputDirectory() {
    return path.join(this.videosPath || path.join(os.homedir(), "Videos"), "Chapter Clipper");
  }

  driveDirectory() {
    return process.env.YOUTUBE_DRIVE_DIR || DEFAULT_DRIVE_DIR;
  }

  async toolsStatus() {
    return {
      ytDlp: Boolean(await this.youtubeExecutable()),
      ffmpeg: Boolean(await this.ffmpegExecutable()),
      ffprobe: Boolean(await findExecutable(["ffprobe.exe", "ffprobe"])),
      outputDir: this.driveDirectory(),
    };
  }

  moveToDrive(source) {
    const drive = this.driveDirectory();
    fs.mkdirSync(drive, { recursive: true });
    const folderName = path.basename(source);
    let destination = path.join(drive, folderName);
    let suffix = 2;
    while (fs.existsSync(destination)) {
      destination = path.join(drive, `${folderName}_${suffix}`);
      suffix += 1;
    }
    moveDirectory(source, destination);
    return destination;
  }

  async processVideo({ url, chapters }) {
    if (!Array.isArray(chapters) || !chapters.length) throw new Error("Select or add at least one chapter.");
    const info = await this.videoInfo({ url });
    const folder = path.join(this.outputDirectory(), safeMediaName(info.title, "YouTube_Video"));
    const clipsDirectory = path.join(folder, "clips");
    fs.mkdirSync(clipsDirectory, { recursive: true });
    const ytDlp = await this.youtubeExecutable();
    const output = await this.mediaCommand(ytDlp, [
      "--no-playlist",
      "--no-warnings",
      "-f",
      "bestvideo+bestaudio/best",
      "--merge-output-format",
      "mp4",
      "--print",
      "after_move:filepath",
      "-o",
      path.join(folder, "source.%(ext)s"),
      ...this.accessArgs(),
      String(url).trim(),
    ], "yt-dlp download", 2 * 60 * 60 * 1000);
    let videoPath = output.split(/\r?\n/).reverse().map((item) => item.trim()).find((item) => fs.existsSync(item));
    if (!videoPath) {
      videoPath = fs.readdirSync(folder)
        .map((name) => path.join(folder, name))
        .find((candidate) => path.extname(candidate).toLowerCase() === ".mp4");
    }
    if (!videoPath) throw new Error("yt-dlp finished but the downloaded MP4 could not be found.");
    const ffmpeg = await this.ffmpegExecutable();
    if (!ffmpeg) throw new Error("ffmpeg was not found on PATH.");
    const clips = [];
    for (let position = 0; position < chapters.length; position += 1) {
      const chapter = chapters[position];
      const startTime = Number(chapter.startTime);
      const endTime = Number(chapter.endTime);
      const duration = endTime - startTime;
      if (endTime <= startTime || duration < 0.5) continue;
      const index = position + 1;
      const clipPath = path.join(clipsDirectory, `${String(index).padStart(2, "0")}_${safeMediaName(chapter.title, "Chapter")}.mp4`);
      await this.mediaCommand(ffmpeg, [
        "-y", "-hide_banner", "-loglevel", "error",
        "-ss", String(startTime), "-i", videoPath,
        "-t", String(duration),
        "-c:v", "libx264", "-crf", "18", "-preset", "fast",
        "-c:a", "aac", "-b:a", "192k",
        "-pix_fmt", "yuv420p", "-movflags", "+fast",
        clipPath,
      ], `ffmpeg clip ${index}`, 2 * 60 * 60 * 1000);
      clips.push({
        index,
        title: chapter.title,
        filePath: clipPath,
        startTime,
        endTime,
        duration,
      });
    }
    if (!clips.length) throw new Error("No valid clips were produced.");
    const destination = this.moveToDrive(folder);
    videoPath = path.join(destination, path.basename(videoPath));
    for (const clip of clips) clip.filePath = path.join(destination, "clips", path.basename(clip.filePath));
    return {
      title: info.title,
      videoPath,
      outputDir: path.join(destination, "clips"),
      clips,
    };
  }

  async uploadClip(token, clip) {
    if (!fs.existsSync(clip.filePath)) throw new Error(`Could not open ${clip.title}: file not found`);
    const length = fs.statSync(clip.filePath).size;
    const url = new URL(`${UPLOAD_URL}/videos`);
    url.search = new URLSearchParams({ uploadType: "resumable", part: "snippet,status" });
    const response = await fetch(url, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json; charset=UTF-8",
        "X-Upload-Content-Type": "video/mp4",
        "X-Upload-Content-Length": String(length),
      },
      body: JSON.stringify({
        snippet: { title: clip.title, description: clip.description ?? "" },
        status: { privacyStatus: "private" },
      }),
    });
    if (!response.ok) await responseText(response, `Starting the YouTube upload for ${clip.title}`);
    const location = response.headers.get("location");
    if (!location) throw new Error("YouTube did not return an upload URL.");
    const uploaded = await uploadFile(location, token, clip.filePath);
    if (!uploaded.id) throw new Error(`YouTube did not return a video ID for ${clip.title}.`);
    return uploaded.id;
  }

  async uploadClips({ playlistId, clips }) {
    if (!String(playlistId ?? "").trim()) throw new Error("Select a YouTube playlist first.");
    if (!Array.isArray(clips) || !clips.length) throw new Error("Create clips before uploading them to YouTube.");
    const token = await this.accessToken();
    const uploaded = [];
    for (const clip of clips) {
      const videoId = await this.uploadClip(token, clip);
      const url = new URL(`${API_URL}/playlistItems`);
      url.search = new URLSearchParams({ part: "snippet" });
      await apiJson(url, {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          snippet: {
            playlistId,
            resourceId: { kind: "youtube#video", videoId },
          },
        }),
      }, `Adding ${clip.title} to the playlist`);
      uploaded.push({
        title: clip.title,
        videoId,
        url: `https://www.youtube.com/watch?v=${videoId}`,
      });
    }
    return { playlistId, clips: uploaded };
  }

  log(level, message) {
    this.logs.push({
      timestamp: new Date().toLocaleTimeString([], { hour12: false }),
      level,
      message,
    });
    this.logs = this.logs.slice(-100);
  }

  async handleSocketRequest(request) {
    if (request.action === "ping") {
      this.log("success", "Extension status check succeeded");
      return { success: true, ready: true };
    }
    if (request.action === "fetch-chapters") {
      this.log("info", "Fetching chapters with yt-dlp");
      const result = await this.videoInfo({ url: request.url });
      this.latestVideo = result;
      this.log("success", `yt-dlp found ${result.chapters.length} chapter(s)`);
      return { success: true, result };
    }
    if (request.action === "get-playlists") {
      this.log("info", "Loading YouTube playlists");
      const result = await this.playlists();
      this.log("success", `Loaded ${result.length} YouTube playlist(s)`);
      return { success: true, result };
    }
    this.log("info", `Processing ${request.chapters?.length ?? 0} chapter(s)`);
    const processed = await this.processVideo({ url: request.url, chapters: request.chapters ?? [] });
    if (request.action === "process-upload") {
      const uploaded = await this.uploadClips({
        playlistId: request.playlistId,
        clips: processed.clips.map((clip) => ({
          title: clip.title,
          filePath: clip.filePath,
          description: null,
        })),
      });
      this.log("success", `Uploaded ${uploaded.clips.length} clip(s) to YouTube`);
      return { success: true, result: { processed, uploaded } };
    }
    this.log("success", `Created ${processed.clips.length} clip(s)`);
    return { success: true, result: processed };
  }

  startSocket() {
    this.socket = new WebSocketServer({ host: "127.0.0.1", port: 32145 });
    this.socket.once("listening", () => this.log("info", "Listening on ws://127.0.0.1:32145"));
    this.socket.on("connection", (socket) => {
      this.log("info", "Extension connected");
      socket.on("message", async (raw) => {
        try {
          const request = JSON.parse(raw.toString());
          socket.send(JSON.stringify(await this.handleSocketRequest(request)));
        } catch (error) {
          this.log("error", error.message);
          socket.send(JSON.stringify({ success: false, error: error.message }));
        }
      });
    });
    this.socket.on("error", (error) => this.log("error", `Chapter Clipper socket failed: ${error.message}`));
  }

  close() {
    this.socket?.close();
    this.socket = null;
  }
}

module.exports = { YouTubeService, safeMediaName };
