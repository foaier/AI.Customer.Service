import { fileURLToPath } from "node:url";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const sourceDirectory = fileURLToPath(new URL("./src", import.meta.url));

export default defineConfig(({ mode }) => {
    const env = loadEnv(mode, process.cwd(), "");
    const backend = env.VITE_BACKEND_URL || "http://localhost:5180";
    return {
        plugins: [react()],
        test: {
            environment: "jsdom",
            setupFiles: "./src/test/setup.ts",
            restoreMocks: true,
            clearMocks: true,
        },
        resolve: {
            alias: {
                "@": sourceDirectory,
            },
        },
        server: {
            port: 5173,
            strictPort: true,
            proxy: {
                "/api": { target: backend, changeOrigin: true, ws: true, rewriteWsOrigin: true },
                "/auth": { target: backend, changeOrigin: true },
            },
        },
    };
});
