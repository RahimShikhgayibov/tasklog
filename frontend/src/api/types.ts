// Mirrors the backend DTOs (System.Text.Json camelCase, enums as strings).
// Dates are ISO 8601 strings in UTC.

export type Priority = "Low" | "Medium" | "High";
export type Recurrence = "None" | "Daily" | "Weekdays" | "Weekly" | "Monthly";
export type ActivityAction = "Created" | "Updated" | "Completed" | "Reopened" | "Deleted";
export type ActivityEntityType = "Task" | "SubTask";

export interface SubTask {
  id: string;
  title: string;
  isDone: boolean;
  order: number;
}

export interface Task {
  id: string;
  title: string;
  description: string | null;
  priority: Priority;
  dueAt: string | null;
  isCompleted: boolean;
  completedAt: string | null;
  categoryId: string | null;
  tags: string[];
  recurrence: Recurrence;
  seriesId: string;
  subTasks: SubTask[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateTaskInput {
  title: string;
  description?: string | null;
  priority?: Priority;
  dueAt?: string | null;
  categoryId?: string | null;
  tags?: string[];
  recurrence?: Recurrence;
  subTasks?: string[];
}

export interface UpdateTaskInput {
  title: string;
  description: string | null;
  priority: Priority;
  dueAt: string | null;
  categoryId: string | null;
  tags: string[];
  recurrence: Recurrence;
}

export interface CompleteTaskResult {
  task: Task;
  nextOccurrence: Task | null;
}

export interface TaskCounts {
  today: number;
  overdue: number;
  upcoming: number;
  allOpen: number;
  byCategory: Record<string, number>;
}

export interface Category {
  id: string;
  name: string;
  color: string;
}

export interface FieldChange {
  field: string;
  from: string | null;
  to: string | null;
}

export interface ActivityEntry {
  id: number;
  timestamp: string;
  entityType: ActivityEntityType;
  entityId: string;
  taskId: string | null;
  title: string;
  parentTitle: string | null;
  action: ActivityAction;
  changes: FieldChange[];
}

export interface ActivityPage {
  items: ActivityEntry[];
  nextCursor: number | null;
}

export interface ActivitySummary {
  created: number;
  completed: number;
  updated: number;
  deleted: number;
  completedTitles: string[];
}

export interface JournalEntry {
  date: string; // yyyy-MM-dd
  done: string | null;
  learned: string | null;
  blockers: string | null;
  tomorrowPlan: string | null;
  minutesSpent: number | null;
  updatedAt: string;
}

export interface SaveJournalInput {
  done: string | null;
  learned: string | null;
  blockers: string | null;
  tomorrowPlan: string | null;
  minutesSpent: number | null;
}

export interface DayStat {
  date: string;
  created: number;
  completed: number;
}

export interface GroupStat {
  key: string;
  name: string;
  color: string | null;
  total: number;
  completed: number;
}

export interface Habit {
  seriesId: string;
  title: string;
  recurrence: Recurrence;
  days: { date: string; done: boolean }[];
  currentStreak: number | null;
}

export interface Stats {
  from: string;
  to: string;
  createdCount: number;
  completedCount: number;
  previousCompletedCount: number;
  completionRate: number | null;
  overdueCount: number;
  bestStreak: { title: string; days: number } | null;
  perDay: DayStat[];
  byCategory: GroupStat[];
  byPriority: GroupStat[];
  habits: Habit[];
}
