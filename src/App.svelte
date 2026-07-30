<script lang="ts">
	import "./app.css";
	import AdbPane from "$lib/components/adb-pane.svelte";
	import AppSidebar from "$lib/components/app-sidebar.svelte";
	import ClipboardSaver from "$lib/components/clipboard-saver.svelte";
	import YouTubeClipper from "$lib/components/youtube-clipper.svelte";
	import YouTubeWebviewHost from "$lib/components/youtube-webview-host.svelte";
	import * as Breadcrumb from "$lib/components/ui/breadcrumb/index.js";
	import { Separator } from "$lib/components/ui/separator/index.js";
	import * as Sidebar from "$lib/components/ui/sidebar/index.js";

	type AppTab = "clipboard" | "youtube" | "adb";

	let activeTab = $state<AppTab>("youtube");
	let youtubeLogsOpen = $state(false);

	const pageTitle = $derived(
		activeTab === "youtube"
			? "YouTube Clipper · Electron"
			: activeTab === "adb"
				? "ADB · Android Tools"
				: "Clipboard Saver"
	);
</script>

<Sidebar.Provider style="--sidebar-width: 16rem; --sidebar-width-icon: 3rem;">
	<AppSidebar {activeTab} onSelect={(tab) => (activeTab = tab)} />

	<Sidebar.Inset class="h-svh min-w-0 overflow-hidden">
		<header class="flex min-h-14 shrink-0 items-center border-b px-4 py-2">
			<div class="flex min-w-0 items-center gap-2">
				<Sidebar.Trigger class="-ms-1" />
				<Separator orientation="vertical" class="me-2 h-4" />
				<Breadcrumb.Root>
					<Breadcrumb.List>
						<Breadcrumb.Item class="hidden sm:block">
							<Breadcrumb.Link href="#">MCPHub Tools</Breadcrumb.Link>
						</Breadcrumb.Item>
						<Breadcrumb.Separator class="hidden sm:block" />
						<Breadcrumb.Item><Breadcrumb.Page>{pageTitle}</Breadcrumb.Page></Breadcrumb.Item>
					</Breadcrumb.List>
				</Breadcrumb.Root>
			</div>
		</header>

		{#if activeTab === "clipboard"}
			<ClipboardSaver />
		{:else if activeTab === "youtube"}
			<div class="flex min-h-0 flex-1 overflow-hidden">
				<div class="min-w-0 flex-1"><YouTubeClipper /></div>
				<aside
					class="bg-background min-h-0 shrink-0 border-l transition-[width] duration-200"
					class:w-12={!youtubeLogsOpen}
					class:w-96={youtubeLogsOpen}
					aria-label="Extension activity sidebar"
				>
					<YouTubeWebviewHost
						collapsed={!youtubeLogsOpen}
						onToggle={() => (youtubeLogsOpen = !youtubeLogsOpen)}
					/>
				</aside>
			</div>
		{:else}
			<AdbPane />
		{/if}
	</Sidebar.Inset>
</Sidebar.Provider>
