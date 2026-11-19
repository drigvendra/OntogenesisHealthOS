import { Button, Card, DataTable, SelectField, TextField } from "@healthos/ui-kit";
import { useState } from "react";
import { post, type Department, type Organization, type Site } from "../api";
import { QueryState, SubmitError, useApi, useSubmit } from "../common";

const ORGS = "/api/core/tenancy/organizations";
const SITES = "/api/core/tenancy/sites";

export function StructurePage() {
  const orgs = useApi<Organization[]>(ORGS);
  const sites = useApi<Site[]>(SITES);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Organization structure</h1>

      <Card title="Organizations">
        <QueryState error={orgs.error} isPending={orgs.isPending} label="organizations" />
        {orgs.data ? (
          <DataTable caption="Organizations" rows={orgs.data} rowKey={(o) => o.id} empty="No organizations yet."
            columns={[{ header: "Name", cell: (o) => o.name }]} />
        ) : null}
        <NewOrganization />
      </Card>

      <Card title="Sites">
        <QueryState error={sites.error} isPending={sites.isPending} label="sites" />
        {sites.data ? (
          <DataTable caption="Sites" rows={sites.data} rowKey={(s) => s.id} empty="No sites yet."
            columns={[
              { header: "Code", cell: (s) => s.code },
              { header: "Name", cell: (s) => s.name },
              { header: "Organization", cell: (s) => orgs.data?.find((o) => o.id === s.organizationId)?.name ?? "—" },
              { header: "Time zone", cell: (s) => s.timeZone },
            ]} />
        ) : null}
        {orgs.data && orgs.data.length > 0 ? <NewSite organizations={orgs.data} /> : null}
      </Card>

      {sites.data?.map((s) => <SiteDepartments key={s.id} site={s} />)}
    </div>
  );
}

function NewOrganization() {
  const [name, setName] = useState("");
  const s = useSubmit([ORGS]);
  return (
    <form className="mt-4 flex flex-wrap items-end gap-3" onSubmit={(e) => void s.run(e, () => post(ORGS, { name })).then((ok) => ok && setName(""))}>
      <TextField label="New organization name" value={name} onChange={(e) => setName(e.target.value)} error={s.field("name")} required />
      <Button type="submit" disabled={s.busy}>Add organization</Button>
      <SubmitError error={s.error} />
    </form>
  );
}

function NewSite({ organizations }: { organizations: Organization[] }) {
  const [form, setForm] = useState({ organizationId: organizations[0]!.id, code: "", name: "", timeZone: "Asia/Kolkata" });
  const s = useSubmit([SITES]);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });
  return (
    <form className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-5 lg:items-end"
      onSubmit={(e) => void s.run(e, () => post(SITES, form)).then((ok) => ok && setForm({ ...form, code: "", name: "" }))}>
      <SelectField label="Organization" value={form.organizationId} onChange={set("organizationId")} error={s.field("organizationId")}>
        {organizations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
      </SelectField>
      <TextField label="Site code" value={form.code} onChange={set("code")} error={s.field("code")} hint="e.g. MUM-01" required />
      <TextField label="Site name" value={form.name} onChange={set("name")} error={s.field("name")} required />
      <TextField label="Time zone" value={form.timeZone} onChange={set("timeZone")} error={s.field("timeZone")} hint="IANA, e.g. Europe/Berlin" required />
      <Button type="submit" disabled={s.busy}>Add site</Button>
      <div className="lg:col-span-5"><SubmitError error={s.error} /></div>
    </form>
  );
}

function SiteDepartments({ site }: { site: Site }) {
  const path = `${SITES}/${site.id}/departments`;
  const depts = useApi<Department[]>(path);
  const [name, setName] = useState("");
  const s = useSubmit([path]);
  return (
    <Card title={`Departments — ${site.name}`}>
      <QueryState error={depts.error} isPending={depts.isPending} label="departments" />
      {depts.data ? (
        <DataTable caption={`Departments of ${site.name}`} rows={depts.data} rowKey={(d) => d.id} empty="No departments yet."
          columns={[{ header: "Name", cell: (d) => d.name }]} />
      ) : null}
      <form className="mt-4 flex flex-wrap items-end gap-3" onSubmit={(e) => void s.run(e, () => post(path, { name })).then((ok) => ok && setName(""))}>
        <TextField label="New department name" value={name} onChange={(e) => setName(e.target.value)} error={s.field("name")} required />
        <Button type="submit" disabled={s.busy}>Add department</Button>
        <SubmitError error={s.error} />
      </form>
    </Card>
  );
}
