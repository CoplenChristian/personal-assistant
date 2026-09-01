export type SettingValue = string | number | boolean;

export type SettingValueType = "enum" | "integer" | "boolean" | "string";

export interface SettingScope {
  type: "global";
  id: string | null;
}

export interface SettingMetadata {
  key: string;
  category: string;
  label: string;
  description: string;
  valueType: SettingValueType;
  options?: string[];
  value: SettingValue;
  defaultValue: SettingValue;
  hasOverride: boolean;
  source: string;
  scope: SettingScope;
  editable: boolean;
  resettable: boolean;
  requiresRestart: boolean;
  bootstrap: boolean;
  sensitive: boolean;
  constraints: Record<string, unknown>;
}

export interface SafetyMetadata {
  key: string;
  label: string;
  state: string;
  source: string;
  locked: true;
  reason: string;
}

export interface IntegrationMetadata {
  id: string;
  label: string;
  status: string;
  phase: string;
}

export interface SettingsSnapshot {
  contractVersion: "phase-0a-settings.v1" | string;
  settings: SettingMetadata[];
  safety: SafetyMetadata[];
  integrations: IntegrationMetadata[];
}

export interface SettingsChange {
  key: string;
  value: SettingValue;
}

export interface SettingsApi {
  getSettings(): Promise<SettingsSnapshot>;
  updateSettings(changes: SettingsChange[]): Promise<SettingsSnapshot>;
  resetSetting(key: string): Promise<SettingsSnapshot>;
}

export type FetchLike = (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;

interface ProblemDetailsPayload {
  code?: unknown;
  detail?: unknown;
  title?: unknown;
  key?: unknown;
}

const SETTINGS_ENDPOINT = "/api/settings";

export class SettingsApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly key: string | undefined;

  constructor(
    message: string,
    options: { status: number; code: string; key?: string },
  ) {
    super(message);
    this.name = "SettingsApiError";
    this.status = options.status;
    this.code = options.code;
    this.key = options.key;
  }

  static fromResponse(status: number, payload: unknown): SettingsApiError {
    const problem = isRecord(payload) ? (payload as ProblemDetailsPayload) : {};
    const code = typeof problem.code === "string" ? problem.code : `http_${status}`;
    const message =
      typeof problem.detail === "string"
        ? problem.detail
        : typeof problem.title === "string"
          ? problem.title
          : "The settings service could not complete that request.";
    const key = typeof problem.key === "string" ? problem.key : undefined;

    return new SettingsApiError(message, {
      status,
      code,
      ...(key ? { key } : {}),
    });
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

function isSettingsSnapshot(value: unknown): value is SettingsSnapshot {
  if (!isRecord(value)) {
    return false;
  }

  return (
    typeof value.contractVersion === "string" &&
    Array.isArray(value.settings) &&
    Array.isArray(value.safety) &&
    Array.isArray(value.integrations)
  );
}

async function requestSnapshot(
  fetcher: FetchLike,
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<SettingsSnapshot> {
  let response: Response;

  try {
    response = await fetcher(input, init);
  } catch {
    throw new SettingsApiError(
      "The local settings service is unreachable. Check that the server is running and try again.",
      { status: 0, code: "settings_unavailable" },
    );
  }

  const payload = await readJson(response);

  if (!response.ok) {
    throw SettingsApiError.fromResponse(response.status, payload);
  }

  if (!isSettingsSnapshot(payload)) {
    throw new SettingsApiError(
      "The local settings service returned an invalid response.",
      { status: response.status, code: "invalid_response" },
    );
  }

  return payload;
}

export function createSettingsApi(fetcher: FetchLike = globalThis.fetch): SettingsApi {
  return {
    getSettings: () => requestSnapshot(fetcher, SETTINGS_ENDPOINT),
    updateSettings: (changes) =>
      requestSnapshot(fetcher, SETTINGS_ENDPOINT, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ changes }),
      }),
    resetSetting: (key) =>
      requestSnapshot(fetcher, `${SETTINGS_ENDPOINT}/${encodeURIComponent(key)}`, {
        method: "DELETE",
      }),
  };
}
