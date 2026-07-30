export type InvokeArgs = Record<string, unknown>;

export function invoke<T>(command: string, args: InvokeArgs = {}): Promise<T> {
  if (!window.mcphub) {
    return Promise.reject(new Error("The Electron desktop bridge is unavailable."));
  }
  return window.mcphub.invoke<T>(command, args);
}

export async function openFile(options: {
  filters?: Array<{ name: string; extensions: string[] }>;
}): Promise<string | null> {
  if (!window.mcphub) {
    throw new Error("The Electron desktop bridge is unavailable.");
  }
  return window.mcphub.openFile(options);
}
