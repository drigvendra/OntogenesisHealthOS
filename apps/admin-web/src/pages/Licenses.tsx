import { Badge, Card, DataTable } from "@healthos/ui-kit";
import type { Entitlement } from "../api";
import { formatDate, QueryState, useApi } from "../common";

export function EntitlementTable({ rows }: { rows: Entitlement[] }) {
  return (
    <DataTable caption="Entitlements" rows={rows} rowKey={(e) => e.feature} empty="No licensed features."
      columns={[
        { header: "Feature", cell: (e) => e.feature },
        { header: "Status", cell: (e) => <Badge tone={e.state === "Active" ? "success" : "warning"}>{e.state === "ReadOnly" ? "Expired — read-only" : e.state}</Badge> },
        { header: "Valid from", cell: (e) => formatDate(e.validFrom) },
        { header: "Valid to", cell: (e) => (e.validTo ? formatDate(e.validTo) : "Open-ended") },
        { header: "Grace (days)", cell: (e) => e.graceDays },
        { header: "Source", cell: (e) => e.source },
      ]} />
  );
}

export function LicensesPage() {
  const ents = useApi<Entitlement[]>("/api/core/licensing/entitlements");
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Licenses</h1>
      <Card title="Licensed features">
        <QueryState error={ents.error} isPending={ents.isPending} label="licenses" />
        {ents.data ? <EntitlementTable rows={ents.data} /> : null}
        <p className="mt-4 text-sm text-text-muted">
          When a license expires, its data stays available read-only for the retention period. Contact Ontogenesis to renew.
        </p>
      </Card>
    </div>
  );
}
