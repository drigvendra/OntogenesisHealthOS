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
    // changeOrigin must stay false: core-api builds the OIDC redirect URI from the Host header, which
    // has to remain localhost:5173 (the origin the browser and Keycloak see).
    proxy: {
      "/api": { target: coreApi, changeOrigin: false },
      "/bff": { target: coreApi, changeOrigin: false },
      "/health": { target: coreApi, changeOrigin: false },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    css: false,
  },
});
