import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from "react";

const control =
  "mt-1 block w-full rounded-md border border-border bg-surface-raised px-3 py-2 text-sm text-text " +
  "focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-brand-600 aria-[invalid=true]:border-danger-600";

interface FieldProps {
  label: string;
  error?: string | undefined;
  hint?: string;
}

export function TextField({ label, error, hint, ...props }: FieldProps & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  const describedBy = [error ? `${id}-err` : null, hint ? `${id}-hint` : null].filter(Boolean).join(" ") || undefined;
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      <input id={id} className={control} aria-invalid={error ? true : undefined} aria-describedby={describedBy} {...props} />
      {hint ? (
        <p id={`${id}-hint`} className="mt-1 text-xs text-text-muted">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-err`} className="mt-1 text-xs text-danger-600">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function SelectField({
  label,
  error,
  children,
  ...props
}: FieldProps & SelectHTMLAttributes<HTMLSelectElement> & { children: ReactNode }) {
  const id = useId();
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      <select id={id} className={control} aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-err` : undefined} {...props}>
        {children}
      </select>
      {error ? (
        <p id={`${id}-err`} className="mt-1 text-xs text-danger-600">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function Card({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-border bg-surface-raised p-5" aria-label={title}>
      <div className="mb-4 flex items-center justify-between gap-4">
        <h2 className="text-lg font-semibold">{title}</h2>
        {actions}
      </div>
      {children}
    </section>
  );
}

export function Badge({ tone = "neutral", children }: { tone?: "neutral" | "success" | "warning" | "danger"; children: ReactNode }) {
  const tones = {
    neutral: "bg-surface text-text-muted border-border",
    success: "bg-brand-50 text-text border-brand-600",
    warning: "bg-warning-50 text-text border-warning-700",
    danger: "bg-danger-50 text-text border-danger-600",
  } as const;
  return <span className={`inline-block rounded border px-2 py-0.5 text-xs ${tones[tone]}`}>{children}</span>;
}

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
}

export function DataTable<T>({ caption, columns, rows, rowKey, empty }: {
  caption: string;
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  empty: string;
}) {
  if (rows.length === 0) {
    return <p className="text-sm text-text-muted">{empty}</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-left text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-b border-border">
            {columns.map((c) => (
              <th key={c.header} scope="col" className="px-2 py-2 font-medium text-text-muted">
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={rowKey(r)} className="border-b border-border last:border-0">
              {columns.map((c) => (
                <td key={c.header} className="px-2 py-2 align-top">
                  {c.cell(r)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
