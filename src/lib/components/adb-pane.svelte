<script lang="ts">
	import { invoke, openFile } from "$lib/desktop";
	import { Badge } from "$lib/components/ui/badge";
	import { Button } from "$lib/components/ui/button";
	import * as Card from "$lib/components/ui/card";
	import { Checkbox } from "$lib/components/ui/checkbox";
	import { Input } from "$lib/components/ui/input";
	import * as ScrollArea from "$lib/components/ui/scroll-area";
	import AndroidIcon from "lucide-svelte/icons/smartphone";
	import CameraIcon from "lucide-svelte/icons/camera";
	import DownloadIcon from "lucide-svelte/icons/download";
	import FileCogIcon from "lucide-svelte/icons/file-cog";
	import FolderOpenIcon from "lucide-svelte/icons/folder-open";
	import LoaderCircleIcon from "lucide-svelte/icons/loader-circle";
	import PlayIcon from "lucide-svelte/icons/play";
	import RefreshCwIcon from "lucide-svelte/icons/refresh-cw";
	import TerminalIcon from "lucide-svelte/icons/terminal";
	import { onMount } from "svelte";

	type Device = { serial: string; model: string; device: string; state: string };
	type CommandResult = { ok: boolean; command: string; stdout: string; stderr: string; lines: string[]; exitCode: number | null; paths: string[] };

	let devices = $state<Device[]>([]);
	let selectedSerials = $state<string[]>([]);
	let apkPath = $state("");
	let output = $state<string[]>(["ADB & Scrcpy ready. Connect Android devices, then click Refresh."]);
	let busy = $state("");
	let error = $state("");
	let scrcpyReady = $state<boolean | null>(null);
	let turnScreenOff = $state(false);
	let allSelected = $derived(devices.length > 0 && selectedSerials.length === devices.length);

	onMount(() => { void refreshDevices(); });

	async function run<T>(label: string, action: () => Promise<T>, linesFor: (value: T) => string[] = () => []) {
		busy = label;
		error = "";
		output = [`${label}…`];
		try {
			const value = await action();
			output = linesFor(value).length ? linesFor(value) : ["Command completed."];
			return value;
		} catch (cause) {
			error = String(cause);
			output = [String(cause)];
			return null;
		} finally {
			busy = "";
		}
	}

	async function refreshDevices() {
		const next = await run("Refreshing connected devices", () => invoke<Device[]>("get_adb_devices"), (value) => value.map((device) => `${device.model} (${device.serial}) · ${device.state}`));
		if (!next) return;
		devices = next;
		const available = new Set(next.filter((device) => device.state === "device").map((device) => device.serial));
		selectedSerials = selectedSerials.filter((serial) => available.has(serial));
		output = [`Found ${next.filter((device) => device.state === "device").length} ready device(s).`];
	}

	function toggleAll() {
		selectedSerials = allSelected ? [] : devices.filter((device) => device.state === "device").map((device) => device.serial);
	}

	function toggleDevice(serial: string, checked: boolean) {
		selectedSerials = checked ? [...new Set([...selectedSerials, serial])] : selectedSerials.filter((value) => value !== serial);
	}

	async function checkScrcpy() {
		const result = await run("Checking Scrcpy backend", () => invoke<CommandResult>("get_scrcpy_version"), (value) => value.lines);
		if (result) scrcpyReady = result.ok;
	}

	async function mirrorSelected() {
		await run("Starting mirror", () => invoke<CommandResult>("start_scrcpy_mirror", { serials: selectedSerials, turnScreenOff }), (value) => value.lines);
	}

	async function screenshotSelected() {
		await run("Taking screenshot", () => invoke<CommandResult>("take_adb_screenshots", { serials: selectedSerials }), (value) => value.lines);
	}

	async function exportSpecs() {
		await run("Extracting device specs", () => invoke<CommandResult>("export_adb_specs", { serials: selectedSerials }), (value) => value.lines);
	}

	async function pickApk() {
		const selected = await openFile({ filters: [{ name: "Android packages", extensions: ["apk"] }] });
		if (selected) apkPath = selected;
	}

	async function installApk() {
		await run("Installing APK", () => invoke<CommandResult>("install_adb_apk", { apkPath, serials: selectedSerials }), (value) => value.lines);
	}
</script>

<div class="bg-muted/30 h-full min-h-0 overflow-auto">
	<div class="mx-auto flex w-full max-w-5xl flex-col gap-4 p-4">
		<div class="flex flex-wrap items-start justify-between gap-3">
			<div class="flex items-center gap-3"><div class="bg-primary text-primary-foreground grid size-10 place-items-center rounded-lg"><AndroidIcon /></div><div><h1 class="text-xl font-semibold">Android / Scrcpy Dashboard</h1><p class="text-muted-foreground text-sm">ADB device control and debug tools</p></div></div>
			<Button variant="outline" disabled={!!busy} onclick={refreshDevices}><RefreshCwIcon data-icon="inline-start" />Refresh</Button>
		</div>

		<Card.Root>
			<Card.Header><Card.Title>Target devices</Card.Title><Card.Description>Select one or more connected Android devices.</Card.Description><Card.Action><Button size="sm" variant="outline" disabled={!devices.length || !!busy} onclick={toggleAll}>{allSelected ? "Clear" : "All"}</Button></Card.Action></Card.Header>
			<Card.Content class="flex flex-col gap-1">
				{#each devices as device (device.serial)}
					<label class="hover:bg-muted/50 flex items-center gap-3 rounded-md px-3 py-2" class:opacity-50={device.state !== "device"}>
						<Checkbox checked={selectedSerials.includes(device.serial)} disabled={device.state !== "device" || !!busy} onclick={() => toggleDevice(device.serial, !selectedSerials.includes(device.serial))} />
						<div class="min-w-0 flex-1"><p class="truncate text-sm font-medium">{device.model} <span class="text-muted-foreground font-mono text-xs">({device.serial})</span></p><p class="text-muted-foreground text-xs">{device.device} · {device.state}</p></div>
					</label>
				{:else}
					<p class="text-muted-foreground rounded-md border border-dashed p-6 text-center text-sm">No adb devices in state=device.</p>
				{/each}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>Actions</Card.Title><Card.Description>Run tools against the selected devices.</Card.Description></Card.Header>
			<Card.Content class="flex flex-col gap-3">
				<label class="hover:bg-muted/40 flex items-start gap-3 rounded-md border p-3">
					<Checkbox checked={turnScreenOff} disabled={!!busy} onclick={() => (turnScreenOff = !turnScreenOff)} />
					<span class="grid gap-1"><span class="text-sm font-medium">Start with screen off</span><span class="text-muted-foreground text-xs">Keep the Scrcpy mirror active while the device display is off. Scrcpy chooses the video settings automatically.</span></span>
				</label>
				<div class="flex flex-wrap gap-2">
				<Button variant="outline" disabled={!!busy} onclick={checkScrcpy}>{#if busy === "Checking Scrcpy backend"}<LoaderCircleIcon data-icon="inline-start" class="animate-spin" />{:else}<TerminalIcon data-icon="inline-start" />{/if}Check Scrcpy {#if scrcpyReady !== null}<Badge variant={scrcpyReady ? "default" : "destructive"}>{scrcpyReady ? "Ready" : "Missing"}</Badge>{/if}</Button>
				<Button disabled={!!busy || !selectedSerials.length} onclick={mirrorSelected}><PlayIcon data-icon="inline-start" />Mirror selected</Button>
				<Button variant="outline" disabled={!!busy || !selectedSerials.length} onclick={screenshotSelected}><CameraIcon data-icon="inline-start" />Screenshot selected</Button>
				<Button variant="outline" disabled={!!busy || !selectedSerials.length} onclick={exportSpecs}><FileCogIcon data-icon="inline-start" />Export specs</Button>
				</div>
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>Install debug APK</Card.Title><Card.Description>Install the selected APK on every selected device.</Card.Description></Card.Header>
			<Card.Content class="flex flex-col gap-3"><div class="flex gap-2"><Input aria-label="APK path" placeholder="C:\\path\\app-debug.apk" bind:value={apkPath} /><Button variant="outline" disabled={!!busy} onclick={pickApk}><FolderOpenIcon data-icon="inline-start" />Pick</Button></div><Button disabled={!!busy || !selectedSerials.length || !apkPath.trim()} onclick={installApk}><DownloadIcon data-icon="inline-start" />Install to selected</Button></Card.Content>
		</Card.Root>

		<Card.Root class="min-h-48">
			<Card.Header><Card.Title class="flex items-center gap-2"><TerminalIcon />Terminal output</Card.Title><Card.Description>Latest ADB or Scrcpy command output.</Card.Description></Card.Header>
			<Card.Content class="min-h-0"><ScrollArea.Root class="bg-background h-48 rounded-md border"><pre class="whitespace-pre-wrap p-3 font-mono text-xs">{output.join("\n")}</pre><ScrollArea.Scrollbar orientation="vertical" /></ScrollArea.Root>{#if error}<p class="text-destructive mt-2 text-sm" role="alert">{error}</p>{/if}</Card.Content>
		</Card.Root>
	</div>
</div>
