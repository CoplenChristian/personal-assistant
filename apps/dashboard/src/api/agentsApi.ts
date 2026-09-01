export type AgentDesiredState = "running" | "stopped";
export type SessionObservedState = "missing" | "starting" | "running" | "exited" | "error";

export interface AgentStatus {
  contractVersion: string;
  id: string;
  name: string;
  runtime: string;
  desiredState: AgentDesiredState;
  observedState: SessionObservedState;
  tmuxSessionName: string;
  sessionDetected: boolean;
  runtimeHealthy: boolean;
  lastSeenAt: string | null;
  stoppedAt: string | null;
  lastError: string | null;
}

export interface AgentApi {
  getPersonal(): Promise<AgentStatus>;
  startPersonal(): Promise<AgentStatus>;
  stopPersonal(): Promise<AgentStatus>;
}

interface ProblemDetailsPayload {
  code?: unknown;
  detail?: unknown;
  title?: unknown;
}

export class AgentApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(message: string, options: { status: number; code: string }) {
    super(message);
    this.name = "AgentApiError";
    this.status = options.status;
    this.code = options.code;
  }

  static fromResponse(status: number, payload: unknown): AgentApiError {
    const problem = isRecord(payload) ? (payload as ProblemDetailsPayload) : {};
    const code = typeof problem.code === "string" ? problem.code : `http_${status}`;
    const message =
      typeof problem.detail === "string"
        ? problem.detail
        : typeof problem.title === "string"
          ? problem.title
          : "The personal agent service could not complete that request.";
    return new AgentApiError(message, { status, code });
  }
}

type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const AGENT_ENDPOINT = "/api/agents/personal";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isAgentStatus(value: unknown): value is AgentStatus {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.id === "string"
    && typeof value.name === "string"
    && typeof value.runtime === "string"
    && typeof value.desiredState === "string"
    && typeof value.observedState === "string"
    && typeof value.tmuxSessionName === "string";
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

async function requestStatus(fetcher: FetchLike, input: RequestInfo | URL, init?: RequestInit): Promise<AgentStatus> {
  let response: Response;
  try {
    response = await fetcher(input, init);
  } catch {
    throw new AgentApiError(
      "The local agent service is unreachable. Check that the server is running and try again.",
      { status: 0, code: "agent_unavailable" },
    );
  }

  const payload = await readJson(response);
  if (!response.ok) {
    throw AgentApiError.fromResponse(response.status, payload);
  }
  if (!isAgentStatus(payload)) {
    throw new AgentApiError("The local agent service returned an invalid response.", { status: response.status, code: "invalid_response" });
  }
  return payload;
}

export function createAgentApi(fetcher: FetchLike = globalThis.fetch): AgentApi {
  return {
    getPersonal: () => requestStatus(fetcher, AGENT_ENDPOINT),
    startPersonal: () => requestStatus(fetcher, `${AGENT_ENDPOINT}/start`, { method: "POST" }),
    stopPersonal: () => requestStatus(fetcher, `${AGENT_ENDPOINT}/stop`, { method: "POST" }),
  };
}
