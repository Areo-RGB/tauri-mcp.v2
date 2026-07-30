<script lang="ts">
	import * as Sidebar from "$lib/components/ui/sidebar/index.js";
	import BoxIcon from "@lucide/svelte/icons/box";
	import ChevronRightIcon from "@lucide/svelte/icons/chevron-right";
	import ClipboardIcon from "@lucide/svelte/icons/clipboard";
	import MonitorIcon from "@lucide/svelte/icons/monitor";
	import TerminalIcon from "@lucide/svelte/icons/terminal";
	import YoutubeIcon from "lucide-svelte/icons/youtube";
	import type { ComponentProps } from "svelte";

	type HubTarget = "windows" | "wsl";
	type AppTab = HubTarget | "clipboard" | "youtube";
	type HubStatus = {
		running: boolean;
		pid: number | null;
		ngrokRunning: boolean;
		ngrokPid: number | null;
	} | null;

	let {
		ref = $bindable(null),
		activeTab,
		statuses,
		onSelect,
		...restProps
	}: ComponentProps<typeof Sidebar.Root> & {
		activeTab: AppTab;
		statuses: Record<HubTarget, HubStatus>;
		onSelect: (target: AppTab) => void;
	} = $props();

	const hubs = [
		{ target: "windows", label: "Windows", subtitle: "Native MCPHub", port: 3000, icon: MonitorIcon },
		{ target: "wsl", label: "WSL", subtitle: "Linux MCPHub", port: 3001, icon: TerminalIcon },
	] as const;

</script>

<Sidebar.Root collapsible="icon" {...restProps} bind:ref>
	<Sidebar.Header>
		<Sidebar.Menu>
			<Sidebar.MenuItem>
				<Sidebar.MenuButton size="lg" tooltipContent="MCPHub Desktop">
					<div class="bg-sidebar-primary text-sidebar-primary-foreground flex aspect-square size-8 items-center justify-center rounded-lg">
						<BoxIcon />
					</div>
					<div class="flex flex-col gap-0.5 leading-none">
						<span class="font-semibold">MCPHub</span>
						<span class="text-xs">Desktop control</span>
					</div>
				</Sidebar.MenuButton>
			</Sidebar.MenuItem>
		</Sidebar.Menu>
	</Sidebar.Header>

	<Sidebar.Content>
		<Sidebar.Group class="px-2">
			<Sidebar.GroupLabel class="text-sidebar-foreground px-2 text-sm font-medium">Dashboards</Sidebar.GroupLabel>
			<Sidebar.GroupContent>
				<Sidebar.Menu>
					{#each hubs as hub (hub.target)}
						<Sidebar.MenuItem>
							<Sidebar.MenuButton
								class="h-9 ps-4"
								isActive={activeTab === hub.target}
								tooltipContent={`${hub.label} · :${hub.port}`}
								onclick={() => onSelect(hub.target)}
							>
								<ChevronRightIcon class="text-muted-foreground" />
								<hub.icon />
								<span>{hub.label} <span class="text-muted-foreground text-xs">· :{hub.port}</span></span>
							</Sidebar.MenuButton>
							<Sidebar.MenuBadge>{statuses[hub.target]?.running ? "On" : "Off"}</Sidebar.MenuBadge>
						</Sidebar.MenuItem>
					{/each}
					<Sidebar.MenuItem>
						<Sidebar.MenuButton class="h-9 ps-4" isActive={activeTab === "youtube"} tooltipContent="YouTube Clipper · Native" onclick={() => onSelect("youtube")}>
							<ChevronRightIcon class="text-muted-foreground" />
							<YoutubeIcon />
							<span>YouTube Clipper <span class="text-muted-foreground text-xs">· Native</span></span>
						</Sidebar.MenuButton>
						<Sidebar.MenuBadge>Rust</Sidebar.MenuBadge>
					</Sidebar.MenuItem>
					<Sidebar.MenuItem>
						<Sidebar.MenuButton class="h-9 ps-4" isActive={activeTab === "clipboard"} tooltipContent="Clipboard Saver" onclick={() => onSelect("clipboard")}>
							<ChevronRightIcon class="text-muted-foreground" />
							<ClipboardIcon />
							<span>Clipboard Saver</span>
						</Sidebar.MenuButton>
					</Sidebar.MenuItem>
				</Sidebar.Menu>
			</Sidebar.GroupContent>
		</Sidebar.Group>
	</Sidebar.Content>

	<Sidebar.Footer class="group-data-[collapsible=icon]:hidden">
		<p class="text-muted-foreground truncate px-2 font-mono text-xs">{activeTab === "windows" ? "http://localhost:3000" : activeTab === "wsl" ? "http://localhost:3001" : activeTab === "youtube" ? "Native Rust media tools" : "Native clipboard tools"}</p>
	</Sidebar.Footer>
	<Sidebar.Rail />
</Sidebar.Root>
