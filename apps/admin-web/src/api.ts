export interface CurrentUser {
  subjectId: string;
  displayName: string | null;
  email: string | null;
  authenticationMethods: string[];
  multiFactorAuthenticated: boolean;
}

export type SessionState =
  | { kind: "anonymous" }
  | { kind: "authenticated"; user: CurrentUser }
  | { kind: "error"; status: number };

/** All API calls carry the anti-forgery header required by core-api (see CsrfHeaderMiddleware). */
export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("X-CSRF", "1");
  return fetch(path, { ...init, headers, credentials: "same-origin" });
}

export async function fetchSession(): Promise<SessionState> {
  const res = await apiFetch("/api/core/identity/me");
  if (res.status === 401) return { kind: "anonymous" };
  if (!res.ok) return { kind: "error", status: res.status };
  return { kind: "authenticated", user: (await res.json()) as CurrentUser };
}

export function loginUrl(returnUrl = "/"): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export async function logout(): Promise<void> {
  await apiFetch("/bff/logout", { method: "POST" });
  window.location.assign("/");
}
