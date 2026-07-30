<script lang="ts">
	import { invoke } from "@tauri-apps/api/core";
	import YoutubeIcon from "lucide-svelte/icons/youtube";

	let host: HTMLDivElement | undefined;
	let observer: ResizeObserver | undefined;
	let frame = 0;
	let error = $state("");

	function scheduleBounds() {
		cancelAnimationFrame(frame);
		frame = requestAnimationFrame(() => void updateBounds(true));
	}

	async function updateBounds(visible: boolean) {
		if (!host && visible) return;
		const bounds = host?.getBoundingClientRect() ?? { x: 0, y: 0, width: 1, height: 1 };
		try {
			await invoke("set_youtube_webview_bounds", {
				x: bounds.x,
				y: bounds.y,
				width: bounds.width,
				height: bounds.height,
				visible
			});
			error = "";
		} catch (cause) {
			error = String(cause);
		}
	}

	function mountWebview(node: HTMLDivElement) {
		host = node;
		observer = new ResizeObserver(scheduleBounds);
		observer.observe(node);
		window.addEventListener("resize", scheduleBounds);
		scheduleBounds();

		return () => {
			cancelAnimationFrame(frame);
			observer?.disconnect();
			window.removeEventListener("resize", scheduleBounds);
			void updateBounds(false);
			host = undefined;
		};
	}
</script>

<div {@attach mountWebview} class="relative min-h-0 flex-1 overflow-hidden bg-background">
	<div class="text-muted-foreground absolute inset-0 flex flex-col items-center justify-center gap-3 text-sm">
		<YoutubeIcon class="size-8" />
		<p>{error || "Opening the native YouTube webview…"}</p>
	</div>
</div>
