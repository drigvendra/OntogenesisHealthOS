import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, AppShell, Button, type NavItem } from "@healthos/ui-kit";
import { BrowserRouter, Link, Navigate, Route, Routes, useLocation } from "react-router";
import { fetchSession, loginUrl, logout, put, type CurrentUser, type TenantSummary } from "./api";
import { useApi } from "./common";
import { HomePage } from "./pages/Home";
import { StructurePage } from "./pages/Structure";
import { MembersPage } from "./pages/Members";
import { LicensesPage } from "./pages/Licenses";
import { AuditPage } from "./pages/Audit";
import { PlatformTenantsPage, PlatformTenantPage } from "./pages/PlatformTenants";

export function App() {
  const session = useQuery({ queryKey: ["session"], queryFn: fetchSession, retry: false });

  if (session.isPending) {
    return (
      <AppShell productName="HealthOS Admin" nav={[]}>
        <p role="status">Loading…</p>
      </AppShell>
    );
  }

  if (session.isError || session.data.kind === "error") {
    return (
      <AppShell productName="HealthOS Admin" nav={[]}>
        <Alert tone="danger" title="HealthOS is unavailable">
          The platform API could not be reached. Try again shortly or contact your administrator.
        </Alert>
      </AppShell>
    );
  }

  if (session.data.kind === "anonymous") {
    return (
      <AppShell productName="HealthOS Admin" nav={[]}>
        <h1 className="mb-4 text-2xl font-semibold">Sign in</h1>
        <p className="mb-6 text-text-muted">Use your organization account to continue.</p>
        <a
          href={loginUrl(window.location.pathname)}
          className="inline-flex rounded-md bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
        >
          Sign in
        </a>
      </AppShell>
    );
  }

  return (
    <BrowserRouter>
      <SignedIn user={session.data.user} />
    </BrowserRouter>
  );
}

function SignedIn({ user }: { user: CurrentUser }) {
  const location = useLocation();
  const tenants = useApi<TenantSummary[]>("/api/core/identity/me/tenants");
  const inTenant = user.activeTenantId !== null;

  const nav: NavItem[] = [{ label: "Home", href: "/" }];
  if (inTenant) {
    nav.push(
      { label: "Organization", href: "/structure" },
      { label: "Members & roles", href: "/members" },
      { label: "Licenses", href: "/licenses" },
      { label: "Audit log", href: "/audit" },
    );
  }
  if (user.isPlatformOperator) {
    nav.push({ label: "Platform: tenants", href: "/platform/tenants" }, { label: "Platform: audit", href: "/platform/audit" });
  }
  for (const item of nav) {
    item.current = item.href === "/" ? location.pathname === "/" : location.pathname.startsWith(item.href);
  }

  return (
    <AppShell
      productName="HealthOS Admin"
      nav={nav}
      renderLink={(item, className) => (
        <Link to={item.href} aria-current={item.current ? "page" : undefined} className={className}>
          {item.label}
        </Link>
      )}
      userSlot={
        <div className="flex flex-wrap items-center gap-3">
          <TenantSwitcher user={user} tenants={tenants.data ?? []} />
          <span className="text-sm">{user.displayName ?? user.subjectId}</span>
          <Button variant="secondary" onClick={() => void logout()}>
            Sign out
          </Button>
        </div>
      }
    >
      <Routes>
        <Route path="/" element={<HomePage user={user} tenants={tenants.data ?? []} />} />
        {inTenant ? (
          <>
            <Route path="/structure" element={<StructurePage />} />
            <Route path="/members" element={<MembersPage />} />
            <Route path="/licenses" element={<LicensesPage />} />
            <Route path="/audit" element={<AuditPage scope="tenant" />} />
          </>
        ) : null}
        {user.isPlatformOperator ? (
          <>
            <Route path="/platform/tenants" element={<PlatformTenantsPage />} />
            <Route path="/platform/tenants/:tenantId" element={<PlatformTenantPage />} />
            <Route path="/platform/audit" element={<AuditPage scope="platform" />} />
          </>
        ) : null}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AppShell>
  );
}

function TenantSwitcher({ user, tenants }: { user: CurrentUser; tenants: TenantSummary[] }) {
  const qc = useQueryClient();
  if (tenants.length === 0) return null;

  async function select(tenantId: string) {
    await put("/api/core/identity/session/tenant", { tenantId });
    await qc.invalidateQueries();
  }

  return (
    <label className="flex items-center gap-2 text-sm">
      <span>Tenant</span>
      <select
        className="rounded-md border border-border bg-surface-raised px-2 py-1"
        value={user.activeTenantId ?? ""}
        onChange={(e) => void select(e.target.value)}
      >
        {user.activeTenantId === null ? <option value="">Choose…</option> : null}
        {tenants.map((t) => (
          <option key={t.id} value={t.id}>
            {t.name}
          </option>
        ))}
      </select>
    </label>
  );
}
