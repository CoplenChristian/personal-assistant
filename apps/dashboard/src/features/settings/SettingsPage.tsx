import { useCallback, useEffect, useMemo, useState } from "react";

import {
  createSettingsApi,
  SettingsApiError,
} from "../../api/settingsApi";
import type {
  IntegrationMetadata,
  SafetyMetadata,
  SettingMetadata,
  SettingValue,
  SettingsSnapshot,
} from "../../api/settingsApi";
import { SettingControl } from "./SettingControl";

const CATEGORY_ORDER = [
  { name: "General", slug: "general", description: "The surface you return to every day." },
  { name: "Agents", slug: "agents", description: "Defaults for agents created in the future." },
  { name: "Sessions", slug: "sessions", description: "Limits that keep native sessions legible and finite." },
  { name: "Documents & Memory", slug: "documents-memory", description: "Pointers and preferences for later local indexing." },
  { name: "Automation", slug: "automation", description: "Guardrails for future routines and schedules." },
  { name: "System", slug: "system", description: "Startup values shown for transparency." },
];

type DraftValues = Record<string, SettingValue>;
type ErrorMap = Record<string, string>;

function initialDrafts(snapshot: SettingsSnapshot): DraftValues {
  return Object.fromEntries(snapshot.settings.map((setting) => [setting.key, setting.value]));
}

function sameValue(left: SettingValue | undefined, right: SettingValue | undefined): boolean {
  return left === right;
}

function constraintNumber(setting: SettingMetadata, name: "minimum" | "maximum"): number | undefined {
  const value = setting.constraints[name];
  return typeof value === "number" ? value : undefined;
}

function validateSetting(setting: SettingMetadata, value: SettingValue, drafts: DraftValues): string | undefined {
  if (setting.valueType === "integer") {
    if (typeof value !== "number" || !Number.isInteger(value)) {
      return "Enter a whole number.";
    }

    const minimum = constraintNumber(setting, "minimum");
    const maximum = constraintNumber(setting, "maximum");
    if (minimum !== undefined && value < minimum) {
      return "Use a value of " + minimum + " or higher.";
    }
    if (maximum !== undefined && value > maximum) {
      return "Use a value of " + maximum + " or lower.";
    }
  }

  if (setting.valueType === "string" && setting.key === "documents.vaultPath" && typeof value === "string" && value.trim().length === 0) {
    return "Enter a vault path.";
  }

  if (setting.key === "sessions.nativeSessionRotateBytes") {
    const warning = drafts["sessions.nativeSessionWarningBytes"];
    if (typeof value === "number" && typeof warning === "number" && value <= warning) {
      return "Must be greater than the native session warning size.";
    }
  }

  return undefined;
}

function groupSettings(settings: SettingMetadata[], category: string): SettingMetadata[] {
  return settings.filter((setting) => setting.category === category);
}

function errorMessage(error: unknown): string {
  if (error instanceof SettingsApiError) {
    return error.message;
  }
  return "The local settings service could not complete that request.";
}

function StatusMessage({ message, error }: { message: string | null; error: string | null }) {
  if (!message && !error) {
    return null;
  }

  return (
    <div className={"status-message" + (error ? " status-message--error" : "")} role={error ? "alert" : "status"} aria-live="polite">
      <span className="status-message__dot" aria-hidden="true" />
      <span>{error ?? message}</span>
    </div>
  );
}

function SafetySection({ safety }: { safety: SafetyMetadata[] }) {
  return (
    <section className="settings-section" id="settings-safety" aria-labelledby="settings-safety-title">
      <div className="section-heading">
        <div className="section-heading__index">08</div>
        <div>
          <p className="eyebrow">NON-NEGOTIABLE</p>
          <h2 id="settings-safety-title">Safety posture</h2>
          <p>These protections are visible here so their boundaries stay legible. They cannot be weakened from Settings.</p>
        </div>
      </div>
      <div className="safety-grid">
        {safety.map((item) => (
          <article className="safety-card" key={item.key}>
            <div className="safety-card__topline">
              <span className="safety-card__icon" aria-hidden="true">◆</span>
              <span className="lock-badge">Locked</span>
            </div>
            <h3>{item.label}</h3>
            <strong>{item.state}</strong>
            <p>{item.reason}</p>
            <span className="setting-card__source">Source: {item.source}</span>
          </article>
        ))}
      </div>
    </section>
  );
}

function IntegrationsSection({ integrations }: { integrations: IntegrationMetadata[] }) {
  return (
    <section className="settings-section" id="settings-integrations" aria-labelledby="settings-integrations-title">
      <div className="section-heading">
        <div className="section-heading__index">07</div>
        <div>
          <p className="eyebrow">FUTURE CONNECTIONS</p>
          <h2 id="settings-integrations-title">Integrations</h2>
          <p>Nothing is connected yet. These cards mark where later vertical slices will live.</p>
        </div>
      </div>
      <div className="integration-grid">
        {integrations.map((integration) => (
          <article className="integration-card" key={integration.id}>
            <div className="integration-card__mark" aria-hidden="true">{integration.id.slice(0, 1).toUpperCase()}</div>
            <div>
              <h3>{integration.label}</h3>
              <p>{integration.status.replace("-", " ")}</p>
            </div>
            <span className="phase-pill">{integration.phase}</span>
          </article>
        ))}
      </div>
    </section>
  );
}

export function SettingsPage() {
  const api = useMemo(() => createSettingsApi(), []);
  const [snapshot, setSnapshot] = useState<SettingsSnapshot | null>(null);
  const [drafts, setDrafts] = useState<DraftValues>({});
  const [loadState, setLoadState] = useState<"idle" | "loading" | "ready">("idle");
  const [saving, setSaving] = useState(false);
  const [resettingKey, setResettingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const loadSettings = useCallback(async () => {
    setLoadState("loading");
    setError(null);
    try {
      const next = await api.getSettings();
      setSnapshot(next);
      setDrafts(initialDrafts(next));
      setMessage(null);
      setLoadState("ready");
    } catch (loadError) {
      setError(errorMessage(loadError));
      setLoadState("idle");
    }
  }, [api]);

  useEffect(() => {
    void loadSettings();
  }, [loadSettings]);

  const theme = typeof drafts["appearance.theme"] === "string" ? drafts["appearance.theme"] : "system";
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const dirtySettings = useMemo(() => {
    if (!snapshot) {
      return [];
    }
    return snapshot.settings.filter((setting) => !sameValue(setting.value, drafts[setting.key]));
  }, [drafts, snapshot]);

  const validationErrors = useMemo<ErrorMap>(() => {
    if (!snapshot) {
      return {};
    }

    return Object.fromEntries(
      snapshot.settings
        .filter((setting) => setting.editable)
        .map((setting) => {
          const value = drafts[setting.key];
          return [setting.key, value === undefined ? "Value is unavailable." : validateSetting(setting, value, drafts)] as const;
        })
        .filter((entry): entry is [string, string] => typeof entry[1] === "string"),
    );
  }, [drafts, snapshot]);

  async function saveChanges() {
    if (!snapshot || dirtySettings.length === 0 || Object.keys(validationErrors).length > 0) {
      return;
    }

    setSaving(true);
    setError(null);
    setMessage("Saving changes…");
    try {
      const next = await api.updateSettings(dirtySettings.map((setting) => ({
        key: setting.key,
        value: drafts[setting.key] ?? setting.value,
      })));
      setSnapshot(next);
      setDrafts(initialDrafts(next));
      setMessage("Settings saved.");
    } catch (saveError) {
      setError(errorMessage(saveError));
      setMessage(null);
    } finally {
      setSaving(false);
    }
  }

  async function resetSetting(setting: SettingMetadata) {
    if (!setting.hasOverride || resettingKey) {
      return;
    }

    setResettingKey(setting.key);
    setError(null);
    setMessage("Resetting " + setting.label + "…");
    try {
      const next = await api.resetSetting(setting.key);
      setSnapshot(next);
      setDrafts(initialDrafts(next));
      setMessage(setting.label + " restored to its default.");
    } catch (resetError) {
      setError(errorMessage(resetError));
      setMessage(null);
    } finally {
      setResettingKey(null);
    }
  }

  if (loadState === "loading" && !snapshot) {
    return (
      <section className="settings-page settings-page--loading" aria-busy="true" aria-labelledby="settings-loading-title">
        <span className="eyebrow">CONTROL PLANE / SETTINGS</span>
        <h1 id="settings-loading-title">Reading the local configuration.</h1>
        <div className="loading-stack" aria-hidden="true"><span /><span /><span /></div>
      </section>
    );
  }

  if (!snapshot) {
    return (
      <section className="settings-page settings-page--error" aria-labelledby="settings-error-title">
        <span className="eyebrow">CONTROL PLANE / SETTINGS</span>
        <h1 id="settings-error-title">Settings are temporarily out of reach.</h1>
        <p>{error ?? "Start the local ASP.NET Core server, then try again."}</p>
        <button className="button button--primary" type="button" onClick={() => void loadSettings()}>Retry connection</button>
      </section>
    );
  }

  return (
    <section className="settings-page" aria-labelledby="settings-title">
      <header className="settings-hero">
        <div>
          <span className="eyebrow">CONTROL PLANE / SETTINGS</span>
          <h1 id="settings-title">The shape of the assistant,<br /><em>kept legible.</em></h1>
          <p className="settings-hero__intro">
            One small surface for preferences, startup boundaries, and the safety posture
            that future capabilities must respect.
          </p>
        </div>
        <div className="settings-hero__stamp" role="img" aria-label="Phase 0A settings">
          <span className="stamp-ring" aria-hidden="true" />
          <span>PHASE<br /><strong>0A</strong></span>
        </div>
      </header>

      <div className="settings-toolbar">
        <div className="settings-toolbar__summary">
          <span className={"status-orb " + (dirtySettings.length > 0 ? "status-orb--amber" : "status-orb--green")} aria-hidden="true" />
          <span>{dirtySettings.length > 0 ? dirtySettings.length + " unsaved change" + (dirtySettings.length === 1 ? "" : "s") : "All changes synced"}</span>
          <span className="toolbar-divider" aria-hidden="true" />
          <span className="toolbar-contract">{snapshot.contractVersion}</span>
        </div>
        <div className="settings-toolbar__actions">
          {loadState === "loading" ? <span className="quiet-note">Refreshing…</span> : null}
          <button
            className="button button--primary"
            type="button"
            onClick={() => void saveChanges()}
            disabled={saving || dirtySettings.length === 0 || Object.keys(validationErrors).length > 0}
            aria-busy={saving}
          >
            {saving ? "Saving…" : "Save changes"}
          </button>
        </div>
      </div>

      <StatusMessage message={message} error={error} />

      <div className="settings-layout">
        <nav className="settings-index" aria-label="Settings sections">
          <span className="settings-index__label">On this page</span>
          {CATEGORY_ORDER.map((category, index) => (
            <a href={"#settings-" + category.slug} key={category.slug}>
              <span>0{index + 1}</span>{category.name}
            </a>
          ))}
          <a href="#settings-integrations"><span>07</span>Integrations</a>
          <a href="#settings-safety"><span>08</span>Safety posture</a>
        </nav>

        <div className="settings-sections">
          {CATEGORY_ORDER.map((category, index) => {
            const categorySettings = groupSettings(snapshot.settings, category.name);
            if (categorySettings.length === 0) {
              return null;
            }

            return (
              <section className="settings-section" id={"settings-" + category.slug} aria-labelledby={"settings-" + category.slug + "-title"} key={category.name}>
                <div className="section-heading">
                  <div className="section-heading__index">0{index + 1}</div>
                  <div>
                    <p className="eyebrow">{category.name.toUpperCase()}</p>
                    <h2 id={"settings-" + category.slug + "-title"}>{category.name}</h2>
                    <p>{category.description}</p>
                  </div>
                </div>
                <div className="settings-grid">
                  {categorySettings.map((setting) => (
                    <SettingControl
                      key={setting.key}
                      setting={setting}
                      value={drafts[setting.key] ?? setting.value}
                      error={validationErrors[setting.key]}
                      onChange={(value) => setDrafts((current) => ({ ...current, [setting.key]: value }))}
                      onReset={() => void resetSetting(setting)}
                    />
                  ))}
                </div>
              </section>
            );
          })}

          <IntegrationsSection integrations={snapshot.integrations} />
          <SafetySection safety={snapshot.safety} />
        </div>
      </div>
    </section>
  );
}
