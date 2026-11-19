import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import axe from "axe-core";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

type Handler = (init?: RequestInit) => { status: number; body?: unknown };

/** URL-aware fetch mock: unknown routes return 404 so tests notice unexpected calls. */
function mockApi(routes: Record<string, Handler>) {
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const key = Object.keys(routes).find((k) => url === k || url.startsWith(k + "?"));
    const { status, body } = key ? routes[key]!(init) : { status: 404 };
    return new Response(body === undefined ? null : JSON.stringify(body), { status });
  });
  vi.stubGlobal("fetch", fn);
  return fn;
}

const user = (overrides: Record<string, unknown> = {}) => ({
  userId: "u-1",
  subjectId: "synthetic-1",
  displayName: "Synthetic Admin",
  email: null,
  authenticationMethods: ["pwd"],
  multiFactorAuthenticated: false,
  activeTenantId: null,
  isPlatformOperator: false,
  ...overrides,
});

const tenant = { id: "t-1", code: "sunrise", name: "Sunrise Clinic", regionCell: "IN-1", status: "active" };

function renderApp() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  );
}

async function expectNoAxeViolations(container: HTMLElement) {
  const results = await axe.run(container, { rules: { "color-contrast": { enabled: false } } });
  expect(results.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.pushState({}, "", "/");
});

describe("App", () => {
  it("shows sign-in for anonymous users and links to the BFF login", async () => {
    mockApi({ "/api/core/identity/me": () => ({ status: 401 }) });
    const { container } = renderApp();

    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(link.getAttribute("href")).toMatch(/^\/bff\/login\?returnUrl=/);
    await expectNoAxeViolations(container);
  });

  it("sends the anti-forgery header on API calls", async () => {
    const fetch = mockApi({ "/api/core/identity/me": () => ({ status: 401 }) });
    renderApp();
    await screen.findByRole("link", { name: "Sign in" });
    expect(new Headers(fetch.mock.calls[0]?.[1]?.headers).get("X-CSRF")).toBe("1");
  });

  it("shows the signed-in user, warns about missing MFA and asks to choose a tenant", async () => {
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user() }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [tenant] }),
    });
    const { container } = renderApp();

    expect(await screen.findByRole("heading", { name: "Welcome, Synthetic Admin" })).toBeInTheDocument();
    expect(screen.getByText("Multi-factor authentication not used")).toBeInTheDocument();
    expect(await screen.findByText("Choose a tenant")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Organization" })).not.toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it("shows tenant navigation only with an active tenant, and platform pages only for operators", async () => {
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user({ activeTenantId: "t-1" }) }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [tenant] }),
      "/api/core/navigation": () => ({ status: 200, body: [] }),
    });
    renderApp();

    expect(await screen.findByRole("link", { name: "Organization" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Platform: tenants" })).not.toBeInTheDocument();
  });

  it("shows server validation errors next to the field", async () => {
    window.history.pushState({}, "", "/structure");
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user({ activeTenantId: "t-1", multiFactorAuthenticated: true }) }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [tenant] }),
      "/api/core/tenancy/sites": () => ({ status: 200, body: [] }),
      "/api/core/tenancy/organizations": (init) =>
        init?.method === "POST"
          ? { status: 400, body: { title: "Validation failed", errors: { name: ["Name is required (max 200 characters)."] } } }
          : { status: 200, body: [] },
    });
    const { container } = renderApp();

    const field = await screen.findByLabelText("New organization name");
    fireEvent.change(field, { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Add organization" }));

    await waitFor(() => expect(screen.getByText("Name is required (max 200 characters).")).toBeInTheDocument());
    expect(field).toHaveAttribute("aria-invalid", "true");
    await expectNoAxeViolations(container);
  });

  it("explains permission denials in plain language", async () => {
    window.history.pushState({}, "", "/structure");
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user({ activeTenantId: "t-1" }) }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [tenant] }),
      "/api/core/tenancy/sites": () => ({ status: 200, body: [] }),
      "/api/core/tenancy/organizations": (init) => (init?.method === "POST" ? { status: 403 } : { status: 200, body: [] }),
    });
    renderApp();

    fireEvent.change(await screen.findByLabelText("New organization name"), { target: { value: "Org" } });
    fireEvent.click(screen.getByRole("button", { name: "Add organization" }));
    expect(await screen.findByText(/multi-factor authentication/i, { selector: "p" })).toBeInTheDocument();
  });

  it("shows an error state when the API is unavailable", async () => {
    mockApi({ "/api/core/identity/me": () => ({ status: 503 }) });
    renderApp();
    expect(await screen.findByRole("alert")).toHaveTextContent("HealthOS is unavailable");
  });
});
