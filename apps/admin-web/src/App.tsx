import { useQuery } from "@tanstack/react-query";
import { Alert, AppShell, Button } from "@healthos/ui-kit";
import { fetchSession, loginUrl, logout } from "./api";

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

  const user = session.data.user;
  return (
    <AppShell
      productName="HealthOS Admin"
      nav={[{ label: "Home", href: "/", current: true }]}
      userSlot={
        <div className="flex items-center gap-3">
          <span className="text-sm">{user.displayName ?? user.subjectId}</span>
          <Button variant="secondary" onClick={() => void logout()}>
            Sign out
          </Button>
        </div>
      }
    >
      <h1 className="mb-4 text-2xl font-semibold">Welcome, {user.displayName ?? "user"}</h1>
      {!user.multiFactorAuthenticated ? (
        <Alert tone="warning" title="Multi-factor authentication not used">
          This session was signed in without a second factor. Administrative actions will require MFA.
        </Alert>
      ) : null}
      <p className="mt-6 text-text-muted">
        Module administration, tenants and licensing become available as their milestones are delivered.
      </p>
    </AppShell>
  );
}
