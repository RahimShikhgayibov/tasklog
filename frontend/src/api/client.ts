import type {
  ActivityAction,
  ActivityPage,
  ActivitySummary,
  Category,
  CompleteTaskResult,
  CreateTaskInput,
  JournalEntry,
  Priority,
  SaveJournalInput,
  Stats,
  Task,
  TaskCounts,
  UpdateTaskInput,
} from "./types";

const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(`/api${path}`, {
    ...init,
    headers: {
      // The server needs the user's calendar day for recurrence and stats.
      "X-Time-Zone": timeZone,
      ...(init.body ? { "Content-Type": "application/json" } : {}),
      ...init.headers,
    },
  });

  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    let fieldErrors: Record<string, string[]> = {};
    try {
      const problem = await res.json();
      if (problem?.errors) {
        fieldErrors = problem.errors;
        message = Object.values(fieldErrors).flat().join(" ");
      } else if (problem?.title) {
        message = problem.title;
      }
    } catch {
      /* not JSON */
    }
    if (res.status === 429) message = "Çox cəhd oldu. Bir dəqiqə gözlə və yenidən yoxla.";
    throw new ApiError(res.status, message, fieldErrors);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

function qs(params: Record<string, string | number | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : "";
}

const json = (body: unknown) => JSON.stringify(body);

export interface TaskQuery {
  status?: "open" | "completed" | "all";
  categoryId?: string;
  tag?: string;
  priority?: Priority;
  search?: string;
  dueBefore?: string;
  dueAfter?: string;
  completedAfter?: string;
  limit?: number;
}

export const api = {
  auth: {
    /** true = logged in; false = the server answered 401. */
    me: async () => {
      try {
        await request<{ authenticated: boolean }>("/auth/me");
        return true;
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return false;
        throw e;
      }
    },
    login: (password: string) => request<{ authenticated: boolean }>("/auth/login", { method: "POST", body: json({ password }) }),
    logout: () => request<void>("/auth/logout", { method: "POST" }),
  },

  tasks: {
    list: (q: TaskQuery) => request<Task[]>(`/tasks${qs({ ...q })}`),
    counts: (todayEnd: string) => request<TaskCounts>(`/tasks/counts${qs({ todayEnd })}`),
    get: (id: string) => request<Task>(`/tasks/${id}`),
    create: (input: CreateTaskInput) => request<Task>("/tasks", { method: "POST", body: json(input) }),
    update: (id: string, input: UpdateTaskInput) =>
      request<Task>(`/tasks/${id}`, { method: "PUT", body: json(input) }),
    remove: (id: string) => request<void>(`/tasks/${id}`, { method: "DELETE" }),
    complete: (id: string) => request<CompleteTaskResult>(`/tasks/${id}/complete`, { method: "POST" }),
    reopen: (id: string) => request<Task>(`/tasks/${id}/reopen`, { method: "POST" }),
    addSubTask: (id: string, title: string) =>
      request<Task>(`/tasks/${id}/subtasks`, { method: "POST", body: json({ title }) }),
    updateSubTask: (id: string, subId: string, patch: { title?: string; isDone?: boolean; order?: number }) =>
      request<Task>(`/tasks/${id}/subtasks/${subId}`, { method: "PATCH", body: json(patch) }),
    removeSubTask: (id: string, subId: string) =>
      request<Task>(`/tasks/${id}/subtasks/${subId}`, { method: "DELETE" }),
  },

  categories: {
    list: () => request<Category[]>("/categories"),
    create: (input: { name: string; color: string }) =>
      request<Category>("/categories", { method: "POST", body: json(input) }),
    update: (id: string, input: { name: string; color: string }) =>
      request<Category>(`/categories/${id}`, { method: "PUT", body: json(input) }),
    remove: (id: string) => request<void>(`/categories/${id}`, { method: "DELETE" }),
  },

  activity: {
    page: (q: { taskId?: string; action?: ActivityAction; from?: string; to?: string; cursor?: number; limit?: number }) =>
      request<ActivityPage>(`/activity${qs({ ...q })}`),
    summary: (from: string, to: string) => request<ActivitySummary>(`/activity/summary${qs({ from, to })}`),
    exportUrl: (from?: string, to?: string) => `/api/activity/export${qs({ from, to })}`,
  },

  journal: {
    list: (limit = 10) => request<JournalEntry[]>(`/journal${qs({ limit })}`),
    // 404 = nothing written for that day yet → null
    get: async (date: string) => {
      try {
        return await request<JournalEntry>(`/journal/${date}`);
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null;
        throw e;
      }
    },
    save: (date: string, input: SaveJournalInput) =>
      request<JournalEntry>(`/journal/${date}`, { method: "PUT", body: json(input) }),
  },

  stats: (from: string, to: string) => request<Stats>(`/stats${qs({ from, to })}`),
};
