import { defineConfig, devices } from "@playwright/test";

// Runs against an already started stack (CI e2e job or scripts/dev-up.ps1 + core-api + admin-web).
export default defineConfig({
  testDir: "./specs",
  timeout: 60_000,
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
