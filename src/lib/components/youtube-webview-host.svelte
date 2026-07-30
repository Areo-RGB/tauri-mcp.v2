<script lang="ts">
	import { invoke } from "@tauri-apps/api/core";
	import { Badge } from "$lib/components/ui/badge";
	import { Button } from "$lib/components/ui/button";
	import * as Card from "$lib/components/ui/card";
	import * as ScrollArea from "$lib/components/ui/scroll-area";
	import ExternalLinkIcon from "lucide-svelte/icons/external-link";
	import PanelRightCloseIcon from "lucide-svelte/icons/panel-right-close";
	import PanelRightOpenIcon from "lucide-svelte/icons/panel-right-open";
	import RefreshCwIcon from "lucide-svelte/icons/refresh-cw";
	import TerminalIcon from "lucide-svelte/icons/terminal";
	import { onMount } from "svelte";

	type LogEntry = { timestamp: string; level: "info" | "success" | "error"; message: string };
	let { collapsed = false, onToggle }: { collapsed?: boolean; onToggle?: () => void } = $props();

	let busy = $state(false);
	let error = $state("");
	let logs = $state<LogEntry[]>([]);
	let socketReady = $derived(logs.some((entry) => entry.message.startsWith("Listening on")));

	async function loadLogs() {
		try {
			logs = await invoke<LogEntry[]>("get_chapter_clipper_logs");
			error = "";
		} catch (cause) {
			error = String(cause);
		}
	}

	async function openChrome() {
		busy = true;
		error = "";
		try {
			await invoke("open_youtube_chrome");
		} catch (cause) {
			error = String(cause);
		} finally {
			busy = false;
		}
	}

	onMount(() => {
		void loadLogs();
		const interval = window.setInterval(() => void loadLogs(), 1_000);
		return () => window.clearInterval(interval);
	});
</script>

{#if collapsed}
	<div class="flex h-full flex-col items-center gap-2 py-3">
		<Button size="icon-sm" variant="ghost" aria-label="Open extension activity" title="Open extension activity" onclick={onToggle}>
			<PanelRightOpenIcon />
		</Button>
		<Badge variant={socketReady ? "default" : "destructive"} class="size-2 p-0" title={socketReady ? "Socket listening" : "Socket offline"}><span class="sr-only">{socketReady ? "Socket listening" : "Socket offline"}</span></Badge>
	</div>
{:else}
<div class="bg-muted/30 h-full min-h-0 p-3">
	<Card.Root class="h-full">
		<Card.Header>
			<Card.Title class="flex items-center gap-2"><TerminalIcon />Extension activity</Card.Title>
			<Card.Description>Live events from the local Chapter Clipper socket.</Card.Description>
			<Card.Action class="flex items-center gap-1">
				<Badge variant={socketReady ? "default" : "destructive"}>{socketReady ? "Listening" : "Offline"}</Badge>
				<Button size="icon-sm" variant="ghost" aria-label="Collapse extension activity" title="Collapse extension activity" onclick={onToggle}><PanelRightCloseIcon /></Button>
			</Card.Action>
		</Card.Header>
		<Card.Content class="min-h-0 flex-1">
			<ScrollArea.Root class="bg-background h-full max-h-[calc(100vh-19rem)] rounded-md border">
				<div class="flex flex-col p-3 font-mono text-xs">
					{#each logs as entry, index (`${entry.timestamp}-${index}`)}
						<div class="hover:bg-muted/50 grid grid-cols-[4.5rem_4.5rem_1fr] gap-2 rounded px-2 py-1.5">
							<time class="text-muted-foreground">{entry.timestamp}</time>
							<span class:text-destructive={entry.level === "error"} class:text-primary={entry.level === "success"}>{entry.level}</span>
							<span class="break-words">{entry.message}</span>
						</div>
					{:else}
						<p class="text-muted-foreground p-6 text-center">Waiting for socket activity…</p>
					{/each}
				</div>
				<ScrollArea.Scrollbar orientation="vertical" />
			</ScrollArea.Root>
		</Card.Content>
		<Card.Footer class="justify-between gap-2">
			<Button variant="outline" onclick={loadLogs}><RefreshCwIcon data-icon="inline-start" />Refresh log</Button>
			<Button onclick={openChrome} disabled={busy}><ExternalLinkIcon data-icon="inline-start" />{busy ? "Opening Chrome…" : "Open YouTube"}</Button>
		</Card.Footer>
	</Card.Root>
	{#if error}<p class="text-destructive mt-2 text-sm" role="alert">{error}</p>{/if}
</div>
{/if}
