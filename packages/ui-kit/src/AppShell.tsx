import type { ReactNode } from "react";

export interface NavItem {
  label: string;
  href: string;
  current?: boolean;
}

export function AppShell({
  productName,
  nav,
  userSlot,
  children,
}: {
  productName: string;
  nav: NavItem[];
  userSlot?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="min-h-screen bg-surface text-text font-sans">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:p-2">
        Skip to main content
      </a>
      <header className="border-b border-border bg-surface-raised">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-3">
          <span className="text-lg font-semibold">{productName}</span>
          {userSlot}
        </div>
        {nav.length > 0 ? (
          <nav aria-label="Main" className="mx-auto max-w-6xl px-4">
            <ul className="flex gap-4">
              {nav.map((item) => (
                <li key={item.href}>
                  <a
                    href={item.href}
                    aria-current={item.current ? "page" : undefined}
                    className="inline-block border-b-2 border-transparent py-2 text-sm aria-[current=page]:border-brand-600"
                  >
                    {item.label}
                  </a>
                </li>
              ))}
            </ul>
          </nav>
        ) : null}
      </header>
      <main id="main" className="mx-auto max-w-6xl px-4 py-8">
        {children}
      </main>
    </div>
  );
}
