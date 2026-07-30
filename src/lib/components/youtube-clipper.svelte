<script lang="ts">
	import { invoke } from "@tauri-apps/api/core";
	import { Badge } from "$lib/components/ui/badge/index.js";
	import { Button } from "$lib/components/ui/button/index.js";
	import * as Card from "$lib/components/ui/card/index.js";
	import { Checkbox } from "$lib/components/ui/checkbox/index.js";
	import { Input } from "$lib/components/ui/input/index.js";
	import { ScrollArea } from "$lib/components/ui/scroll-area/index.js";
	import { Textarea } from "$lib/components/ui/textarea/index.js";
	import CheckIcon from "@lucide/svelte/icons/check";
	import CloudUploadIcon from "@lucide/svelte/icons/cloud-upload";
	import CopyIcon from "@lucide/svelte/icons/copy";
	import DownloadIcon from "@lucide/svelte/icons/download";
	import FilmIcon from "@lucide/svelte/icons/film";
	import LoaderCircleIcon from "@lucide/svelte/icons/loader-circle";
	import ListPlusIcon from "@lucide/svelte/icons/list-plus";
	import LogInIcon from "@lucide/svelte/icons/log-in";
	import LogOutIcon from "@lucide/svelte/icons/log-out";
	import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
	import ScissorsIcon from "@lucide/svelte/icons/scissors";
	import WrenchIcon from "@lucide/svelte/icons/wrench";
	import YoutubeIcon from "lucide-svelte/icons/youtube";
	import { onMount } from "svelte";

	type ToolsStatus = { ytDlp: boolean; ffmpeg: boolean; ffprobe: boolean; outputDir: string };
	type Chapter = { index: number; title: string; startTime: number; endTime: number; duration: number; selected?: boolean };
	type VideoInfo = { id: string; title: string; duration: number; uploader: string; thumbnail: string; chapters: Chapter[] };
	type Clip = Chapter & { filePath: string };
	type ProcessResult = { title: string; videoPath: string; outputDir: string; clips: Clip[] };
	type Playlist = { id: string; title: string; description: string; privacyStatus: string; itemCount: number };
	type AuthStatus = { connected: boolean; channelTitle: string | null };
	type UploadResult = { playlistId: string; clips: { title: string; videoId: string; url: string }[] };

	const TIMESTAMP_PROMPT = `Can you use the transcript to find only the drills mentioned in the video and give the start and end timestamp for each drill? Check the video title to verify the expected number of drills before answering. Do not use a markdown table or bullets. Return one line per drill in this exact format:

Drill name: start - end

Example:
Jump Squats: 0:26 - 0:56
ExampleEnd`;

	let tools = $state<ToolsStatus | null>(null);
	let url = $state("");
	let mode = $state<"chapters" | "custom">("chapters");
	let customText = $state("");
	let video = $state<VideoInfo | null>(null);
	let customChapters = $state<Chapter[]>([]);
	let result = $state<ProcessResult | null>(null);
	let busy = $state<"fetch" | "process" | null>(null);
	let status = $state("Ready");
	let error = $state("");
	let accountError = $state("");
	let account = $state<AuthStatus | null>(null);
	let playlists = $state<Playlist[]>([]);
	let selectedPlaylistId = $state("");
	let newPlaylistTitle = $state("");
	let newPlaylistDescription = $state("");
	let newPlaylistPrivacy = $state("private");
	let accountBusy = $state<"connect" | "refresh" | "create" | "upload" | null>(null);
	let uploadResult = $state<UploadResult | null>(null);
	let promptCopied = $state(false);
	let loadedExtensionVideoId = "";
	let activeChapters = $derived(mode === "chapters" ? (video?.chapters ?? []) : customChapters);
	let selectedChapters = $derived(activeChapters.filter((chapter) => chapter.selected !== false));
	let toolsReady = $derived(!!tools?.ytDlp && !!tools?.ffmpeg);

	onMount(() => {
		void loadTools();
		void loadExtensionVideo();
		void loadAccount();
		const interval = window.setInterval(() => void loadExtensionVideo(), 1_000);
		return () => window.clearInterval(interval);
	});

	async function loadAccount() {
		try {
			account = await invoke<AuthStatus>("get_youtube_auth_status");
			if (account.connected) await loadPlaylists();
		} catch (cause) {
			accountError = String(cause);
		}
	}

	async function loadPlaylists() {
		playlists = await invoke<Playlist[]>("get_youtube_playlists");
		if (!playlists.some((playlist) => playlist.id === selectedPlaylistId)) {
			selectedPlaylistId = playlists[0]?.id ?? "";
		}
	}

	async function connectYouTube() {
		accountBusy = "connect";
		accountError = "";
		try {
			account = await invoke<AuthStatus>("youtube_authenticate");
			await loadPlaylists();
		} catch (cause) {
			accountError = String(cause);
		} finally {
			accountBusy = null;
		}
	}

	async function refreshYouTubeAccount() {
		accountBusy = "refresh";
		accountError = "";
		try {
			account = await invoke<AuthStatus>("get_youtube_auth_status");
			if (account.connected) await loadPlaylists();
		} catch (cause) {
			accountError = String(cause);
		} finally {
			accountBusy = null;
		}
	}

	async function disconnectYouTube() {
		accountBusy = "refresh";
		accountError = "";
		try {
			await invoke("disconnect_youtube");
			account = { connected: false, channelTitle: null };
			playlists = [];
			selectedPlaylistId = "";
		} catch (cause) {
			accountError = String(cause);
		} finally {
			accountBusy = null;
		}
	}

	async function createPlaylist() {
		if (!newPlaylistTitle.trim()) return;
		accountBusy = "create";
		accountError = "";
		try {
			const playlist = await invoke<Playlist>("create_youtube_playlist", {
				title: newPlaylistTitle.trim(),
				description: newPlaylistDescription.trim(),
				privacyStatus: newPlaylistPrivacy
			});
			playlists = [...playlists, playlist];
			selectedPlaylistId = playlist.id;
			newPlaylistTitle = "";
			newPlaylistDescription = "";
		} catch (cause) {
			accountError = String(cause);
		} finally {
			accountBusy = null;
		}
	}

	async function uploadClips() {
		const currentResult = result;
		if (!currentResult || !selectedPlaylistId) return;
		accountBusy = "upload";
		accountError = "";
		uploadResult = null;
		try {
			uploadResult = await invoke<UploadResult>("upload_youtube_clips", {
				playlistId: selectedPlaylistId,
				clips: currentResult.clips.map((clip) => ({
					title: clip.title,
					filePath: clip.filePath,
					description: `Clipped from ${currentResult.title}`
				}))
			});
			status = `Uploaded ${uploadResult.clips.length} clips to YouTube`;
		} catch (cause) {
			accountError = String(cause);
		} finally {
			accountBusy = null;
		}
	}

	async function loadTools() {
		try { tools = await invoke<ToolsStatus>("get_youtube_tools_status"); }
		catch (cause) { error = String(cause); }
	}

	async function loadExtensionVideo() {
		try {
			const latest = await invoke<VideoInfo | null>("get_latest_extension_video");
			if (!latest || latest.id === loadedExtensionVideoId) return;
			loadedExtensionVideoId = latest.id;
			latest.chapters = latest.chapters.map((chapter) => ({ ...chapter, selected: true }));
			video = latest;
			url = `https://www.youtube.com/watch?v=${latest.id}`;
			mode = latest.chapters.length ? "chapters" : "custom";
			result = null;
			error = "";
			status = latest.chapters.length
				? `Loaded ${latest.chapters.length} chapters from the extension`
				: "The extension video has no chapters";
		} catch (cause) {
			error = String(cause);
		}
	}

	async function fetchVideo() {
		busy = "fetch"; error = ""; result = null; status = "Reading video metadata with yt-dlp…";
		try {
			if (!url.trim()) throw new Error("Paste a YouTube URL, or use the Chrome extension.");
			video = await invoke<VideoInfo>("get_youtube_video_info", { url });
			video.chapters = video.chapters.map((chapter) => ({ ...chapter, selected: true }));
			status = video.chapters.length ? `Found ${video.chapters.length} chapters` : "Video loaded — add custom timestamps to continue";
			if (!video.chapters.length) mode = "custom";
		} catch (cause) { error = String(cause); status = "Could not load video"; }
		finally { busy = null; }
	}

	function timeToSeconds(value: string) {
		const parts = value.trim().split(":").map(Number);
		if (parts.some(Number.isNaN)) return Number.NaN;
		if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
		if (parts.length === 2) return parts[0] * 60 + parts[1];
		return parts.length === 1 ? parts[0] : Number.NaN;
	}

	async function copyTimestampPrompt() {
		error = "";
		try {
			await invoke("set_clipboard_text", { content: TIMESTAMP_PROMPT });
			promptCopied = true;
			status = "Copied the transcript timestamp prompt";
			window.setTimeout(() => promptCopied = false, 2_000);
		} catch (cause) {
			error = String(cause);
		}
	}

	function parseTimestamps() {
		error = "";
		const chapters: Chapter[] = [];
		for (const line of customText.split("\n")) {
			const timestamps = [...line.matchAll(/\b\d+(?::\d{1,2}){1,2}\b/g)];
			if (timestamps.length < 2) continue;
			const startText = timestamps[0][0];
			const endText = timestamps[1][0];
			const startIndex = timestamps[0].index ?? line.length;
			const title = line.slice(0, startIndex).replace(/[\s|:,-]+$/, "").trim();
			if (!title) continue;
			const startTime = timeToSeconds(startText); const endTime = timeToSeconds(endText);
			if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || endTime <= startTime) continue;
			chapters.push({ index: chapters.length + 1, title, startTime, endTime, duration: endTime - startTime, selected: true });
		}
		customChapters = chapters;
		status = chapters.length ? `Parsed ${chapters.length} custom clips` : "No valid timestamp rows found";
		if (!chapters.length) error = "Use one line per clip: Drill name: 0:00 - 1:24";
	}

	function formatTime(seconds: number) {
		const hours = Math.floor(seconds / 3600); const minutes = Math.floor((seconds % 3600) / 60); const secs = Math.floor(seconds % 60);
		return hours ? `${hours}:${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}` : `${minutes}:${String(secs).padStart(2, "0")}`;
	}

	function setAll(selected: boolean) {
		if (mode === "chapters" && video) video.chapters = video.chapters.map((chapter) => ({ ...chapter, selected }));
		else customChapters = customChapters.map((chapter) => ({ ...chapter, selected }));
	}

	async function processVideo() {
		busy = "process"; error = ""; result = null; status = `Downloading and cutting ${selectedChapters.length} clips…`;
		try {
			result = await invoke<ProcessResult>("process_youtube_video", { url, chapters: selectedChapters });
			status = `${result.clips.length} clips ready`;
		} catch (cause) { error = String(cause); status = "Media processing failed"; }
		finally { busy = null; }
	}
</script>

<div class="h-full min-h-0 overflow-auto bg-muted/30">
	<div class="flex w-full flex-col gap-3 p-3">
		<div class="flex flex-wrap items-start justify-between gap-3">
			<div class="flex items-center gap-3"><div class="bg-primary text-primary-foreground grid size-10 place-items-center rounded-lg"><YoutubeIcon class="size-5" /></div><div><h1 class="text-lg font-semibold">Chapter Clipper</h1><p class="text-muted-foreground text-sm">Native yt-dlp and ffmpeg workflow</p></div></div>
			<div class="flex items-center gap-2"><Badge variant={toolsReady ? "default" : "destructive"}>{toolsReady ? "Ready" : "Tools missing"}</Badge></div>
		</div>

		<Card.Root>
			<Card.Header><Card.Title>1. Fetch chapters</Card.Title><Card.Description>Paste a YouTube URL here, or use the separate Chrome extension.</Card.Description></Card.Header>
			<Card.Content class="flex flex-col gap-3">
				<div class="flex gap-2"><Input aria-label="YouTube URL" placeholder="Or paste YouTube URL…" bind:value={url} onkeydown={(event) => event.key === "Enter" && fetchVideo()} /><Button variant="outline" disabled={busy !== null || !toolsReady || !url.trim()} onclick={fetchVideo}>Fetch</Button></div>
				{#if video}<div class="flex items-center gap-3 rounded-lg border bg-muted/30 p-2">{#if video.thumbnail}<img class="h-12 w-20 rounded-md object-cover" src={video.thumbnail} alt="" />{/if}<div class="min-w-0"><p class="line-clamp-2 text-sm font-medium">{video.title}</p><p class="text-muted-foreground text-xs">{formatTime(video.duration)} · {video.chapters.length} chapters</p></div></div>{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>2. Select clips</Card.Title><Card.Description>Choose the chapters to download and cut.</Card.Description></Card.Header>
			<Card.Content class="flex flex-col gap-3">
				<div class="grid grid-cols-2 rounded-md border p-0.5"><Button size="sm" variant={mode === "chapters" ? "secondary" : "ghost"} onclick={() => mode = "chapters"}>Chapters</Button><Button size="sm" variant={mode === "custom" ? "secondary" : "ghost"} onclick={() => mode = "custom"}>Custom</Button></div>
				{#if mode === "custom"}
					<div class="flex flex-wrap items-start justify-between gap-2 rounded-md border bg-muted/30 p-3">
						<div class="grid gap-1"><p class="text-sm font-medium">Simple timestamp format</p><p class="text-muted-foreground text-xs">One line per drill: <code>Drill name: start - end</code></p><pre class="text-muted-foreground overflow-x-auto text-xs">Jump Squats: 0:26 - 0:56
Jump Lunges: 0:56 - 1:29</pre></div>
						<Button size="icon-sm" variant="outline" title="Copy transcript timestamp prompt" aria-label="Copy transcript timestamp prompt" onclick={copyTimestampPrompt}>{#if promptCopied}<CheckIcon />{:else}<CopyIcon />{/if}</Button>
					</div>
					<Textarea aria-label="Drill timestamps" class="min-h-32 font-mono text-xs" placeholder="Jump Squats: 0:26 - 0:56&#10;Jump Lunges: 0:56 - 1:29" bind:value={customText} />
					<Button variant="outline" onclick={parseTimestamps}>Parse timestamps</Button>
				{/if}
				{#if activeChapters.length}<div class="flex items-center justify-between"><p class="text-sm font-medium">{selectedChapters.length} of {activeChapters.length} selected</p><div class="flex gap-1"><Button size="xs" variant="ghost" onclick={() => setAll(true)}>All</Button><Button size="xs" variant="ghost" onclick={() => setAll(false)}>Clear</Button></div></div><ScrollArea class="h-80 rounded-md border"><div class="divide-y">{#each activeChapters as chapter (chapter.index)}<label class="hover:bg-muted/50 flex cursor-pointer items-center gap-2 px-2 py-2.5"><Checkbox bind:checked={chapter.selected} /><span class="min-w-0 flex-1 truncate text-sm">{chapter.title}</span><span class="text-muted-foreground shrink-0 font-mono text-[11px]">{formatTime(chapter.startTime)}</span></label>{/each}</div></ScrollArea>{:else}<div class="text-muted-foreground flex min-h-28 flex-col items-center justify-center gap-2 rounded-md border border-dashed px-3 text-center text-sm"><FilmIcon class="size-5" /><p>{mode === "chapters" ? "Open a video and fetch its chapters" : "Paste and parse timestamps"}</p></div>{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>3. Download and cut</Card.Title><Card.Description>{tools?.outputDir ?? "Checking output folder…"}</Card.Description></Card.Header>
			<Card.Content><Button class="w-full" size="lg" disabled={busy !== null || !video || !selectedChapters.length || !toolsReady} onclick={processVideo}>{#if busy === "process"}<LoaderCircleIcon data-icon="inline-start" class="animate-spin" />Processing media…{:else}<ScissorsIcon data-icon="inline-start" />Download &amp; cut {selectedChapters.length || ""} clips{/if}</Button></Card.Content>
			<Card.Footer class="flex-col items-stretch gap-3"><div class="flex items-center gap-2 text-sm">{#if result}<CheckIcon class="text-green-700" />{:else}<WrenchIcon class="text-muted-foreground" />{/if}<span>{status}</span></div>{#if error}<div class="border-destructive/30 bg-destructive/10 text-destructive rounded-md border p-3 text-sm" role="alert">{error}</div>{/if}{#if result}<div class="rounded-md border"><div class="bg-muted/50 flex items-center justify-between border-b px-3 py-2"><p class="text-sm font-medium">Created clips</p><Badge variant="secondary">{result.clips.length}</Badge></div><div class="divide-y">{#each result.clips as clip (clip.filePath)}<div class="flex items-center gap-3 px-3 py-2"><DownloadIcon class="text-muted-foreground size-4" /><span class="min-w-0 flex-1 truncate text-sm">{clip.title}</span><span class="text-muted-foreground text-xs">{formatTime(clip.duration)}</span></div>{/each}</div><p class="text-muted-foreground border-t px-3 py-2 font-mono text-xs">{result.outputDir}</p></div>{/if}</Card.Footer>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>4. Upload to YouTube</Card.Title><Card.Description>Connect your Google account, choose a playlist, then upload the clips you created.</Card.Description></Card.Header>
			<Card.Content class="flex flex-col gap-3">
				{#if account?.connected}
					<div class="flex flex-wrap items-center justify-between gap-2 rounded-md border bg-muted/30 p-3">
						<div><p class="text-sm font-medium">Connected to YouTube</p><p class="text-muted-foreground text-xs">{account.channelTitle ?? "YouTube account"}</p></div>
						<div class="flex gap-2"><Button size="sm" variant="outline" disabled={accountBusy !== null} onclick={refreshYouTubeAccount}><RefreshCwIcon data-icon="inline-start" />Refresh</Button><Button size="sm" variant="ghost" disabled={accountBusy !== null} onclick={disconnectYouTube}><LogOutIcon data-icon="inline-start" />Disconnect</Button></div>
					</div>
					<div class="flex flex-wrap items-end gap-2">
						<label class="grid min-w-56 flex-1 gap-1.5 text-sm font-medium">Playlist<select class="border-input bg-background h-9 rounded-md border px-3 text-sm" aria-label="YouTube playlist" bind:value={selectedPlaylistId} disabled={accountBusy !== null || !playlists.length}><option value="">{playlists.length ? "Select a playlist" : "No playlists yet"}</option>{#each playlists as playlist (playlist.id)}<option value={playlist.id}>{playlist.title} · {playlist.privacyStatus}</option>{/each}</select></label>
						<Button variant="outline" disabled={accountBusy !== null} onclick={refreshYouTubeAccount}><RefreshCwIcon data-icon="inline-start" />Load playlists</Button>
					</div>
					<div class="rounded-md border p-3"><p class="mb-2 text-sm font-medium">Create a playlist</p><div class="grid gap-2 sm:grid-cols-2"><Input aria-label="New playlist title" placeholder="Playlist title" bind:value={newPlaylistTitle} /><Input aria-label="New playlist description" placeholder="Description (optional)" bind:value={newPlaylistDescription} /></div><div class="mt-2 flex flex-wrap items-center justify-between gap-2"><label class="text-muted-foreground flex items-center gap-2 text-xs">Visibility<select class="border-input bg-background h-8 rounded-md border px-2 text-xs" aria-label="New playlist visibility" bind:value={newPlaylistPrivacy}><option value="private">Private</option><option value="unlisted">Unlisted</option><option value="public">Public</option></select></label><Button size="sm" variant="outline" disabled={accountBusy !== null || !newPlaylistTitle.trim()} onclick={createPlaylist}><ListPlusIcon data-icon="inline-start" />Create playlist</Button></div></div>
					<Button size="lg" disabled={accountBusy !== null || !result || !selectedPlaylistId} onclick={uploadClips}>{#if accountBusy === "upload"}<LoaderCircleIcon data-icon="inline-start" class="animate-spin" />Uploading clips…{:else}<CloudUploadIcon data-icon="inline-start" />Upload {result?.clips.length ?? 0} clips to playlist{/if}</Button>
				{:else}
					<div class="flex flex-wrap items-center justify-between gap-3 rounded-md border border-dashed p-3"><div><p class="text-sm font-medium">YouTube account not connected</p><p class="text-muted-foreground text-xs">Google OAuth lets MCPHub fetch and manage your playlists.</p></div><Button disabled={accountBusy !== null} onclick={connectYouTube}>{#if accountBusy === "connect"}<LoaderCircleIcon data-icon="inline-start" class="animate-spin" />Connecting…{:else}<LogInIcon data-icon="inline-start" />Connect with Google{/if}</Button></div>
				{/if}
				{#if accountError}<div class="border-destructive/30 bg-destructive/10 text-destructive rounded-md border p-3 text-sm" role="alert">{accountError}</div>{/if}
				{#if uploadResult}<div class="rounded-md border"><div class="bg-muted/50 flex items-center justify-between border-b px-3 py-2"><p class="text-sm font-medium">Uploaded to playlist</p><Badge variant="secondary">{uploadResult.clips.length}</Badge></div><div class="divide-y">{#each uploadResult.clips as clip (clip.videoId)}<a class="hover:bg-muted/50 flex items-center gap-2 px-3 py-2 text-sm" href={clip.url} target="_blank" rel="noreferrer"><CheckIcon class="text-green-700" />{clip.title}</a>{/each}</div></div>{/if}
			</Card.Content>
		</Card.Root>
	</div>
</div>
