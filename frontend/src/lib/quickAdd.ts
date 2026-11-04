import { addDays, setHours, setMinutes, startOfDay } from "date-fns";
import type { Category, Priority, Recurrence } from "../api/types";

/**
 * Parses the quick-add line, e.g.
 *   "Hesabat hazırla sabah 15:00 #iş !yüksək @İş hər gün"
 * into a title plus fields. Everything it recognizes is removed from the
 * title; everything else stays as typed.
 *
 *   #tag              → tag (several allowed)
 *   !yüksək !orta !aşağı (also !high !medium !low, !1 !2 !3) → priority
 *   @Kateqoriya       → category, matched by name (case-insensitive)
 *   bu gün | bugün | sabah | birigün | 15.11 | 15.11.2026 → day
 *   15:00             → time (without a day it means today)
 *   hər gün | iş günləri | hər həftə | hər ay → recurrence
 *
 * A day without a time gets 23:59 ("by the end of the day").
 */
export interface QuickAddResult {
  title: string;
  tags: string[];
  priority?: Priority;
  categoryId?: string;
  dueAt?: Date;
  recurrence?: Recurrence;
}

// \b does not work for letters like ə, ş, ı — use Unicode-aware boundaries.
const B = "(?<![\\p{L}\\p{N}_])";
const E = "(?![\\p{L}\\p{N}_])";
const re = (body: string) => new RegExp(`${B}${body}${E}`, "iu");

const PRIORITY_WORDS: [RegExp, Priority][] = [
  [new RegExp(`${B}!(?:yüksək|yuksek|high|1)${E}`, "iu"), "High"],
  [new RegExp(`${B}!(?:orta|medium|2)${E}`, "iu"), "Medium"],
  [new RegExp(`${B}!(?:aşağı|asagi|low|3)${E}`, "iu"), "Low"],
];

const RECURRENCE_WORDS: [RegExp, Recurrence][] = [
  [re("hər\\s+gün"), "Daily"],
  [re("iş\\s+günləri"), "Weekdays"],
  [re("hər\\s+həftə"), "Weekly"],
  [re("hər\\s+ay"), "Monthly"],
];

const DAY_WORDS: [RegExp, number][] = [
  [re("bu\\s*gün"), 0],
  [re("sabah"), 1],
  [re("birigün"), 2],
];

const DATE_RE = re("(\\d{1,2})\\.(\\d{1,2})(?:\\.(\\d{4}))?");
const TIME_RE = re("([01]?\\d|2[0-3]):([0-5]\\d)");
const TAG_RE = new RegExp(`${B}#([\\p{L}\\p{N}_-]+)`, "gu");
const CATEGORY_RE = new RegExp(`${B}@([\\p{L}\\p{N}_-]+)`, "u");

const lower = (s: string) => s.toLocaleLowerCase("az");

export function parseQuickAdd(input: string, categories: Category[], now = new Date()): QuickAddResult {
  let text = ` ${input} `;
  const result: QuickAddResult = { title: "", tags: [] };

  const take = (pattern: RegExp): RegExpMatchArray | null => {
    const m = text.match(pattern);
    if (m && m.index !== undefined) text = text.slice(0, m.index) + " " + text.slice(m.index + m[0].length);
    return m;
  };

  // Recurrence before days: "hər gün" must not leave a stray "gün".
  for (const [pattern, value] of RECURRENCE_WORDS) {
    if (take(pattern)) {
      result.recurrence = value;
      break;
    }
  }

  for (const [pattern, value] of PRIORITY_WORDS) {
    if (take(pattern)) {
      result.priority = value;
      break;
    }
  }

  const cat = take(CATEGORY_RE);
  if (cat?.[1]) {
    const wanted = lower(cat[1]);
    const match = categories.find((c) => lower(c.name) === wanted);
    if (match) result.categoryId = match.id;
    else text = text + ` @${cat[1]}`; // unknown category: keep the text
  }

  result.tags = [...text.matchAll(TAG_RE)].map((m) => m[1] ?? "").filter(Boolean);
  text = text.replace(TAG_RE, " ");

  let day: Date | undefined;
  for (const [pattern, offset] of DAY_WORDS) {
    if (take(pattern)) {
      day = addDays(startOfDay(now), offset);
      break;
    }
  }

  if (!day) {
    const m = take(DATE_RE);
    if (m) {
      const d = Number(m[1]);
      const mo = Number(m[2]);
      const y = m[3] ? Number(m[3]) : now.getFullYear();
      const candidate = new Date(y, mo - 1, d);
      if (candidate.getMonth() === mo - 1 && candidate.getDate() === d) day = candidate;
    }
  }

  const time = take(TIME_RE);
  if (time) {
    const base = day ?? startOfDay(now);
    result.dueAt = setMinutes(setHours(base, Number(time[1])), Number(time[2]));
  } else if (day) {
    result.dueAt = setMinutes(setHours(day, 23), 59);
  }

  result.title = text.replace(/\s+/g, " ").trim();
  return result;
}
