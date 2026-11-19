import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import axe from "axe-core";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

function mockFetch(status: number, body?: unknown) {
  const fn = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
    new Response(body === undefined ? null : JSON.stringify(body), { status }),
  );
  vi.stubGlobal("fetch", fn);
  return fn;
}

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

afterEach(() => vi.unstubAllGlobals());

describe("App", () => {
  it("shows sign-in for anonymous users and links to the BFF login", async () => {
    mockFetch(401);
    const { container } = renderApp();

    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(link.getAttribute("href")).toMatch(/^\/bff\/login\?returnUrl=/);
    await expectNoAxeViolations(container);
  });

  it("sends the anti-forgery header on API calls", async () => {
    const fetch = mockFetch(401);
    renderApp();
    await screen.findByRole("link", { name: "Sign in" });

    const init = fetch.mock.calls[0]?.[1];
    expect(new Headers(init?.headers).get("X-CSRF")).toBe("1");
  });

  it("shows the signed-in user and warns when MFA was not used", async () => {
    mockFetch(200, {
      subjectId: "synthetic-1",
      displayName: "Synthetic Admin",
      email: null,
      authenticationMethods: ["pwd"],
      multiFactorAuthenticated: false,
    });
    const { container } = renderApp();

    expect(await screen.findByRole("heading", { name: "Welcome, Synthetic Admin" })).toBeInTheDocument();
    expect(screen.getByText("Multi-factor authentication not used")).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it("shows an error state when the API is unavailable", async () => {
    mockFetch(503);
    renderApp();
    expect(await screen.findByRole("alert")).toHaveTextContent("HealthOS is unavailable");
  });
});
