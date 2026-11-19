import { Alert, Card } from "@healthos/ui-kit";
import type { CurrentUser, NavigationItem, TenantSummary } from "../api";
import { useApi } from "../common";

export function HomePage({ user, tenants }: { user: CurrentUser; tenants: TenantSummary[] }) {
  const active = tenants.find((t) => t.id === user.activeTenantId);
  const nav = useApi<NavigationItem[]>(user.activeTenantId ? "/api/core/navigation?appName=admin-web" : null);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Welcome, {user.displayName ?? "user"}</h1>

      {!user.multiFactorAuthenticated ? (
        <Alert tone="warning" title="Multi-factor authentication not used">
          This session was signed in without a second factor. Administrative actions will require MFA.
        </Alert>
      ) : null}

      {active ? (
        <Card title="Active tenant">
          <p>
            <strong>{active.name}</strong> <span className="text-text-muted">({active.code}, cell {active.regionCell})</span>
          </p>
        </Card>
      ) : tenants.length > 0 ? (
        <Alert tone="info" title="Choose a tenant">
          You belong to {tenants.length} tenant{tenants.length > 1 ? "s" : ""}. Use the tenant selector at the top to continue.
        </Alert>
      ) : !user.isPlatformOperator ? (
        <Alert tone="info" title="No tenant access yet">
          Your account is not a member of any organization. Ask your administrator to add you.
        </Alert>
      ) : null}

      {nav.data && nav.data.length > 0 ? (
        <Card title="Licensed modules">
          <ul className="list-inside list-disc text-sm">
            {nav.data.map((n) => (
              <li key={n.moduleId + n.path}>
                {n.moduleName}
                {n.readOnly ? " (license expired — read-only)" : ""}
              </li>
            ))}
          </ul>
        </Card>
      ) : null}
    </div>
  );
}
