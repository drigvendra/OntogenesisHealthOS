import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// core-api runs on http://localhost:5080 in development. The SPA and API share one origin through
// this proxy so the BFF session cookie is first-party and SameSite=Lax works.
const coreApi = process.env.HEALTHOS_CORE_API ?? "http://localhost:5080";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": coreApi,
      "/bff": coreApi,
      "/health": coreApi,
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    css: false,
  },
});
