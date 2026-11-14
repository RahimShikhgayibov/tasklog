import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type TaskQuery } from "./client";
import type { ActivityAction, CreateTaskInput, SaveJournalInput, UpdateTaskInput } from "./types";

// Query keys. Any task change can affect lists, counts, history and stats,
// so mutations invalidate all of them — simple and correct for a small app.
export const keys = {
  auth: ["auth"] as const,
  tasks: ["tasks"] as const,
  taskList: (q: TaskQuery) => ["tasks", "list", q] as const,
  counts: ["tasks", "counts"] as const,
  categories: ["categories"] as const,
  activity: ["activity"] as const,
  journal: ["journal"] as const,
  stats: ["stats"] as const,
};

function useInvalidateAll() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: keys.tasks }),
      qc.invalidateQueries({ queryKey: keys.categories }),
      qc.invalidateQueries({ queryKey: keys.activity }),
      qc.invalidateQueries({ queryKey: keys.journal }),
      qc.invalidateQueries({ queryKey: keys.stats }),
    ]);
}

export const useAuth = () =>
  useQuery({ queryKey: keys.auth, queryFn: api.auth.me, staleTime: Infinity, retry: 2 });

export function useLogin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (password: string) => api.auth.login(password),
    onSuccess: () => {
      qc.setQueryData(keys.auth, true);
      // Anything that failed with 401 before the login loads again.
      qc.invalidateQueries({ predicate: (q) => q.queryKey[0] !== keys.auth[0] });
    },
  });
}

export function useLogout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: api.auth.logout,
    onSettled: () => {
      qc.setQueryData(keys.auth, false);
      // Drop every cached task, log and journal entry (but keep the auth
      // query itself: the app is subscribed to it).
      qc.removeQueries({ predicate: (q) => q.queryKey[0] !== keys.auth[0] });
    },
  });
}

export const useTasks = (q: TaskQuery, enabled = true) =>
  useQuery({ queryKey: keys.taskList(q), queryFn: () => api.tasks.list(q), enabled });

export const useTaskCounts = (todayEnd: string) =>
  useQuery({ queryKey: [...keys.counts, todayEnd], queryFn: () => api.tasks.counts(todayEnd) });

export const useTask = (id: string | null) =>
  useQuery({
    queryKey: ["tasks", "one", id],
    queryFn: () => api.tasks.get(id!),
    enabled: !!id,
  });

export const useCategories = () => useQuery({ queryKey: keys.categories, queryFn: api.categories.list });

export function useTaskMutations() {
  const invalidate = useInvalidateAll();
  const opts = { onSettled: invalidate };

  return {
    create: useMutation({ mutationFn: (input: CreateTaskInput) => api.tasks.create(input), ...opts }),
    update: useMutation({
      mutationFn: ({ id, input }: { id: string; input: UpdateTaskInput }) => api.tasks.update(id, input),
      ...opts,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.tasks.remove(id), ...opts }),
    complete: useMutation({ mutationFn: (id: string) => api.tasks.complete(id), ...opts }),
    reopen: useMutation({ mutationFn: (id: string) => api.tasks.reopen(id), ...opts }),
    addSubTask: useMutation({
      mutationFn: ({ id, title }: { id: string; title: string }) => api.tasks.addSubTask(id, title),
      ...opts,
    }),
    updateSubTask: useMutation({
      mutationFn: ({ id, subId, patch }: { id: string; subId: string; patch: { title?: string; isDone?: boolean } }) =>
        api.tasks.updateSubTask(id, subId, patch),
      ...opts,
    }),
    removeSubTask: useMutation({
      mutationFn: ({ id, subId }: { id: string; subId: string }) => api.tasks.removeSubTask(id, subId),
      ...opts,
    }),
  };
}

export function useCategoryMutations() {
  const invalidate = useInvalidateAll();
  return {
    create: useMutation({
      mutationFn: (input: { name: string; color: string }) => api.categories.create(input),
      onSettled: invalidate,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.categories.remove(id), onSettled: invalidate }),
  };
}

export const useActivity = (filter: { taskId?: string; action?: ActivityAction; limit?: number }) =>
  useInfiniteQuery({
    queryKey: [...keys.activity, "page", filter],
    queryFn: ({ pageParam }) => api.activity.page({ ...filter, cursor: pageParam }),
    initialPageParam: undefined as number | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });

export const useActivitySummary = (from: string, to: string) =>
  useQuery({ queryKey: [...keys.activity, "summary", from, to], queryFn: () => api.activity.summary(from, to) });

export const useJournal = (date: string) =>
  useQuery({ queryKey: [...keys.journal, "day", date], queryFn: () => api.journal.get(date) });

export const useJournalList = () =>
  useQuery({ queryKey: [...keys.journal, "list"], queryFn: () => api.journal.list(10) });

export function useSaveJournal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ date, input }: { date: string; input: SaveJournalInput }) => api.journal.save(date, input),
    onSuccess: (saved) => qc.setQueryData([...keys.journal, "day", saved.date], saved),
    onSettled: () => qc.invalidateQueries({ queryKey: [...keys.journal, "list"] }),
  });
}

export const useStats = (from: string, to: string) =>
  useQuery({ queryKey: [...keys.stats, from, to], queryFn: () => api.stats(from, to) });
