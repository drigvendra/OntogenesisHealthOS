import { Badge, Button, Card, DataTable, SelectField, TextField } from "@healthos/ui-kit";
import { useState } from "react";
import { post, type Member, type Permission, type Role } from "../api";
import { QueryState, SubmitError, useApi, useSubmit } from "../common";

const MEMBERS = "/api/core/identity/members";
const ROLES = "/api/core/identity/roles";
const PERMISSIONS = "/api/core/identity/permissions";

export function MembersPage() {
  const members = useApi<Member[]>(MEMBERS);
  const roles = useApi<Role[]>(ROLES);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Members & roles</h1>

      <Card title="Members">
        <QueryState error={members.error} isPending={members.isPending} label="members" />
        {members.data ? (
          <DataTable caption="Members" rows={members.data} rowKey={(m) => m.userId} empty="No members yet."
            columns={[
              { header: "Name", cell: (m) => m.displayName ?? "—" },
              { header: "Email", cell: (m) => m.email ?? "—" },
              { header: "Status", cell: (m) => <Badge tone={m.status === "active" ? "success" : "warning"}>{m.status}</Badge> },
              { header: "Roles", cell: (m) => (m.roles.length ? m.roles.join(", ") : "—") },
              { header: "Assign role", cell: (m) => (roles.data ? <AssignRole member={m} roles={roles.data} /> : null) },
            ]} />
        ) : null}
        <AddMember />
      </Card>

      <Card title="Roles">
        <QueryState error={roles.error} isPending={roles.isPending} label="roles" />
        {roles.data ? (
          <DataTable caption="Roles" rows={roles.data} rowKey={(r) => r.id} empty="No roles yet."
            columns={[
              { header: "Code", cell: (r) => <>{r.code} {r.isSystem ? <Badge>system</Badge> : null}</> },
              { header: "Name", cell: (r) => r.name },
              { header: "Permissions", cell: (r) => <span className="text-xs">{r.permissions.join(", ")}</span> },
            ]} />
        ) : null}
        <NewRole />
      </Card>
    </div>
  );
}

function AddMember() {
  const [email, setEmail] = useState("");
  const s = useSubmit([MEMBERS]);
  return (
    <form className="mt-4 flex flex-wrap items-end gap-3" onSubmit={(e) => void s.run(e, () => post(MEMBERS, { email })).then((ok) => ok && setEmail(""))}>
      <TextField label="Add member by email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} error={s.field("email")}
        hint="The person must have signed in once." required />
      <Button type="submit" disabled={s.busy}>Add member</Button>
      <SubmitError error={s.error} />
    </form>
  );
}

function AssignRole({ member, roles }: { member: Member; roles: Role[] }) {
  const available = roles.filter((r) => !member.roles.includes(r.code));
  const [roleId, setRoleId] = useState(available[0]?.id ?? "");
  const s = useSubmit([MEMBERS]);
  if (available.length === 0) return <span className="text-xs text-text-muted">All roles assigned</span>;
  return (
    <form className="flex items-end gap-2" onSubmit={(e) => void s.run(e, () => post(`${MEMBERS}/${member.userId}/roles`, { roleId, siteId: null }))}>
      <SelectField label={`Role for ${member.displayName ?? member.email ?? "member"}`} value={roleId} onChange={(e) => setRoleId(e.target.value)}>
        {available.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
      </SelectField>
      <Button type="submit" variant="secondary" disabled={s.busy}>Assign</Button>
      <SubmitError error={s.error} />
    </form>
  );
}

function NewRole() {
  const permissions = useApi<Permission[]>(PERMISSIONS);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const s = useSubmit([ROLES]);

  const toggle = (p: string) => setSelected((cur) => (cur.includes(p) ? cur.filter((x) => x !== p) : [...cur, p]));

  return (
    <form className="mt-6 space-y-3"
      onSubmit={(e) => void s.run(e, () => post(ROLES, { code, name, permissions: selected })).then((ok) => {
        if (ok) { setCode(""); setName(""); setSelected([]); }
      })}>
      <h3 className="font-medium">New role</h3>
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField label="Role code" value={code} onChange={(e) => setCode(e.target.value)} error={s.field("code")} hint="e.g. front-desk" required />
        <TextField label="Role name" value={name} onChange={(e) => setName(e.target.value)} error={s.field("name")} required />
      </div>
      <fieldset>
        <legend className="text-sm font-medium">Permissions</legend>
        {s.field("permissions") ? <p className="text-xs text-danger-600">{s.field("permissions")}</p> : null}
        <div className="mt-2 grid gap-1 sm:grid-cols-2">
          {permissions.data?.map((p) => (
            <label key={p.code} className="flex items-start gap-2 text-sm">
              <input type="checkbox" checked={selected.includes(p.code)} onChange={() => toggle(p.code)} />
              <span>
                {p.description} <span className="text-xs text-text-muted">({p.code}{p.requiresMfa ? ", needs MFA" : ""})</span>
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      <Button type="submit" disabled={s.busy}>Create role</Button>
      <SubmitError error={s.error} />
    </form>
  );
}
