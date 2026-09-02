export interface ActivityCounters {
  promptsDelivered: number;
  scheduledRuns: number;
  scheduledPromptsQueued: number;
  scheduledPromptsDropped: number;
  emailReads: number;
  emailModifications: number;
  messagesSent: number;
  messagesReplied: number;
  messagesBlocked: number;
  calendarWrites: number;
  reminderWrites: number;
  memoryWrites: number;
  memoryCheckpoints: number;
  documentIndexing: number;
  browserActions: number;
  securityBlocked: number;
  failures: number;
  agentStarts: number;
  agentStops: number;
  agentClears: number;
  agentRotations: number;
  rosterChanges: number;
}

export interface ActivityEventItem {
  id: string;
  timestamp: string;
  agentId: string | null;
  realm: string | null;
  category: string;
  operation: string;
  target: string | null;
  status: string;
  durationMs: number | null;
  metadataJson: string;
}

export interface ActivitySnapshot {
  contractVersion: string;
  date: string;
  timezone: string;
  counters: ActivityCounters;
  recentEvents: ActivityEventItem[];
  feedLimit: number;
}

export interface ActivityApi {
  getActivity(options?: {
    date?: string;
    timezone?: string;
    limit?: number;
    signal?: AbortSignal;
  }): Promise<ActivitySnapshot>;
}

interface ProblemDetailsPayload {
  code?: unknown;
  detail?: unknown;
  title?: unknown;
}

export class ActivityApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(message: string, options: { status: number; code: string }) {
    super(message);
    this.name = "ActivityApiError";
    this.status = options.status;
    this.code = options.code;
  }

  static fromResponse(status: number, payload: unknown): ActivityApiError {
    const problem = isRecord(payload) ? (payload as ProblemDetailsPayload) : {};
    const code = typeof problem.code === "string" ? problem.code : `http_${status}`;
    let message = "The activity service could not complete that request.";
    if (typeof problem.detail === "string") {
      message = problem.detail;
    } else if (typeof problem.title === "string") {
      message = problem.title;
    }
    return new ActivityApiError(message, { status, code });
  }
}

type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export const COUNTER_KEYS: Array<keyof ActivityCounters> = [
  "promptsDelivered",
  "scheduledRuns",
  "scheduledPromptsQueued",
  "scheduledPromptsDropped",
  "emailReads",
  "emailModifications",
  "messagesSent",
  "messagesReplied",
  "messagesBlocked",
  "calendarWrites",
  "reminderWrites",
  "memoryWrites",
  "memoryCheckpoints",
  "documentIndexing",
  "browserActions",
  "securityBlocked",
  "failures",
  "agentStarts",
  "agentStops",
  "agentClears",
  "agentRotations",
  "rosterChanges",
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isActivityEventItem(value: unknown): value is ActivityEventItem {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.id === "string"
    && typeof value.timestamp === "string"
    && typeof value.category === "string"
    && typeof value.operation === "string"
    && typeof value.status === "string"
    && typeof value.metadataJson === "string";
}

function isActivitySnapshot(value: unknown): value is ActivitySnapshot {
  if (!isRecord(value) || !isRecord(value.counters)) {
    return false;
  }

  const counters = value.counters;
  const countersValid = COUNTER_KEYS.every((key) => typeof counters[key] === "number");
  const events = value.recentEvents;
  const eventsValid = Array.isArray(events) && events.every(isActivityEventItem);

  return typeof value.contractVersion === "string"
    && typeof value.date === "string"
    && typeof value.timezone === "string"
    && countersValid
    && eventsValid
    && typeof value.feedLimit === "number";
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

export function createActivityApi(fetcher: FetchLike = globalThis.fetch): ActivityApi {
  return {
    getActivity: async (options) => {
      const params = new URLSearchParams();
      if (options?.date) {
        params.set("date", options.date);
      }
      if (options?.timezone) {
        params.set("timezone", options.timezone);
      }
      if (typeof options?.limit === "number") {
        params.set("limit", String(options.limit));
      }

      const query = params.toString();
      const url = query.length > 0 ? `/api/activity?${query}` : "/api/activity";

      let response: Response;
      try {
        response = await fetcher(
          url,
          options?.signal !== undefined ? { signal: options.signal } : {},
        );
      } catch (error) {
        if (options?.signal?.aborted) {
          throw error;
        }
        throw new ActivityApiError(
          "The activity service is unreachable. Check that the server is running and try again.",
          { status: 0, code: "activity_unavailable" },
        );
      }

      const payload = await readJson(response);
      if (!response.ok) {
        throw ActivityApiError.fromResponse(response.status, payload);
      }
      if (!isActivitySnapshot(payload)) {
        throw new ActivityApiError("The activity service returned an invalid response.", {
          status: response.status,
          code: "invalid_response",
        });
      }

      return payload;
    },
  };
}

export const COUNTER_LABELS: Record<keyof ActivityCounters, string> = {
  promptsDelivered: "Prompts delivered",
  scheduledRuns: "Scheduled runs",
  scheduledPromptsQueued: "Queued scheduled prompts",
  scheduledPromptsDropped: "Dropped scheduled prompts",
  emailReads: "Email reads",
  emailModifications: "Email modifications",
  messagesSent: "Messages sent",
  messagesReplied: "Messages replied",
  messagesBlocked: "Messages blocked",
  calendarWrites: "Calendar writes",
  reminderWrites: "Reminder writes",
  memoryWrites: "Memory writes",
  memoryCheckpoints: "Memory checkpoints",
  documentIndexing: "Document indexing",
  browserActions: "Browser actions",
  securityBlocked: "Blocked security actions",
  failures: "Failures",
  agentStarts: "Agent starts",
  agentStops: "Agent stops",
  agentClears: "Agent clears",
  agentRotations: "Agent rotations",
  rosterChanges: "Roster changes",
};
