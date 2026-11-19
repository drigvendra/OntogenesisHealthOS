import { Badge, Button, Card, DataTable, SelectField, TextField } from "@healthos/ui-kit";
import { useState } from "react";
import { Link, useParams } from "react-router";
import { post, put, type Entitlement, type LicensableFeature, type TenantSummary } from "../api";
import { QueryState, SubmitError, useApi, useSubmit } from "../common";
import { EntitlementTable } from "./Licenses";

const TENANTS = "/api/platform/tenants";

export function PlatformTenantsPage() {
  const tenants = useApi<TenantSummary[]>(TENANTS);
  const [form, setForm] = useState({ code: "", name: "" });
  const s = useSubmit([TENANTS]);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Tenants</h1>
      <Card title="Tenants in this cell">
        <QueryState error={tenants.error} isPending={tenants.isPending} label="tenants" />
        {tenants.data ? (
          <DataTable caption="Tenants" rows={tenants.data} rowKey={(t) => t.id} empty="No tenants yet."
            columns={[
              { header: "Name", cell: (t) => <Link className="underline" to={`/platform/tenants/${t.id}`}>{t.name}</Link> },
              { header: "Code", cell: (t) => t.code },
              { header: "Cell", cell: (t) => t.regionCell },
              { header: "Status", cell: (t) => <Badge tone={t.status === "active" ? "success" : "warning"}>{t.status}</Badge> },
            ]} />
        ) : null}
        <form className="mt-4 flex flex-wrap items-end gap-3"
          onSubmit={(e) => void s.run(e, () => post(TENANTS, form)).then((ok) => ok && setForm({ code: "", name: "" }))}>
          <TextField label="Tenant code" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} error={s.field("code")} hint="e.g. sunrise-clinic" required />
          <TextField label="Tenant name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} error={s.field("name")} required />
          <Button type="submit" disabled={s.busy}>Create tenant</Button>
          <SubmitError error={s.error} />
        </form>
      </Card>
    </div>
  );
}

export function PlatformTenantPage() {
  const { tenantId } = useParams();
  const tenants = useApi<TenantSummary[]>(TENANTS);
  const tenant = tenants.data?.find((t) => t.id === tenantId);
  const entPath = `${TENANTS}/${tenantId}/entitlements`;
  const ents = useApi<Entitlement[]>(entPath);
  const status = useSubmit([TENANTS]);

  if (!tenant) {
    return tenants.isPending || tenants.error
      ? <QueryState error={tenants.error} isPending={tenants.isPending} label="tenant" />
      : <p>Tenant not found.</p>;
  }

  const suspended = tenant.status !== "active";
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">{tenant.name}</h1>
      <Card title="Status"
        actions={
          <Button variant="secondary" disabled={status.busy}
            onClick={() => void status.run(null, () => post(`${TENANTS}/${tenant.id}/${suspended ? "activate" : "suspend"}`, {}))}>
            {suspended ? "Reactivate tenant" : "Suspend tenant"}
          </Button>
        }>
        <p className="text-sm">
          <Badge tone={suspended ? "warning" : "success"}>{tenant.status}</Badge>{" "}
          {suspended ? "Members cannot access this tenant. Data is retained." : `Code ${tenant.code}, cell ${tenant.regionCell}.`}
        </p>
        <SubmitError error={status.error} />
      </Card>
      <AppointAdmin tenantId={tenant.id} />
      <Card title="Entitlements">
        <QueryState error={ents.error} isPending={ents.isPending} label="entitlements" />
        {ents.data ? <EntitlementTable rows={ents.data} /> : null}
        <GrantEntitlement path={entPath} />
      </Card>
    </div>
  );
}

function AppointAdmin({ tenantId }: { tenantId: string }) {
  const [email, setEmail] = useState("");
  const [done, setDone] = useState(false);
  const s = useSubmit([]);
  return (
    <Card title="Tenant administrator">
      <form className="flex flex-wrap items-end gap-3"
        onSubmit={(e) => void s.run(e, () => post(`${TENANTS}/${tenantId}/admins`, { email })).then((ok) => { setDone(ok); if (ok) setEmail(""); })}>
        <TextField label="Appoint administrator (email)" type="email" value={email} onChange={(e) => setEmail(e.target.value)} error={s.field("email")}
          hint="The person must have signed in once." required />
        <Button type="submit" disabled={s.busy}>Appoint</Button>
        <SubmitError error={s.error} />
        {done ? <p role="status" className="text-sm">Administrator appointed.</p> : null}
      </form>
    </Card>
  );
}

function GrantEntitlement({ path }: { path: string }) {
  const features = useApi<LicensableFeature[]>("/api/platform/licensing/features");
  const [feature, setFeature] = useState("");
  const [validTo, setValidTo] = useState("");
  const s = useSubmit([path]);

  if (features.data && features.data.length === 0) {
    return <p className="mt-4 text-sm text-text-muted">No licensable product modules are installed in this deployment yet.</p>;
  }

  return (
    <form className="mt-4 flex flex-wrap items-end gap-3"
      onSubmit={(e) => void s.run(e, () => put(path, { feature: feature || features.data?.[0]?.feature, validTo: validTo ? new Date(validTo).toISOString() : null }))}>
      <SelectField label="Feature" value={feature} onChange={(e) => setFeature(e.target.value)} error={s.field("feature")}>
        {features.data?.map((f) => <option key={f.feature} value={f.feature}>{f.displayName} ({f.feature})</option>)}
      </SelectField>
      <TextField label="Valid until (optional)" type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} error={s.field("validTo")} />
      <Button type="submit" disabled={s.busy}>Grant / update</Button>
      <SubmitError error={s.error} />
    </form>
  );
}
