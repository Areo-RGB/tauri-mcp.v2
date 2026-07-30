<script lang="ts">
	import * as Sidebar from "$lib/components/ui/sidebar/index.js";
	import BoxIcon from "@lucide/svelte/icons/box";
	import ChevronRightIcon from "@lucide/svelte/icons/chevron-right";
	import ClipboardIcon from "@lucide/svelte/icons/clipboard";
	import AndroidIcon from "lucide-svelte/icons/smartphone";
	import YoutubeIcon from "lucide-svelte/icons/youtube";
	import type { ComponentProps } from "svelte";

	type AppTab = "clipboard" | "youtube" | "adb";

	let {
		ref = $bindable(null),
		activeTab,
		onSelect,
		...restProps
	}: ComponentProps<typeof Sidebar.Root> & {
		activeTab: AppTab;
		onSelect: (target: AppTab) => void;
	} = $props();

	const tools = [
		{ target: "youtube", label: "YouTube Clipper", icon: YoutubeIcon },
		{ target: "adb", label: "ADB Android Tools", icon: AndroidIcon },
		{ target: "clipboard", label: "Clipboard Saver", icon: ClipboardIcon }
	] as const;
</script>

<Sidebar.Root collapsible="icon" {...restProps} bind:ref>
	<Sidebar.Header>
		<Sidebar.Menu>
			<Sidebar.MenuItem>
				<Sidebar.MenuButton size="lg" tooltipContent="MCPHub Tools">
					<div class="bg-sidebar-primary text-sidebar-primary-foreground flex aspect-square size-8 items-center justify-center rounded-lg">
						<BoxIcon />
					</div>
					<div class="flex flex-col gap-0.5 leading-none">
						<span class="font-semibold">MCPHub Tools</span>
						<span class="text-xs">Electron desktop</span>
					</div>
				</Sidebar.MenuButton>
			</Sidebar.MenuItem>
		</Sidebar.Menu>
	</Sidebar.Header>

	<Sidebar.Content>
		<Sidebar.Group class="px-2">
			<Sidebar.GroupLabel class="text-sidebar-foreground px-2 text-sm font-medium">Tools</Sidebar.GroupLabel>
			<Sidebar.GroupContent>
				<Sidebar.Menu>
					{#each tools as tool (tool.target)}
						<Sidebar.MenuItem>
							<Sidebar.MenuButton
								class="h-9 ps-4"
								isActive={activeTab === tool.target}
								tooltipContent={tool.label}
								onclick={() => onSelect(tool.target)}
							>
								<ChevronRightIcon class="text-muted-foreground" />
								<tool.icon />
								<span>{tool.label}</span>
							</Sidebar.MenuButton>
							<Sidebar.MenuBadge>Node</Sidebar.MenuBadge>
						</Sidebar.MenuItem>
					{/each}
				</Sidebar.Menu>
			</Sidebar.GroupContent>
		</Sidebar.Group>
	</Sidebar.Content>

	<Sidebar.Footer class="group-data-[collapsible=icon]:hidden">
		<p class="text-muted-foreground truncate px-2 font-mono text-xs">
			{activeTab === "youtube"
				? "Electron media + OAuth bridge"
				: activeTab === "adb"
					? "Electron ADB bridge"
					: "Electron clipboard bridge"}
		</p>
	</Sidebar.Footer>
	<Sidebar.Rail />
</Sidebar.Root>
