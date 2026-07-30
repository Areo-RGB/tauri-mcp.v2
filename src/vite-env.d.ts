/// <reference types="vite/client" />

interface Window {
  mcphub?: {
    invoke<T>(command: string, args?: Record<string, unknown>): Promise<T>;
    openFile(options?: {
      filters?: Array<{ name: string; extensions: string[] }>;
    }): Promise<string | null>;
  };
}
