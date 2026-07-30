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
	import DownloadIcon from "@lucide/svelte/icons/download";
	import FilmIcon from "@lucide/svelte/icons/film";
	import LoaderCircleIcon from "@lucide/svelte/icons/loader-circle";
	import ScissorsIcon from "@lucide/svelte/icons/scissors";
	import WrenchIcon from "@lucide/svelte/icons/wrench";
	import YoutubeIcon from "lucide-svelte/icons/youtube";
	import { onMount } from "svelte";

	type ToolsStatus = { ytDlp: boolean; ffmpeg: boolean; ffprobe: boolean; outputDir: string };
	type Chapter = { index: number; title: string; startTime: number; endTime: number; duration: number; selected?: boolean };
	type VideoInfo = { id: string; title: string; duration: number; uploader: string; thumbnail: string; chapters: Chapter[] };
	type Clip = Chapter & { filePath: string };
	type ProcessResult = { title: string; videoPath: string; outputDir: string; clips: Clip[] };

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
	let loadedExtensionVideoId = "";
	let activeChapters = $derived(mode === "chapters" ? (video?.chapters ?? []) : customChapters);
	let selectedChapters = $derived(activeChapters.filter((chapter) => chapter.selected !== false));
	let toolsReady = $derived(!!tools?.ytDlp && !!tools?.ffmpeg);

	onMount(() => {
		void loadTools();
		void loadExtensionVideo();
		const interval = window.setInterval(() => void loadExtensionVideo(), 1_000);
		return () => window.clearInterval(interval);
	});

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

	function parseTimestamps() {
		error = "";
		const chapters: Chapter[] = [];
		for (const line of customText.replace(/\|\s*\|/g, "|\n|").split("\n")) {
			const cells = line.split("|").map((cell) => cell.trim()).filter(Boolean);
			if (cells.length < 3 || cells.some((cell) => cell.includes("---"))) continue;
			const title = cells[0].replaceAll("**", "");
			if (["title", "name", "drill name", "#"].includes(title.toLowerCase())) continue;
			const startTime = timeToSeconds(cells[1]); const endTime = timeToSeconds(cells[2]);
			if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || endTime <= startTime) continue;
			chapters.push({ index: chapters.length + 1, title, startTime, endTime, duration: endTime - startTime, selected: true });
		}
		customChapters = chapters;
		status = chapters.length ? `Parsed ${chapters.length} custom clips` : "No valid timestamp rows found";
		if (!chapters.length) error = "Use rows such as: | Intro | 0:00 | 1:24 |";
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
				{#if mode === "custom"}<Textarea aria-label="Markdown timestamp table" class="min-h-32 font-mono text-xs" placeholder="| Intro | 0:00 | 1:24 |&#10;| Main topic | 1:24 | 4:18 |" bind:value={customText} /><Button variant="outline" onclick={parseTimestamps}>Parse timestamps</Button>{/if}
				{#if activeChapters.length}<div class="flex items-center justify-between"><p class="text-sm font-medium">{selectedChapters.length} of {activeChapters.length} selected</p><div class="flex gap-1"><Button size="xs" variant="ghost" onclick={() => setAll(true)}>All</Button><Button size="xs" variant="ghost" onclick={() => setAll(false)}>Clear</Button></div></div><ScrollArea class="h-80 rounded-md border"><div class="divide-y">{#each activeChapters as chapter (chapter.index)}<label class="hover:bg-muted/50 flex cursor-pointer items-center gap-2 px-2 py-2.5"><Checkbox bind:checked={chapter.selected} /><span class="min-w-0 flex-1 truncate text-sm">{chapter.title}</span><span class="text-muted-foreground shrink-0 font-mono text-[11px]">{formatTime(chapter.startTime)}</span></label>{/each}</div></ScrollArea>{:else}<div class="text-muted-foreground flex min-h-28 flex-col items-center justify-center gap-2 rounded-md border border-dashed px-3 text-center text-sm"><FilmIcon class="size-5" /><p>{mode === "chapters" ? "Open a video and fetch its chapters" : "Paste and parse timestamps"}</p></div>{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>3. Download and cut</Card.Title><Card.Description>{tools?.outputDir ?? "Checking output folder…"}</Card.Description></Card.Header>
			<Card.Content><Button class="w-full" size="lg" disabled={busy !== null || !video || !selectedChapters.length || !toolsReady} onclick={processVideo}>{#if busy === "process"}<LoaderCircleIcon data-icon="inline-start" class="animate-spin" />Processing media…{:else}<ScissorsIcon data-icon="inline-start" />Download &amp; cut {selectedChapters.length || ""} clips{/if}</Button></Card.Content>
			<Card.Footer class="flex-col items-stretch gap-3"><div class="flex items-center gap-2 text-sm">{#if result}<CheckIcon class="text-green-700" />{:else}<WrenchIcon class="text-muted-foreground" />{/if}<span>{status}</span></div>{#if error}<div class="border-destructive/30 bg-destructive/10 text-destructive rounded-md border p-3 text-sm" role="alert">{error}</div>{/if}{#if result}<div class="rounded-md border"><div class="bg-muted/50 flex items-center justify-between border-b px-3 py-2"><p class="text-sm font-medium">Created clips</p><Badge variant="secondary">{result.clips.length}</Badge></div><div class="divide-y">{#each result.clips as clip (clip.filePath)}<div class="flex items-center gap-3 px-3 py-2"><DownloadIcon class="text-muted-foreground size-4" /><span class="min-w-0 flex-1 truncate text-sm">{clip.title}</span><span class="text-muted-foreground text-xs">{formatTime(clip.duration)}</span></div>{/each}</div><p class="text-muted-foreground border-t px-3 py-2 font-mono text-xs">{result.outputDir}</p></div>{/if}</Card.Footer>
		</Card.Root>
	</div>
</div>
