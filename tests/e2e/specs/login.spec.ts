import { createHmac } from "node:crypto";
import { expect, test } from "@playwright/test";

/** RFC 6238 TOTP as Keycloak computes it (HMAC-SHA1, 30 s, 6 digits, raw text secret). */
function totp(secret: string, now = Date.now()): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(now / 1000 / 30)));
  const h = createHmac("sha1", Buffer.from(secret, "utf8")).update(counter).digest();
  const offset = h[h.length - 1]! & 0xf;
  return ((h.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
}

// Synthetic Keycloak user from deploy/local/keycloak/healthos-dev-realm.json (no TOTP required).
const USERNAME = "synthetic.user";
const PASSWORD = process.env.E2E_PASSWORD ?? "";

test.beforeAll(() => {
  if (!PASSWORD) throw new Error("E2E_PASSWORD is not set");
});

test("API rejects anonymous calls and calls without the anti-forgery header", async ({ request }) => {
  expect((await request.get("/api/core/identity/me", { headers: { "X-CSRF": "1" } })).status()).toBe(401);
  expect((await request.get("/api/core/identity/me")).status()).toBe(403);
});

test("synthetic user signs in through Keycloak via the BFF", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("link", { name: "Sign in" }).click();

  await expect(page).toHaveURL(/localhost:8080\/realms\/healthos-dev\//);
  await page.locator("#username").fill(USERNAME);
  await page.locator("#password").fill(PASSWORD);
  await page.locator("#kc-login").click();

  // On failure, report where the browser ended up and what it shows (Keycloak error, callback error, ...).
  try {
    await page.waitForURL(/localhost:5173\/(?!bff\/)/, { timeout: 20_000 });
    await expect(page.getByRole("heading", { name: "Welcome, Synthetic User" })).toBeVisible({ timeout: 15_000 });
  } catch (e) {
    const text = (await page.locator("body").innerText().catch(() => "")).replace(/\s+/g, " ").slice(0, 400);
    throw new Error(`Login did not complete. URL: ${page.url()} | Page: ${text} | ${(e as Error).message.split("\n")[0]}`);
  }
  await expect(page.getByText("Multi-factor authentication not used")).toBeVisible();

  // Tokens never reach the browser: the session cookie is HttpOnly and invisible to page scripts.
  const session = (await page.context().cookies()).find((c) => c.name === "healthos.session");
  expect(session?.httpOnly).toBe(true);
  expect(await page.evaluate(() => document.cookie)).not.toContain("healthos.session");

  // The session carries the internal user id created by just-in-time provisioning.
  const me = await page.evaluate(async () => {
    const res = await fetch("/api/core/identity/me", { headers: { "X-CSRF": "1" } });
    return (await res.json()) as { userId: string | null; subjectId: string };
  });
  expect(me.userId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  expect(me.subjectId).not.toBe("");
});

test("MFA sign-in is recognised by HealthOS", async ({ page }) => {
  const secret = process.env.E2E_TOTP_SECRET ?? "";
  test.skip(!secret, "E2E_TOTP_SECRET not set");

  await page.goto("/");
  await page.getByRole("link", { name: "Sign in" }).click();
  await page.locator("#username").fill("synthetic.admin");
  await page.locator("#password").fill(PASSWORD);
  await page.locator("#kc-login").click();

  await page.locator("#otp").fill(totp(secret));
  await page.locator("#kc-login").click();

  try {
    await page.waitForURL(/localhost:5173\/(?!bff\/)/, { timeout: 20_000 });
    await expect(page.getByRole("heading", { name: "Welcome, Synthetic Admin" })).toBeVisible({ timeout: 15_000 });
  } catch (e) {
    const text = (await page.locator("body").innerText().catch(() => "")).replace(/\s+/g, " ").slice(0, 400);
    throw new Error(`MFA login did not complete. URL: ${page.url()} | Page: ${text} | ${(e as Error).message.split("\n")[0]}`);
  }

  const me = await page.evaluate(async () => {
    const res = await fetch("/api/core/identity/me", { headers: { "X-CSRF": "1" } });
    return (await res.json()) as { multiFactorAuthenticated: boolean; authenticationMethods: string[] };
  });
  expect(me.multiFactorAuthenticated, `amr was: ${JSON.stringify(me.authenticationMethods)}`).toBe(true);
  await expect(page.getByText("Multi-factor authentication not used")).toHaveCount(0);
});