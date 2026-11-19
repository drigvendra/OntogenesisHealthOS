import { Alert, Badge, Button, Card, DataTable } from "@healthos/ui-kit";
import { useState } from "react";
import { ApiError, post, type ActivationProblem, type ModuleStatus } from "../api";
import { QueryState, SubmitError, useApi, useSubmit } from "../common";

const MODULES = "/api/core/modules";

export function ModulesPage() {
  const modules = useApi<ModuleStatus[]>(MODULES);
  const s = useSubmit([MODULES, "/api/core/navigation?appName=admin-web"]);
  const [needs, setNeeds] = useState<{ moduleId: string; required: string[] } | null>(null);

  async function enable(moduleId: string, includeDependencies: boolean) {
    setNeeds(null);
    try {
      await post(`${MODULES}/${moduleId}/enable`, { includeDependencies });
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setNeeds({ moduleId, required: (e.body as ActivationProblem).required });
        return;
      }
      throw e;
    }
  }

  const products = modules.data?.filter((m) => m.kind !== "platform") ?? [];
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Modules</h1>
      {needs ? (
        <Alert tone="warning" title="Other modules must be enabled first">
          <p>Enabling {needs.moduleId} also enables: {needs.required.filter((r) => r !== needs.moduleId).join(", ")}.</p>
          <Button className="mt-2" onClick={() => void s.run(null, () => enable(needs.moduleId, true))}>Enable all</Button>
        </Alert>
      ) : null}
      <SubmitError error={s.error} />
      <Card title="Products and packs">
        <QueryState error={modules.error} isPending={modules.isPending} label="modules" />
        <DataTable caption="Product modules" rows={products} rowKey={(m) => m.id} empty="No product modules are installed in this deployment."
          columns={[
            { header: "Module", cell: (m) => <><strong>{m.displayName}</strong> <span className="text-xs text-text-muted">{m.id} v{m.version}</span></> },
            { header: "Status", cell: (m) => <Badge tone={m.state === "Active" ? "success" : m.state === "ReadOnly" ? "warning" : "neutral"}>{m.state === "ReadOnly" ? "Read-only" : m.state}</Badge> },
            { header: "Why", cell: (m) => <ul className="text-xs">{m.reasons.map((r) => <li key={r.code + r.message}>{r.message}</li>)}</ul> },
            { header: "Action", cell: (m) => m.enabled
                ? <Button variant="secondary" disabled={s.busy} onClick={() => void s.run(null, () => post(`${MODULES}/${m.id}/disable`, {}))}>Disable</Button>
                : <Button disabled={s.busy} onClick={() => void s.run(null, () => enable(m.id, false))}>Enable</Button> },
          ]} />
        <p className="mt-4 text-sm text-text-muted">Disabling a module hides it; its records are kept and reappear when it is enabled again.</p>
      </Card>
      <Card title="Platform services">
        <p className="text-sm text-text-muted">{modules.data?.filter((m) => m.kind === "platform").map((m) => m.displayName).join(" · ")}</p>
      </Card>
    </div>
  );
}
