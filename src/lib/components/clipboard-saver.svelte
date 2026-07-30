<script lang="ts">
	import { invoke } from "@tauri-apps/api/core";
	import { Badge } from "$lib/components/ui/badge/index.js";
	import { Button } from "$lib/components/ui/button/index.js";
	import * as Card from "$lib/components/ui/card/index.js";
	import { ScrollArea } from "$lib/components/ui/scroll-area/index.js";
	import { Textarea } from "$lib/components/ui/textarea/index.js";
	import ClipboardCopyIcon from "@lucide/svelte/icons/clipboard-copy";
	import ClipboardPasteIcon from "@lucide/svelte/icons/clipboard-paste";
	import HistoryIcon from "@lucide/svelte/icons/history";
	import PauseIcon from "@lucide/svelte/icons/pause";
	import PlayIcon from "@lucide/svelte/icons/play";
	import RadioIcon from "@lucide/svelte/icons/radio";
	import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
	import SaveIcon from "@lucide/svelte/icons/save";
	import { onDestroy, onMount } from "svelte";

	type ClipboardSnapshot = { content: string; extension: string };
	type SaveResult = { path: string; extension: string };
	type RunResult = { output: string; extension: string; exitCode: number; path: string };
	type HistoryEntry = ClipboardSnapshot & { capturedAt: string };

	const runnableTypes = new Set(["py", "js", "ts", "ps1", "bat"]);
	let content = $state("");
	let extension = $state("txt");
	let status = $state("Reading the Windows clipboard…");
	let busy = $state<"save" | "run" | "copy" | null>(null);
	let lastClipboard = $state<string | null>(null);
	let autoSync = $state(true);
	let history = $state<HistoryEntry[]>([]);
	let clipboardInterval: number | undefined;
	let detectionTimer: number | undefined;
	let lineCount = $derived(content ? content.split(/\r?\n/).length : 0);
	let isRunnable = $derived(runnableTypes.has(extension));
	let hasEdits = $derived(lastClipboard !== null && content !== lastClipboard);

	function remember(snapshot: ClipboardSnapshot) {
		if (!snapshot.content || history[0]?.content === snapshot.content) return;
		history = [{ ...snapshot, capturedAt: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" }) }, ...history.filter((entry) => entry.content !== snapshot.content)].slice(0, 20);
	}

	async function refresh(force = false, silent = false) {
		try {
			const snapshot = await invoke<ClipboardSnapshot>("get_clipboard_snapshot");
			if (snapshot.content !== lastClipboard || force) {
				remember(snapshot);
				content = snapshot.content;
				extension = snapshot.extension;
				lastClipboard = snapshot.content;
				status = content ? `Ready to save as .${extension}` : "Clipboard is empty or does not contain text.";
			} else if (!silent) {
				status = content ? `Clipboard refreshed · detected .${extension}` : "Clipboard is empty or does not contain text.";
			}
		} catch (error) { if (!silent) status = String(error); }
	}

	function scheduleDetection() {
		if (detectionTimer) window.clearTimeout(detectionTimer);
		detectionTimer = window.setTimeout(async () => {
			const snapshot = await invoke<ClipboardSnapshot>("detect_clipboard_type", { content });
			extension = snapshot.extension;
			status = content ? `Edited content detected as .${extension}` : "Editor is empty.";
		}, 180);
	}

	async function copyBack() {
		busy = "copy";
		try {
			const snapshot = await invoke<ClipboardSnapshot>("set_clipboard_text", { content });
			lastClipboard = snapshot.content; extension = snapshot.extension; remember(snapshot);
			status = "Editor content copied to the Windows clipboard.";
		} catch (error) { status = `Copy failed: ${String(error)}`; }
		finally { busy = null; }
	}

	async function save() {
		if (!content) { status = "Nothing saved: the editor has no text."; return; }
		busy = "save";
		try {
			const result = await invoke<SaveResult>("save_clipboard_text", { content });
			extension = result.extension; status = `Saved: ${result.path}`;
		} catch (error) { status = `Save failed: ${String(error)}`; }
		finally { busy = null; }
	}

	async function run() {
		if (!content) { status = "Nothing to run: the editor has no text."; return; }
		busy = "run"; status = `Saving and running detected .${extension} code…`;
		try {
			const result = await invoke<RunResult>("run_clipboard_text", { content });
			content = result.output; lastClipboard = result.output; extension = result.extension;
			remember({ content: result.output, extension: "txt" });
			status = result.exitCode === 0 ? `Run complete · output copied to clipboard · saved ${result.path}` : `Run failed with exit code ${result.exitCode} · output copied to clipboard`;
		} catch (error) { status = `Run failed: ${String(error)}`; }
		finally { busy = null; }
	}

	function restore(entry: HistoryEntry) {
		content = entry.content; extension = entry.extension;
		status = `Restored ${entry.capturedAt} snapshot to the editor.`;
	}

	onMount(() => {
		void refresh();
		clipboardInterval = window.setInterval(() => { if (autoSync && !busy && !hasEdits) void refresh(false, true); }, 500);
	});
	onDestroy(() => {
		if (clipboardInterval) window.clearInterval(clipboardInterval);
		if (detectionTimer) window.clearTimeout(detectionTimer);
	});
</script>

<div class="flex min-h-0 flex-1 bg-muted/30 p-4 md:p-6">
	<div class="mx-auto grid min-h-0 w-full max-w-7xl gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
		<Card.Root class="flex min-h-0 flex-col">
			<Card.Header>
				<div class="flex items-start justify-between gap-4">
					<div class="flex items-center gap-3">
						<div class="bg-primary text-primary-foreground flex size-10 items-center justify-center rounded-lg"><ClipboardPasteIcon /></div>
						<div><Card.Title>Clipboard Saver</Card.Title><Card.Description>Live clipboard preview, editor, detector, saver, and code runner.</Card.Description></div>
					</div>
					<div class="flex flex-wrap justify-end gap-2">
						<Badge variant={isRunnable ? "default" : "secondary"}>.{extension}</Badge>
						{#if hasEdits}<Badge variant="outline">Edited</Badge>{/if}
					</div>
				</div>
			</Card.Header>
			<Card.Content class="flex min-h-0 flex-1 flex-col gap-2">
				<Textarea bind:value={content} oninput={scheduleDetection} spellcheck="false" placeholder="Copy or type text and code here…" class="min-h-0 flex-1 resize-none font-mono text-sm" />
				<div class="text-muted-foreground flex flex-wrap justify-between gap-2 text-xs">
					<span>{content.length.toLocaleString()} characters · {lineCount.toLocaleString()} lines</span>
					<span>{isRunnable ? `.${extension} can run here` : `.${extension} saves without execution`}</span>
				</div>
			</Card.Content>
			<Card.Footer class="flex flex-wrap items-center gap-2">
				<Button disabled={!content || busy !== null} onclick={save}><SaveIcon data-icon="inline-start" />{busy === "save" ? "Saving…" : "Save to Desktop"}</Button>
				<Button variant="secondary" disabled={!content || !isRunnable || busy !== null} onclick={run}><PlayIcon data-icon="inline-start" />{busy === "run" ? "Running…" : "Run"}</Button>
				<Button variant="outline" disabled={!content || busy !== null} onclick={copyBack}><ClipboardCopyIcon data-icon="inline-start" />{busy === "copy" ? "Copying…" : "Copy"}</Button>
				<Button variant="outline" disabled={busy !== null} onclick={() => refresh(true)}><RefreshCwIcon data-icon="inline-start" />Refresh</Button>
				<Button variant="ghost" onclick={() => (autoSync = !autoSync)}>{#if autoSync}<RadioIcon data-icon="inline-start" />Live{:else}<PauseIcon data-icon="inline-start" />Paused{/if}</Button>
				<p class="text-muted-foreground min-w-64 flex-1 text-sm xl:text-right">{status}</p>
			</Card.Footer>
		</Card.Root>

		<Card.Root class="hidden min-h-0 flex-col lg:flex">
			<Card.Header><div class="flex items-center gap-2"><HistoryIcon class="size-4" /><Card.Title>Session history</Card.Title></div><Card.Description>Last 20 unique clipboard values.</Card.Description></Card.Header>
			<Card.Content class="min-h-0 flex-1 px-2">
				<ScrollArea class="h-full px-2">
					{#if history.length === 0}
						<p class="text-muted-foreground px-2 py-6 text-center text-sm">Clipboard snapshots will appear here.</p>
					{:else}
						<div class="flex flex-col gap-1 pb-2">
							{#each history as entry (entry.capturedAt + entry.content)}
								<Button variant="ghost" class="h-auto w-full justify-start px-2 py-2 text-left" onclick={() => restore(entry)}>
									<span class="min-w-0 flex-1"><span class="block truncate text-xs font-medium">{entry.content.replace(/\s+/g, " ")}</span><span class="text-muted-foreground mt-1 block text-xs">.{entry.extension} · {entry.capturedAt}</span></span>
								</Button>
							{/each}
						</div>
					{/if}
				</ScrollArea>
			</Card.Content>
		</Card.Root>
	</div>
</div>
