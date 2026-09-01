import { useEffect, useState } from "react";
import type { ReactNode } from "react";

import { SettingsPage } from "../features/settings/SettingsPage";
import { AgentControlCard } from "../features/agents/AgentControlCard";

function normalizePath(pathname: string): string {
  const withoutTrailingSlash = pathname.replace(/\/+$/, "");
  return withoutTrailingSlash || "/";
}

function useCurrentPath(): string {
  const [path, setPath] = useState(() => normalizePath(globalThis.location?.pathname ?? "/"));

  useEffect(() => {
    const handlePopState = () => {
      setPath(normalizePath(globalThis.location?.pathname ?? "/"));
    };

    globalThis.addEventListener("popstate", handlePopState);
    return () => globalThis.removeEventListener("popstate", handlePopState);
  }, []);

  return path;
}

function NavItem({ href, label, currentPath }: { href: string; label: string; currentPath: string }) {
  const active = currentPath === href;

  return (
    <a className={`rail-nav__item${active ? " rail-nav__item--active" : ""}`} href={href} aria-current={active ? "page" : undefined}>
      <span className="rail-nav__marker" aria-hidden="true" />
      <span>{label}</span>
    </a>
  );
}

function AppShell({ currentPath, children }: { currentPath: string; children: ReactNode }) {
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">Skip to content</a>
      <aside className="app-rail" aria-label="Application navigation">
        <a className="brand-lockup" href="/" aria-label="Personal Assistant overview">
          <span className="brand-lockup__mark" aria-hidden="true">PA</span>
          <span>
            <span className="brand-lockup__eyebrow">LOCAL CONTROL PLANE</span>
            <span className="brand-lockup__name">Personal Assistant</span>
          </span>
        </a>

        <nav className="rail-nav" aria-label="Primary navigation">
          <span className="rail-nav__caption">Workspace</span>
          <NavItem href="/" label="Overview" currentPath={currentPath} />
          <NavItem href="/settings" label="Settings" currentPath={currentPath} />
        </nav>

        <div className="rail-footer">
          <span className="status-orb status-orb--amber" aria-hidden="true" />
          <div>
            <span className="rail-footer__label">Local only</span>
            <span className="rail-footer__detail">Native lifecycle · Phase 0B</span>
          </div>
        </div>
      </aside>

      <div className="app-content">
        <header className="topbar">
          <div className="topbar__context">
            <span className="topbar__path">/ personal-assistant</span>
            <span className="topbar__separator" aria-hidden="true">/</span>
            <span className="topbar__current">{currentPath === "/settings" ? "settings" : "overview"}</span>
          </div>
          <div className="topbar__status">
            <span className="status-orb status-orb--green" aria-hidden="true" />
            <span>Configured host</span>
          </div>
        </header>
        <main id="main-content" className="app-main">{children}</main>
      </div>
    </div>
  );
}

function OverviewPage() {
  return (
    <div className="overview-stack">
      <section className="overview-card" aria-labelledby="overview-title">
        <span className="eyebrow">CONTROL PLANE / OVERVIEW</span>
        <h1 id="overview-title">A quiet place to see what is configured.</h1>
        <p>
          Settings and native agent lifecycle now share one effective configuration boundary. Terminal
          output and activity surfaces follow in the next slices.
        </p>
        <a className="button button--primary" href="/settings">Open settings <span aria-hidden="true">↗</span></a>
      </section>
      <AgentControlCard />
    </div>
  );
}

function NotFoundPage() {
  return (
    <section className="overview-card" aria-labelledby="not-found-title">
      <span className="eyebrow">404 / LOCAL ROUTE</span>
      <h1 id="not-found-title">That surface is not configured yet.</h1>
      <p>Return to the overview or open the real settings route.</p>
      <a className="button button--primary" href="/settings">Go to settings <span aria-hidden="true">↗</span></a>
    </section>
  );
}

export function App() {
  const currentPath = useCurrentPath();

  let content: ReactNode = <NotFoundPage />;
  if (currentPath === "/") {
    content = <OverviewPage />;
  } else if (currentPath === "/settings") {
    content = <SettingsPage />;
  }

  return <AppShell currentPath={currentPath}>{content}</AppShell>;
}
