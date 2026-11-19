import type { ReactNode } from "react";

type Tone = "danger" | "warning" | "info";

const tones: Record<Tone, string> = {
  danger: "bg-danger-50 border-danger-600 text-text",
  warning: "bg-warning-50 border-warning-700 text-text",
  info: "bg-brand-50 border-brand-600 text-text",
};

export function Alert({ tone = "info", title, children }: { tone?: Tone; title: string; children?: ReactNode }) {
  return (
    <div role={tone === "danger" ? "alert" : "status"} className={`rounded-md border-l-4 p-4 ${tones[tone]}`}>
      <p className="font-semibold">{title}</p>
      {children ? <div className="mt-1 text-sm text-text-muted">{children}</div> : null}
    </div>
  );
}
