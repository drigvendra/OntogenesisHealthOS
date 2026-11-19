import { Badge, Button, Card, DataTable, SelectField, TextField } from "@healthos/ui-kit";
import { useState } from "react";
import { post, type DefinitionSummary, type Draft, type ResolvedSetting, type SettingDefinition, type Snapshot } from "../api";
import { formatDate, QueryState, SubmitError, useApi, useSubmit } from "../common";

const BASE = "/api/core/configuration";

function show(v: unknown): string {
  return typeof v === "string" ? v : JSON.stringify(v);
}

export function ConfigurationPage() {
  const effective = useApi<ResolvedSetting[]>(`${BASE}/effective`);
  const forms = useApi<DefinitionSummary[]>(`${BASE}/forms`);
  const workflows = useApi<DefinitionSummary[]>(`${BASE}/workflows`);
  const [open, setOpen] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Configuration</h1>

      <Card title="Settings that apply to you">
        <QueryState error={effective.error} isPending={effective.isPending} label="settings" />
        {effective.data ? (
          <DataTable caption="Effective settings" rows={effective.data} rowKey={(s) => s.key} empty="No settings."
            columns={[
              { header: "Setting", cell: (s) => s.key },
              { header: "Value", cell: (s) => <strong>{show(s.value)}</strong> },
              { header: "Set at", cell: (s) => <>{s.source}{s.sourceKey && s.sourceKey !== "default" ? ` (${s.sourceKey})` : ""} {s.lockedAt ? <Badge tone="warning">locked at {s.lockedAt}</Badge> : null}</> },
              { header: "Why", cell: (s) => (
                <div>
                  <Button variant="secondary" aria-expanded={open === s.key} onClick={() => setOpen(open === s.key ? null : s.key)}>{open === s.key ? "Hide" : "Explain"}</Button>
                  {open === s.key ? (
                    <ol className="mt-2 list-decimal pl-5 text-xs">
                      {s.trace.map((t, i) => <li key={i}>{t.scope}{t.scopeKey ? ` ${t.scopeKey}` : ""}: {t.effect}{t.value ? ` → ${t.value}` : ""}</li>)}
                    </ol>
                  ) : null}
                </div>
              ) },
            ]} />
        ) : null}
      </Card>

      <ChangeSettings />

      <Card title="Forms and workflows">
        <DataTable caption="Forms" rows={[...(forms.data ?? []).map((f) => ({ ...f, kind: "Form" })), ...(workflows.data ?? []).map((w) => ({ ...w, kind: "Workflow" }))]}
          rowKey={(d) => d.kind + d.code} empty="No forms or workflows yet. They are added by onboarding templates or published by administrators."
          columns={[
            { header: "Type", cell: (d) => d.kind },
            { header: "Title", cell: (d) => d.title },
            { header: "Version", cell: (d) => `v${d.version}` },
            { header: "Source", cell: (d) => d.source },
            { header: "Published", cell: (d) => formatDate(d.createdAt) },
          ]} />
      </Card>
    </div>
  );
}

function ChangeSettings() {
  const definitions = useApi<SettingDefinition[]>(`${BASE}/definitions`);
  const drafts = useApi<Draft[]>(`${BASE}/drafts`);
  const snapshots = useApi<Snapshot[]>(`${BASE}/snapshots`);
  const s = useSubmit([`${BASE}/drafts`, `${BASE}/snapshots`, `${BASE}/effective`]);
  const [form, setForm] = useState({ key: "", scope: "Organization", scopeKey: "", value: "", summary: "" });

  const tenantScopes = ["Organization", "Site", "Role", "User"];
  const def = definitions.data?.find((d) => d.key === (form.key || definitions.data?.[0]?.key));
  const parse = (v: string): unknown => (def?.type === "Integer" || def?.type === "Decimal" ? Number(v) : def?.type === "Boolean" ? v === "true" : v);

  return (
    <Card title="Change settings">
      <form className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3"
        onSubmit={(e) => void s.run(e, () => post(`${BASE}/drafts`, {
          summary: form.summary,
          entries: [{ key: def?.key, scope: form.scope, scopeKey: form.scopeKey, value: parse(form.value) }],
        }))}>
        <SelectField label="Setting" value={def?.key ?? ""} onChange={(e) => setForm({ ...form, key: e.target.value })}>
          {definitions.data?.map((d) => <option key={d.key} value={d.key}>{d.key}</option>)}
        </SelectField>
        <SelectField label="Level" value={form.scope} onChange={(e) => setForm({ ...form, scope: e.target.value })}>
          {tenantScopes.filter((x) => def?.allowedScopes.includes(x) ?? true).map((x) => <option key={x}>{x}</option>)}
        </SelectField>
        <TextField label="Applies to (organization / site / role code / user id)" value={form.scopeKey} onChange={(e) => setForm({ ...form, scopeKey: e.target.value })} required />
        <TextField label={`Value${def?.unit ? ` (${def.unit})` : ""}`} value={form.value} onChange={(e) => setForm({ ...form, value: e.target.value })}
          hint={def ? `${def.description}${def.options?.length ? ` Options: ${def.options.join(", ")}` : ""}` : undefined} required />
        <TextField label="Reason for change" value={form.summary} onChange={(e) => setForm({ ...form, summary: e.target.value })} required />
        <div className="flex items-end"><Button type="submit" disabled={s.busy}>Create draft</Button></div>
      </form>
      <SubmitError error={s.error} />

      <h3 className="mt-6 font-medium">Drafts</h3>
      <DataTable caption="Drafts" rows={drafts.data ?? []} rowKey={(d) => d.id} empty="No drafts."
        columns={[
          { header: "Change", cell: (d) => d.summary },
          { header: "Status", cell: (d) => <Badge tone={d.status === "published" ? "success" : "neutral"}>{d.status}</Badge> },
          { header: "Created", cell: (d) => formatDate(d.createdAt) },
          { header: "Action", cell: (d) => d.status === "draft"
              ? <Button variant="secondary" disabled={s.busy} onClick={() => void s.run(null, () => post(`${BASE}/drafts/${d.id}/approve`, {}))}>Approve</Button>
              : d.status === "approved"
                ? <Button disabled={s.busy} onClick={() => void s.run(null, () => post(`${BASE}/drafts/${d.id}/publish`, {}))}>Publish</Button>
                : null },
        ]} />

      <h3 className="mt-6 font-medium">Published versions</h3>
      <DataTable caption="Published versions" rows={snapshots.data ?? []} rowKey={(x) => String(x.version)} empty="Nothing published yet: profile defaults apply."
        columns={[
          { header: "Version", cell: (x) => `v${x.version}${x.rolledBackTo !== null ? ` (rollback to v${x.rolledBackTo})` : ""}` },
          { header: "Entries", cell: (x) => x.entryCount },
          { header: "Published", cell: (x) => formatDate(x.createdAt) },
          { header: "Action", cell: (x) => <Button variant="secondary" disabled={s.busy} onClick={() => void s.run(null, () => post(`${BASE}/rollback`, { toVersion: x.version - 1 }))}>Roll back to v{x.version - 1}</Button> },
        ]} />
    </Card>
  );
}
