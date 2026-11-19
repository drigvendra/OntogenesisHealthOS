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

  it("offers to enable required modules when a pack needs its parent", async () => {
    window.history.pushState({}, "", "/modules");
    const calls: string[] = [];
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user({ activeTenantId: "t-1", multiFactorAuthenticated: true }) }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [tenant] }),
      "/api/core/modules": () => ({ status: 200, body: [
        { id: "clinicos.core", displayName: "ClinicOS", kind: "product", version: "0.1.0", enabled: false, state: "Inactive", reasons: [{ code: "NOT_ENABLED", message: "Not enabled for this tenant." }], licenseFeature: "CLINICOS:core" },
        { id: "specialty.dental", displayName: "Dental pack", kind: "pack", version: "0.1.0", enabled: false, state: "Inactive", reasons: [], licenseFeature: "DENTAL:pack" },
      ] }),
      "/api/core/modules/specialty.dental/enable": (init) => {
        calls.push(String(init?.body));
        return calls.length === 1
          ? { status: 409, body: { title: "Other modules must be enabled first.", reasons: [], required: ["clinicos.core", "specialty.dental"] } }
          : { status: 200, body: [] };
      },
    });
    const { container } = renderApp();

    expect(await screen.findByText("Not enabled for this tenant.")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Enable" })[1]!);
    expect(await screen.findByText(/also enables: clinicos.core/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Enable all" }));
    await waitFor(() => expect(calls[1]).toContain('"includeDependencies":true'));
    await expectNoAxeViolations(container);
  });

  it("shows onboarding preview problems before anything is applied", async () => {
    window.history.pushState({}, "", "/platform/tenants/t-9");
    mockApi({
      "/api/core/identity/me": () => ({ status: 200, body: user({ isPlatformOperator: true, multiFactorAuthenticated: true }) }),
      "/api/core/identity/me/tenants": () => ({ status: 200, body: [] }),
      "/api/platform/tenants": () => ({ status: 200, body: [{ ...tenant, id: "t-9" }] }),
      "/api/platform/tenants/t-9/entitlements": () => ({ status: 200, body: [] }),
      "/api/platform/licensing/features": () => ({ status: 200, body: [] }),
      "/api/platform/configuration/profiles": () => ({ status: 200, body: [{ scope: "Country", key: "IN", displayName: "India", note: "" }] }),
      "/api/platform/onboarding/templates": () => ({ status: 200, body: [
        { code: "dental-clinic", displayName: "Dental clinic", description: "Dental.", industry: "clinic", specialty: "dental", modules: ["clinicos.core", "specialty.dental"], roles: ["dentist"], forms: 1, workflows: 1 },
      ] }),
      "/api/platform/tenants/t-9/onboarding/preview": () => ({ status: 200, body: {
        template: "dental-clinic", succeeded: false, problems: ["Module 'specialty.dental' is not installed in this deployment (bundle)."],
        steps: [{ kind: "module", item: "clinicos.core", status: "planned", detail: null }],
      } }),
    });
    renderApp();

    fireEvent.click(await screen.findByRole("button", { name: "Preview" }));
    expect(await screen.findByText(/not installed in this deployment/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Apply" })).toBeDisabled();
  });

  it("shows an error state when the API is unavailable", async () => {
    mockApi({ "/api/core/identity/me": () => ({ status: 503 }) });
    renderApp();
    expect(await screen.findByRole("alert")).toHaveTextContent("HealthOS is unavailable");
  });
});
