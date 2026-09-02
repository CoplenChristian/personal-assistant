export type SessionHygieneAction = "compact" | "clear" | "rotate";

export interface CheckpointPayload {
  reason: string;
  generatedMemory: string;
  generatedHandoff: string;
}

export interface SessionHygieneRequest {
  requestId: string;
  checkpoint: CheckpointPayload;
}

export interface SessionHygieneResponse {
  contractVersion: string;
  requestId: string;
  action: SessionHygieneAction;
  checkpointId: string;
  desiredState: string;
  observedState: string;
  nativeActionPerformed: boolean;
}

export interface CheckpointResponse {
  contractVersion: string;
  requestId: string;
  checkpointId: string;
}

export interface HygieneApi {
  execute(action: SessionHygieneAction, request: SessionHygieneRequest): Promise<SessionHygieneResponse>;
  checkpoint(request: SessionHygieneRequest): Promise<CheckpointResponse>;
}

interface ProblemDetailsPayload {
  code?: unknown;
  detail?: unknown;
  title?: unknown;
}

type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export class HygieneApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(message: string, options: { status: number; code: string }) {
    super(message);
    this.name = "HygieneApiError";
    this.status = options.status;
    this.code = options.code;
  }

  static fromResponse(status: number, payload: unknown): HygieneApiError {
    const problem = isRecord(payload) ? (payload as ProblemDetailsPayload) : {};
    const code = typeof problem.code === "string" ? problem.code : `http_${status}`;
    const message =
      typeof problem.detail === "string"
        ? problem.detail
        : typeof problem.title === "string"
          ? problem.title
          : "The session hygiene action could not be completed.";
    return new HygieneApiError(message, { status, code });
  }
}

export function createHygieneApi(fetcher: FetchLike = globalThis.fetch): HygieneApi {
  async function request<T>(path: string, payload: SessionHygieneRequest): Promise<T> {
    let response: Response;
    try {
      response = await fetcher(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
    } catch {
      throw new HygieneApiError(
        "The local session hygiene service is unreachable. Check that the server is running and try again.",
        { status: 0, code: "hygiene_unavailable" },
      );
    }

    const body = await readJson(response);
    if (!response.ok) {
      throw HygieneApiError.fromResponse(response.status, body);
    }
    if (!isRecord(body) || typeof body.checkpointId !== "string" || typeof body.requestId !== "string") {
      throw new HygieneApiError("The local service returned an invalid hygiene response.", {
        status: response.status,
        code: "invalid_response",
      });
    }
    return body as T;
  }

  return {
    execute: (action, payload) => request<SessionHygieneResponse>(`/api/agents/personal/hygiene/${action}`, payload),
    checkpoint: (payload) => request<CheckpointResponse>("/api/agents/personal/hygiene/checkpoint", payload),
  };
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
