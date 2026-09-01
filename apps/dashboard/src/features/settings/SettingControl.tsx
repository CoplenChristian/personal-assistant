import type { SettingMetadata, SettingValue } from "../../api/settingsApi";

interface SettingControlProps {
  setting: SettingMetadata;
  value: SettingValue;
  error?: string | undefined;
  onChange: (value: SettingValue) => void;
  onReset: () => void;
}

function fieldId(key: string): string {
  return "setting-" + key.replace(/[^a-zA-Z0-9]+/g, "-");
}

function formatBytes(value: number): string {
  const units = ["B", "KiB", "MiB", "GiB"];
  let amount = value;
  let unitIndex = 0;

  while (amount >= 1024 && unitIndex < units.length - 1) {
    amount /= 1024;
    unitIndex += 1;
  }

  const rounded = amount >= 10 || Number.isInteger(amount) ? Math.round(amount) : amount.toFixed(1);
  return String(rounded) + " " + units[unitIndex];
}

function formatValue(setting: SettingMetadata, value: SettingValue): string {
  if (typeof value === "boolean") {
    return value ? "Enabled" : "Disabled";
  }

  if (typeof value === "number" && setting.constraints.unit === "bytes") {
    return formatBytes(value);
  }

  return String(value);
}

function numberDisplayValue(setting: SettingMetadata, value: SettingValue): string | number {
  if (typeof value !== "number") {
    return "";
  }

  if (setting.constraints.unit === "bytes") {
    return value / (1024 * 1024);
  }

  return value;
}

function numberMultiplier(setting: SettingMetadata): number {
  return setting.constraints.unit === "bytes" ? 1024 * 1024 : 1;
}

function displayConstraint(setting: SettingMetadata, name: "minimum" | "maximum"): number | undefined {
  const value = setting.constraints[name];
  if (typeof value !== "number") {
    return undefined;
  }

  return value / numberMultiplier(setting);
}

export function SettingControl({ setting, value, error, onChange, onReset }: SettingControlProps) {
  const id = fieldId(setting.key);
  const labelId = id + "-label";
  const descriptionId = id + "-description";
  const errorId = id + "-error";
  const describedBy = error ? descriptionId + " " + errorId : descriptionId;
  const unit = typeof setting.constraints.unit === "string" ? setting.constraints.unit : undefined;

  if (!setting.editable) {
    return (
      <article className="setting-card setting-card--locked">
        <div className="setting-card__heading">
          <div>
            <p className="setting-card__label">{setting.label}</p>
            <p className="setting-card__description" id={descriptionId}>{setting.description}</p>
          </div>
          <span className="lock-badge"><span aria-hidden="true">◆</span> Locked</span>
        </div>
        <output className="setting-card__locked-value" aria-label={setting.label + " locked value"}>
          {formatValue(setting, value)}
        </output>
        <p className="setting-card__source">Source: {setting.source}. Change requires startup configuration or policy.</p>
      </article>
    );
  }

  return (
    <article className={"setting-card" + (error ? " setting-card--error" : "")}>
      <div className="setting-card__heading">
          <div>
            {setting.valueType === "boolean" ? (
              <span className="setting-card__label" id={labelId}>{setting.label}</span>
            ) : (
              <label className="setting-card__label" htmlFor={id} id={labelId}>{setting.label}</label>
            )}
            <p className="setting-card__description" id={descriptionId}>{setting.description}</p>
        </div>
        <div className="setting-card__actions">
          {setting.requiresRestart ? <span className="restart-badge">Restart required</span> : null}
          {setting.hasOverride ? (
            <button className="text-button" type="button" onClick={onReset}>
              <span aria-hidden="true">Reset</span>
              <span className="sr-only">Reset {setting.label} to default</span>
            </button>
          ) : null}
        </div>
      </div>

      <div className="setting-card__input">
        {setting.valueType === "enum" ? (
          <select
            id={id}
            value={typeof value === "string" ? value : String(value)}
            aria-describedby={describedBy}
            onChange={(event) => onChange(event.target.value)}
          >
            {setting.options?.map((option) => <option value={option} key={option}>{option}</option>)}
          </select>
        ) : null}

        {setting.valueType === "boolean" ? (
          <label className="switch-row" htmlFor={id}>
            <input
              id={id}
              type="checkbox"
              checked={value === true}
              aria-labelledby={labelId}
              aria-describedby={describedBy}
              onChange={(event) => onChange(event.target.checked)}
            />
            <span className="switch-row__track" aria-hidden="true"><span /></span>
            <span>{value ? "Enabled" : "Disabled"}</span>
          </label>
        ) : null}

        {setting.valueType === "integer" ? (
          <div className="number-input">
            <input
              id={id}
              type="number"
              value={numberDisplayValue(setting, value)}
              min={displayConstraint(setting, "minimum")}
              max={displayConstraint(setting, "maximum")}
              step={setting.constraints.unit === "bytes" ? 1 : 1}
              aria-describedby={describedBy}
              onChange={(event) => {
                const next = Number(event.target.value);
                onChange(Number.isFinite(next) ? next * numberMultiplier(setting) : 0);
              }}
            />
            {unit ? <span>{unit === "bytes" ? "MiB" : unit}</span> : null}
          </div>
        ) : null}

        {setting.valueType === "string" ? (
          <input
            id={id}
            type={setting.constraints.format === "path" ? "text" : "text"}
            value={typeof value === "string" ? value : String(value)}
            aria-describedby={describedBy}
            onChange={(event) => onChange(event.target.value)}
          />
        ) : null}
      </div>

      <div className="setting-card__footer">
        <span className="setting-card__source">
          {setting.hasOverride ? "Custom override" : "Default"} · {formatValue(setting, value)}
        </span>
        {error ? <p className="field-error" id={errorId} role="alert">{error}</p> : null}
      </div>
    </article>
  );
}
