import { Alert, Badge, Button, Card, DataTable } from "@healthos/ui-kit";
import { useState } from "react";
import { api, type AuditEvent, type AuditVerification } from "../api";
import { formatDate, QueryState, useApi } from "../common";

export function AuditPage({ scope }: { scope: "tenant" | "platform" }) {
  const base = scope === "tenant" ? "/api/core/audit" : "/api/platform/audit";
  const events = useApi<AuditEvent[]>(`${base}/events?limit=200`);
  const [verification, setVerification] = useState<AuditVerification | null>(null);
  const [checking, setChecking] = useState(false);

  async function verify() {
    setChecking(true);
    try {
      setVerification(await api<AuditVerification>(`${base}/verify`));
    } finally {
      setChecking(false);
    }
  }

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">{scope === "tenant" ? "Audit log" : "Platform audit log"}</h1>
      <Card title="Integrity" actions={<Button variant="secondary" onClick={() => void verify()} disabled={checking}>Verify chain</Button>}>
        {verification === null ? (
          <p className="text-sm text-text-muted">Every entry is hash-chained to the previous one. Verification recomputes the whole chain.</p>
        ) : verification.intact ? (
          <Alert tone="info" title="Audit chain verified: no tampering detected." />
        ) : (
          <Alert tone="danger" title={`Audit chain broken at entry #${verification.firstInvalidSeq}`}>
            Entries from this point cannot be trusted. Report this to your security officer immediately.
          </Alert>
        )}
      </Card>
      <Card title="Recent events">
        <QueryState error={events.error} isPending={events.isPending} label="audit events" />
        {events.data ? (
          <DataTable caption="Audit events" rows={events.data} rowKey={(e) => String(e.seq)} empty="No events yet."
            columns={[
              { header: "#", cell: (e) => e.seq },
              { header: "When", cell: (e) => formatDate(e.occurredAt) },
              { header: "Action", cell: (e) => e.action },
              { header: "Resource", cell: (e) => `${e.resourceType}${e.resourceId ? " " + e.resourceId.slice(0, 8) : ""}` },
              { header: "Outcome", cell: (e) => <Badge tone={e.outcome === "success" ? "success" : "danger"}>{e.outcome}</Badge> },
              { header: "Actor", cell: (e) => (e.actorUserId ? e.actorUserId.slice(0, 8) : "system") },
            ]} />
        ) : null}
      </Card>
    </div>
  );
}
