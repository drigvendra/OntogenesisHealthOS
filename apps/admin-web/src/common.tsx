import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert } from "@healthos/ui-kit";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "./api";

export function useApi<T>(path: string | null) {
  return useQuery({ queryKey: [path], queryFn: () => api<T>(path!), enabled: path !== null, retry: false });
}

export function QueryState({ error, isPending, label }: { error: unknown; isPending: boolean; label: string }) {
  if (isPending) return <p role="status">Loading {label}…</p>;
  if (error) return <Alert tone="danger" title={`Could not load ${label}`}>{error instanceof Error ? error.message : null}</Alert>;
  return null;
}

/** Form submission with server validation errors mapped to fields, and query invalidation on success. */
export function useSubmit(invalidate: string[]) {
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function run(e: FormEvent | null, action: () => Promise<unknown>): Promise<boolean> {
    e?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await action();
      await Promise.all(invalidate.map((k) => qc.invalidateQueries({ queryKey: [k] })));
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, "Something went wrong. Try again."));
      return false;
    } finally {
      setBusy(false);
    }
  }

  const field = (name: string) => error?.fieldErrors[name]?.[0];
  return { run, busy, error, field };
}

export function SubmitError({ error }: { error: ApiError | null }) {
  if (!error || Object.keys(error.fieldErrors).length > 0) return null;
  return <Alert tone="danger" title={error.message} />;
}

export function formatDate(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}
