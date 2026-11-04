import {
  addDays,
  differenceInCalendarDays,
  endOfDay,
  format,
  isSameDay,
  startOfDay,
} from "date-fns";
import { az } from "date-fns/locale";

// All API dates are UTC ISO strings. Everything here works in the browser's
// local time zone, which is what the user sees.

/** A deadline without a time is stored as 23:59 local. */
export function isEndOfDay(d: Date): boolean {
  return d.getHours() === 23 && d.getMinutes() === 59;
}

export function todayRange(now = new Date()) {
  return { start: startOfDay(now), end: endOfDay(now) };
}

/** yyyy-MM-dd in local time (the journal/stats day key). */
export function toDateKey(d: Date): string {
  return format(d, "yyyy-MM-dd");
}

export function fromDateKey(key: string): Date {
  const [y, m, d] = key.split("-").map(Number);
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1);
}

export function formatTime(d: Date): string {
  return format(d, "HH:mm");
}

/** "Bu gün, 18:00", "Dünən", "Sabah, 09:00", "Cümə", "15 noy". */
export function formatDue(iso: string, now = new Date()): string {
  const d = new Date(iso);
  const diff = differenceInCalendarDays(d, now);
  const time = isEndOfDay(d) ? "" : `, ${formatTime(d)}`;

  let day: string;
  if (diff === 0) day = "Bu gün";
  else if (diff === -1) day = "Dünən";
  else if (diff === 1) day = "Sabah";
  else if (diff > 1 && diff < 7) day = capitalize(format(d, "EEEE", { locale: az }));
  else day = format(d, d.getFullYear() === now.getFullYear() ? "d MMM" : "d MMM yyyy", { locale: az });

  return day + time;
}

export function formatDayHeading(d: Date, now = new Date()): string {
  const label = format(d, "d MMMM", { locale: az });
  if (isSameDay(d, now)) return `Bu gün — ${label}`;
  if (isSameDay(d, addDays(now, -1))) return `Dünən — ${label}`;
  if (isSameDay(d, addDays(now, 1))) return `Sabah — ${label}`;
  return `${capitalize(format(d, "EEEE", { locale: az }))} — ${label}`;
}

export function formatLongDate(d: Date): string {
  return capitalize(format(d, "EEEE, d MMMM yyyy", { locale: az }));
}

export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return isEndOfDay(d) ? format(d, "d MMM yyyy", { locale: az }) : format(d, "d MMM yyyy, HH:mm", { locale: az });
}

export function formatMinutes(total: number | null): string {
  if (total === null) return "";
  const h = Math.floor(total / 60);
  const m = total % 60;
  if (h === 0) return `${m} dəq`;
  return m === 0 ? `${h} saat` : `${h} saat ${m} dəq`;
}

/** Value for <input type="datetime-local"> from a UTC ISO string. */
export function toLocalInputValue(iso: string | null): string {
  if (!iso) return "";
  return format(new Date(iso), "yyyy-MM-dd'T'HH:mm");
}

/** <input type="datetime-local"> value (local) → UTC ISO string. */
export function fromLocalInputValue(value: string): string | null {
  if (!value) return null;
  const d = new Date(value); // parsed as local time
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

function capitalize(s: string): string {
  return s.charAt(0).toLocaleUpperCase("az") + s.slice(1);
}
