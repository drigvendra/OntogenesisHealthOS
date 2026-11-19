import { Alert, Badge, Button, Card, DataTable, SelectField } from "@healthos/ui-kit";
import { useState } from "react";
import { ApiError, post, type ConfigProfile, type OnboardingResult, type OnboardingTemplate } from "../api";
import { useApi } from "../common";

/** Onboarding wizard: template → country → preview → apply (spec §8 admin flow). */
export function OnboardingWizard({ tenantId }: { tenantId: string }) {
  const templates = useApi<OnboardingTemplate[]>("/api/platform/onboarding/templates");
  const profiles = useApi<ConfigProfile[]>("/api/platform/configuration/profiles");
  const [template, setTemplate] = useState("");
  const [country, setCountry] = useState("IN");
  const [result, setResult] = useState<OnboardingResult | null>(null);
  const [applied, setApplied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const chosen = templates.data?.find((t) => t.code === (template || templates.data?.[0]?.code));
  const countries = profiles.data?.filter((p) => p.scope === "Country") ?? [];

  async function run(action: "preview" | "apply") {
    if (!chosen) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await post<OnboardingResult>(`/api/platform/tenants/${tenantId}/onboarding/${action}`, { template: chosen.code, country, trialDays: 30 }));
      setApplied(action === "apply");
    } catch (e) {
      if (e instanceof ApiError && e.status === 422) setResult(e.body as OnboardingResult);
      else setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Onboarding">
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField label="Customer profile" value={chosen?.code ?? ""} onChange={(e) => { setTemplate(e.target.value); setResult(null); }}>
          {templates.data?.map((t) => <option key={t.code} value={t.code}>{t.displayName}</option>)}
        </SelectField>
        <SelectField label="Country / region" value={country} onChange={(e) => { setCountry(e.target.value); setResult(null); }}>
          {countries.map((c) => <option key={c.key} value={c.key}>{c.displayName}</option>)}
        </SelectField>
      </div>
      {chosen ? (
        <p className="mt-3 text-sm text-text-muted">
          {chosen.description} Modules: {chosen.modules.join(", ")}. Roles: {chosen.roles.join(", ")}. {chosen.forms} form(s), {chosen.workflows} workflow(s). Licences are granted as a 30-day trial.
        </p>
      ) : null}
      <div className="mt-4 flex gap-3">
        <Button variant="secondary" disabled={busy || !chosen} onClick={() => void run("preview")}>Preview</Button>
        <Button disabled={busy || !result?.succeeded || applied} onClick={() => void run("apply")}>Apply</Button>
      </div>
      {error ? <Alert tone="danger" title={error} /> : null}
      {result ? (
        <div className="mt-4 space-y-3">
          {result.problems.length > 0 ? (
            <Alert tone="danger" title="This profile cannot be applied">
              <ul className="list-disc pl-5">{result.problems.map((p) => <li key={p}>{p}</li>)}</ul>
            </Alert>
          ) : applied ? <Alert tone="info" title="Onboarding applied. Appoint a tenant administrator to hand over." /> : null}
          <DataTable caption="Onboarding steps" rows={result.steps} rowKey={(s) => s.kind + s.item} empty="No steps."
            columns={[
              { header: "Step", cell: (s) => s.kind },
              { header: "Item", cell: (s) => s.item },
              { header: "Status", cell: (s) => <Badge tone={s.status === "done" ? "success" : s.status === "failed" ? "danger" : "neutral"}>{s.status}</Badge> },
              { header: "Detail", cell: (s) => s.detail ?? "" },
            ]} />
        </div>
      ) : null}
    </Card>
  );
}
