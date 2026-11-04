import type { ActivityEntry, FieldChange, Priority, Recurrence } from "../api/types";
import { formatDateTime } from "./dates";

export const PRIORITIES: { value: Priority; label: string }[] = [
  { value: "High", label: "Yüksək" },
  { value: "Medium", label: "Orta" },
  { value: "Low", label: "Aşağı" },
];

export const RECURRENCES: { value: Recurrence; label: string }[] = [
  { value: "None", label: "Təkrarlanmır" },
  { value: "Daily", label: "Hər gün" },
  { value: "Weekdays", label: "İş günləri" },
  { value: "Weekly", label: "Hər həftə" },
  { value: "Monthly", label: "Hər ay" },
];

export const priorityLabel = (p: Priority) => PRIORITIES.find((x) => x.value === p)?.label ?? p;
export const recurrenceLabel = (r: Recurrence) => RECURRENCES.find((x) => x.value === r)?.label ?? r;

const FIELD_LABELS: Record<string, string> = {
  Title: "Ad",
  Description: "Təsvir",
  Priority: "Prioritet",
  DueAt: "Son tarix",
  CategoryId: "Kateqoriya",
  Tags: "Teqlər",
  Recurrence: "Təkrar",
};

export const fieldLabel = (field: string) => FIELD_LABELS[field] ?? field;

/** Turns a stored history value (enum name, ISO date, "a, b") into display text. */
export function formatChangeValue(field: string, value: string | null): string {
  if (value === null || value === "") return "—";
  switch (field) {
    case "Priority":
      return priorityLabel(value as Priority);
    case "Recurrence":
      return recurrenceLabel(value as Recurrence);
    case "DueAt":
      return formatDateTime(value);
    case "Tags":
      return value
        .split(",")
        .map((t) => `#${t.trim()}`)
        .join(" ");
    case "Description":
      return value.length > 60 ? `${value.slice(0, 60)}…` : value;
    default:
      return value;
  }
}

/** Short verb phrase for an entry, e.g. "prioritet dəyişdi". */
export function describeAction(e: ActivityEntry): string {
  const sub = e.entityType === "SubTask";
  switch (e.action) {
    case "Created":
      return sub ? "alt tapşırıq əlavə edildi" : "yaradıldı";
    case "Completed":
      return sub ? "alt tapşırıq tamamlandı" : "tamamlandı";
    case "Reopened":
      return sub ? "alt tapşırıq yenidən açıldı" : "yenidən açıldı";
    case "Deleted":
      return sub ? "alt tapşırıq silindi" : "silindi";
    case "Updated": {
      const first = e.changes[0];
      if (e.changes.length === 1 && first) return `${fieldLabel(first.field).toLocaleLowerCase("az")} dəyişdi`;
      return "dəyişdirildi";
    }
  }
}

export function describeChange(c: FieldChange) {
  return {
    label: fieldLabel(c.field),
    from: formatChangeValue(c.field, c.from),
    to: formatChangeValue(c.field, c.to),
  };
}
