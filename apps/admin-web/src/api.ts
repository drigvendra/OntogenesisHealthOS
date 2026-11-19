// Typed client for core-api. Every call carries the anti-forgery header the BFF requires.

export interface CurrentUser {
  userId: string | null;
  subjectId: string;
  displayName: string | null;
  email: string | null;
  authenticationMethods: string[];
  multiFactorAuthenticated: boolean;
  activeTenantId: string | null;
  isPlatformOperator: boolean;
}

export interface TenantSummary { id: string; code: string; name: string; regionCell: string; status: string }
export interface Organization { id: string; name: string }
export interface Site { id: string; organizationId: string; code: string; name: string; timeZone: string }
export interface Department { id: string; siteId: string; name: string }
export interface Member { userId: string; displayName: string | null; email: string | null; status: string; roles: string[] }
export interface Role { id: string; code: string; name: string; isSystem: boolean; permissions: string[] }
export interface Permission { code: string; description: string; requiresMfa: boolean }
export interface Entitlement { feature: string; state: "None" | "Active" | "ReadOnly"; validFrom: string; validTo: string | null; graceDays: number; source: string }
export interface LicensableFeature { feature: string; moduleId: string; displayName: string }
export interface AuditEvent { seq: number; occurredAt: string; actorUserId: string | null; action: string; resourceType: string; resourceId: string | null; outcome: string; details: string; hash: string }
export interface AuditVerification { intact: boolean; firstInvalidSeq: number | null }
export interface NavigationItem { moduleId: string; moduleName: string; app: string; path: string; readOnly: boolean }

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("X-CSRF", "1");
  if (init.body !== undefined) headers.set("Content-Type", "application/json");
  const res = await fetch(path, { ...init, headers, credentials: "same-origin" });
  if (res.ok) {
    return (res.status === 204 ? undefined : await res.json()) as T;
  }

  let title = defaultMessage(res.status);
  let errors: Record<string, string[]> = {};
  try {
    const body = (await res.json()) as { title?: string; errors?: Record<string, string[]> };
    title = body.title ?? title;
    errors = body.errors ?? {};
  } catch {
    // empty or non-JSON body
  }
  throw new ApiError(res.status, title, errors);
}

function defaultMessage(status: number): string {
  switch (status) {
    case 401: return "Your session has ended. Sign in again.";
    case 403: return "You don't have permission for this action. Some actions also need a sign-in with multi-factor authentication.";
    case 404: return "Not found.";
    case 409: return "This conflicts with existing data.";
    default: return "Something went wrong. Try again.";
  }
}

export const post = <T>(path: string, body: unknown) => api<T>(path, { method: "POST", body: JSON.stringify(body) });
export const put = <T>(path: string, body: unknown) => api<T>(path, { method: "PUT", body: JSON.stringify(body) });

export type SessionState =
  | { kind: "anonymous" }
  | { kind: "authenticated"; user: CurrentUser }
  | { kind: "error"; status: number };

export async function fetchSession(): Promise<SessionState> {
  try {
    return { kind: "authenticated", user: await api<CurrentUser>("/api/core/identity/me") };
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return { kind: "anonymous" };
    return { kind: "error", status: e instanceof ApiError ? e.status : 0 };
  }
}

export function loginUrl(returnUrl = "/"): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export async function logout(): Promise<void> {
  await api<void>("/bff/logout", { method: "POST" }).catch(() => undefined);
  window.location.assign("/");
}
