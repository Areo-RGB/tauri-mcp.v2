<script lang="ts">
	import './app.css';
	import { invoke } from "@tauri-apps/api/core";
	import AppSidebar from "$lib/components/app-sidebar.svelte";
	import ClipboardSaver from "$lib/components/clipboard-saver.svelte";
	import YouTubeWebviewHost from "$lib/components/youtube-webview-host.svelte";
	import YouTubeClipper from "$lib/components/youtube-clipper.svelte";
	import * as Breadcrumb from "$lib/components/ui/breadcrumb/index.js";
	import { Badge } from "$lib/components/ui/badge/index.js";
	import { Button } from "$lib/components/ui/button/index.js";
	import { ScrollArea } from "$lib/components/ui/scroll-area/index.js";
	import { Separator } from "$lib/components/ui/separator/index.js";
	import * as Sidebar from "$lib/components/ui/sidebar/index.js";
	import ExternalLinkIcon from "@lucide/svelte/icons/external-link";
	import HammerIcon from "@lucide/svelte/icons/hammer";
	import PlayIcon from "@lucide/svelte/icons/play";
	import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
	import RotateCcwIcon from "@lucide/svelte/icons/rotate-ccw";
	import SquareIcon from "@lucide/svelte/icons/square";
	import TerminalIcon from "@lucide/svelte/icons/terminal";
	import XIcon from "@lucide/svelte/icons/x";
	import { onDestroy, onMount } from "svelte";

	type HubTarget = "windows" | "wsl";
	type AppTab = HubTarget | "clipboard" | "youtube";

	interface HubProcessInfo {
		running: boolean;
		pid: number | null;
		ngrokRunning: boolean;
		ngrokPid: number | null;
		script: string | null;
		projectDir: string | null;
		lastExitCode: number | null;
		logTail: string;
		hubLogTail: string;
		ngrokLogTail: string
	}

	interface EndpointReachability {
		reachable: boolean;
		statusCode: number | null;
		latencyMs: number;
		url: string;
		detail: string;
	}

	const SSH_WSL_URL = "https://width-cucumber-wavy.ngrok-free.dev/mcp/ssh-wsl/";

	const hubs = {
		windows: {
			label: "Windows",
			subtitle: "Native MCPHub",
			url: "http://localhost:3000",
			port: 3000,
			projectDir: "C:\\Users\\paul\\projects\\mcp_UI\\mcphub"
		},
		wsl: {
			label: "WSL",
			subtitle: "Linux MCPHub",
			url: "http://localhost:3001",
			port: 3001,
			projectDir: "/mnt/c/Users/paul/Documents/Codex/2026-07-29/https-deepwiki-com-samanhappy-mcphub/mcphub"
		}
	} as const;

	const hubEntries = Object.entries(hubs) as [HubTarget, (typeof hubs)[HubTarget]][];
	let activeTab = $state<AppTab>("windows");
	let frameVersions: Record<HubTarget, number> = $state({ windows: 0, wsl: 0 });
	let statuses: Record<HubTarget, HubProcessInfo | null> = $state({ windows: null, wsl: null });
	let busy: HubTarget | null = $state(null);
	let message = $state("");
	let logsOpen = $state(false);
	let statusInterval: number | undefined;
	let endpointInterval: number | undefined;
	let sshReachability = $state<EndpointReachability | null>(null);
	let activeHub = $derived(activeTab === "windows" || activeTab === "wsl" ? hubs[activeTab] : null);
	let activeHubStatus = $derived(activeTab === "windows" || activeTab === "wsl" ? statuses[activeTab] : null);

	async function refreshStatuses() {
		const next = { ...statuses };

		for (const [target] of hubEntries) {
			try {
				next[target] = await invoke<HubProcessInfo>("get_hub_process_status", { target });
			} catch {
				next[target] = null;
			}
		}

		statuses = next;

	}

	async function refreshSshReachability() {
		try {
			sshReachability = await invoke<EndpointReachability>("check_endpoint_reachability", { url: SSH_WSL_URL });
		} catch(error) {
			sshReachability = { reachable: false, statusCode: null, latencyMs: 0, url: SSH_WSL_URL, detail: String(error) };
		}
	}

	async function run(target: HubTarget, script: "build" | "start") {
		busy = target;
		message = "";

		try {
			statuses[target] = await invoke<HubProcessInfo>("run_hub_script", { target, projectDir: hubs[target].projectDir, script });
			statuses = { ...statuses };
			message = `${hubs[target].label}: ${script} started`;
		} catch(error) {
			message = String(error);
		} finally {
			busy = null;
		}
	}

	async function stop(target: HubTarget) {
		busy = target;
		message = "";

		try {
			statuses[target] = await invoke<HubProcessInfo>("stop_hub_process", { target });
			statuses = { ...statuses };
			message = `${hubs[target].label}: stopped`;
		} catch(error) {
			message = String(error);
		} finally {
			busy = null;
		}
	}

	async function restart(target: HubTarget) {
		const script = statuses[target]?.script === "build" ? "build" : "start";

		busy = target;
		message = "";

		try {
			await invoke("stop_hub_process", { target });
			statuses[target] = await invoke<HubProcessInfo>("run_hub_script", { target, projectDir: hubs[target].projectDir, script });
			statuses = { ...statuses };
			message = `${hubs[target].label}: restarted`;
		} catch(error) {
			message = String(error);
		} finally {
			busy = null;
		}
	}

	function selectTab(tab: AppTab) {
		activeTab = tab;

		if (tab === "clipboard") logsOpen = false;
	}

	function reload() {
		if (activeHub && activeTab === "windows" || activeTab === "wsl") frameVersions = { ...frameVersions, [activeTab]: frameVersions[activeTab] + 1 };
	}

	onMount(() => {
		void refreshStatuses();
		void refreshSshReachability();
		statusInterval = window.setInterval(refreshStatuses, 2000);
		endpointInterval = window.setInterval(refreshSshReachability, 10000);
	});

	onDestroy(() => {
		if (statusInterval) window.clearInterval(statusInterval);
		if (endpointInterval) window.clearInterval(endpointInterval);
	});
</script>

<Sidebar.Provider
	style="--sidebar-width: 16rem; --sidebar-width-icon: 3rem;"
>
	<AppSidebar
		activeTab={activeTab}
		statuses={statuses}
		onSelect={selectTab}
	/>

	<Sidebar.Inset class="h-svh min-w-0 overflow-hidden">
		<header
			class="flex min-h-14 shrink-0 flex-wrap items-center justify-between gap-2 border-b px-4 py-2"
		>
			<div class="flex min-w-0 items-center gap-2">
				<Sidebar.Trigger class="-ms-1" />
				<Separator orientation="vertical" class="me-2 h-4" />

				<Breadcrumb.Root>
					<Breadcrumb.List>
						<Breadcrumb.Item class="hidden sm:block"><Breadcrumb.Link href="#">MCPHub</Breadcrumb.Link></Breadcrumb.Item>
						<Breadcrumb.Separator class="hidden sm:block" />

						<Breadcrumb.Item>
							<Breadcrumb.Page>
								{activeHub
									? `${activeHub.label} · :${activeHub.port}`
									: activeTab === "youtube" ? "YouTube Clipper · Native" : "Clipboard Saver"}
							</Breadcrumb.Page>
						</Breadcrumb.Item>
					</Breadcrumb.List>
				</Breadcrumb.Root>
			</div>

			{#if activeHub}
				<div class="flex shrink-0 flex-wrap items-center justify-end gap-2">
					<div class="me-1 flex items-center gap-1.5">
						<Badge variant={activeHubStatus?.running ? "default" : "secondary"}>{activeHubStatus?.running ? `Live · PID ${activeHubStatus.pid}` : "Stopped"}</Badge>
						<Badge variant="outline">ngrok {activeHubStatus?.ngrokRunning ? "on" : "off"}</Badge>
						{#if activeTab === "wsl"}
							<Badge variant={sshReachability?.reachable ? "default" : "destructive"} title={sshReachability?.detail ?? "Checking SSH endpoint"}>
								SSH {sshReachability?.reachable ? `${sshReachability.latencyMs} ms` : "offline"}
							</Badge>
						{/if}
					</div>

					<Button size="sm" variant="outline" disabled={busy !== null || activeHubStatus?.running} onclick={() => run(activeTab as HubTarget, "build")}><HammerIcon data-icon="inline-start" />Build</Button>
					<Button size="sm" disabled={busy !== null || activeHubStatus?.running} onclick={() => run(activeTab as HubTarget, "start")}><PlayIcon data-icon="inline-start" />Start</Button>
					<Button size="sm" variant="outline" disabled={busy !== null || !activeHubStatus?.running} onclick={() => restart(activeTab as HubTarget)}><RotateCcwIcon data-icon="inline-start" />Restart</Button>
					<Button size="sm" variant="destructive" disabled={busy !== null || (!activeHubStatus?.running && !activeHubStatus?.ngrokRunning)} onclick={() => stop(activeTab as HubTarget)}><SquareIcon data-icon="inline-start" />Stop</Button>

					<Button
						size="sm"
						variant={logsOpen ? "secondary" : "outline"}
						onclick={() => logsOpen = !logsOpen}
					><TerminalIcon data-icon="inline-start" />Logs</Button>

					<Button size="sm" variant="outline" onclick={reload}><RefreshCwIcon data-icon="inline-start" />Reload</Button>

					<Button
						size="sm"
						variant="outline"
						href={activeHub?.url}
						target="_blank"
						rel="noreferrer"
					>
						<ExternalLinkIcon data-icon="inline-start" />
						Browser
					</Button>
				</div>
			{/if}
		</header>
		{#if message && activeHub}<p class="text-muted-foreground shrink-0 border-b px-4 py-1 text-xs">{message}</p>{/if}

		{#if activeTab === "clipboard"}
			<ClipboardSaver />
		{:else if activeTab === "youtube"}
			<div class="grid min-h-0 flex-1 grid-cols-[minmax(0,3fr)_minmax(20rem,1fr)] overflow-hidden">
				<YouTubeWebviewHost />
				<div class="min-h-0 border-l"><YouTubeClipper /></div>
			</div>
		{:else}
			<section class="webview-stack">
				{#each hubEntries as [target, hub] (target)}
					{#key frameVersions[target]}
						<iframe
							title={`${hub.label} MCPHub dashboard`}
							src={hub.url}
							class:visible={activeTab === target}
							aria-hidden={activeTab !== target}
						></iframe>
					{/key}
				{/each}
			</section>
		{/if}

		{#if logsOpen && activeHub && activeTab !== "clipboard"}
			<section
				class="log-drawer"
				aria-label={`${activeHub.label} process logs`}
			>
				<div
					class="flex items-center justify-between gap-3 px-4 py-3"
				>
					<div>
						<p class="text-muted-foreground text-xs">Live output · refreshes every 2 seconds</p>
						<h2 class="text-sm font-semibold">{activeHub.label} logs</h2>
					</div>

					<Button
						size="icon-sm"
						variant="ghost"
						aria-label="Close logs"
						onclick={() => logsOpen = false}
					><XIcon /></Button>
				</div>

				<div class="log-grid px-4 pb-4">
					<article class="log-panel">
						<header>
							<span>Hub / build</span>
							<span>{activeHubStatus?.script ?? "not started"}</span>
						</header>

						<ScrollArea class="min-h-0 flex-1"><pre>{activeHubStatus?.hubLogTail || `No ${activeHub.label} build or Hub output yet.`}</pre></ScrollArea>
					</article>

					<article class="log-panel">
						<header>
							<span>ngrok</span>
							<span>{activeHubStatus?.ngrokRunning ? "running" : "stopped"}</span>
						</header>

						<ScrollArea class="min-h-0 flex-1"><pre>{activeHubStatus?.ngrokLogTail || `No ${activeHub.label} ngrok output yet.`}</pre></ScrollArea>
					</article>
				</div>
			</section>
		{/if}
	</Sidebar.Inset>
</Sidebar.Provider>
