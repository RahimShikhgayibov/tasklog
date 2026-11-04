import { addDays, endOfDay, startOfDay, subDays } from "date-fns";
import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import { useActivity, useActivitySummary, useJournal, useJournalList, useSaveJournal } from "../api/hooks";
import type { ActivityAction, ActivityEntry, JournalEntry } from "../api/types";
import {
  ArrowRightIcon,
  CheckIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  DownloadIcon,
  PencilIcon,
  PlusIcon,
  TrashIcon,
} from "../components/Icons";
import { formatDayHeading, formatMinutes, formatTime, fromDateKey, toDateKey } from "../lib/dates";
import { describeAction, describeChange } from "../lib/labels";
import { format } from "date-fns";
import { az } from "date-fns/locale";

const ACTION_FILTERS: { value: ActivityAction | undefined; label: string }[] = [
  { value: undefined, label: "Hamısı" },
  { value: "Created", label: "Yaradıldı" },
  { value: "Updated", label: "Dəyişdirildi" },
  { value: "Completed", label: "Tamamlandı" },
  { value: "Deleted", label: "Silindi" },
];

export function LogPage() {
  return (
    <div className="page">
      <header className="page-header">
        <div>
          <p className="eyebrow">Hər dəyişiklik avtomatik qeyd olunur</p>
          <h1 className="page-title">Tarixçə və jurnal</h1>
        </div>
        <a className="btn btn-outline" href={api.activity.exportUrl(subDays(new Date(), 30).toISOString())} download>
          <DownloadIcon size={16} /> CSV export (30 gün)
        </a>
      </header>

      <div className="split">
        <ActivityFeed />
        <Journal />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------

function ActivityFeed() {
  const [action, setAction] = useState<ActivityAction | undefined>(undefined);
  const { data, isLoading, error, fetchNextPage, hasNextPage, isFetchingNextPage } = useActivity({ action, limit: 50 });

  const items = data?.pages.flatMap((p) => p.items) ?? [];
  const groups = groupByDay(items);

  return (
    <section className="card list-col stack-16" aria-label="Fəaliyyət tarixçəsi">
      <div className="row space-between wrap gap-12">
        <h2 className="card-title">Fəaliyyət tarixçəsi</h2>
        <div className="chips" role="group" aria-label="Əməliyyat növü">
          {ACTION_FILTERS.map((f) => (
            <button
              key={f.label}
              type="button"
              className={action === f.value ? "chip active" : "chip"}
              aria-pressed={action === f.value}
              onClick={() => setAction(f.value)}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? <p className="muted">Yüklənir…</p> : null}
      {error ? <p className="form-error">{error.message}</p> : null}
      {!isLoading && items.length === 0 ? (
        <div className="empty">
          <p className="strong">Hələ qeyd yoxdur.</p>
          <p className="muted small">
            <Link to="/" className="link">
              Tapşırıq əlavə et
            </Link>{" "}
            — hər dəyişiklik burada görünəcək.
          </p>
        </div>
      ) : null}

      {groups.map(([day, entries]) => (
        <div key={day} className="stack-2">
          <h3 className="day-heading">{formatDayHeading(fromDateKey(day)).toLocaleUpperCase("az")}</h3>
          {entries.map((e) => (
            <ActivityRow key={e.id} entry={e} />
          ))}
        </div>
      ))}

      {hasNextPage ? (
        <button
          type="button"
          className="btn btn-outline btn-sm align-center"
          disabled={isFetchingNextPage}
          onClick={() => fetchNextPage()}
        >
          {isFetchingNextPage ? "Yüklənir…" : "Daha köhnə qeydləri yüklə"}
        </button>
      ) : null}
    </section>
  );
}

function groupByDay(items: ActivityEntry[]): [string, ActivityEntry[]][] {
  const map = new Map<string, ActivityEntry[]>();
  for (const e of items) {
    const key = toDateKey(new Date(e.timestamp));
    map.set(key, [...(map.get(key) ?? []), e]);
  }
  return [...map.entries()];
}

function ActivityRow({ entry: e }: { entry: ActivityEntry }) {
  const isSub = e.entityType === "SubTask";
  return (
    <div className="activity-row">
      <span className="mono muted small activity-time">{formatTime(new Date(e.timestamp))}</span>
      <span className={`badge badge-${e.action.toLowerCase()}`}>
        <ActionIcon action={e.action} />
      </span>
      <div className="activity-body">
        <div>
          <span className="strong">{e.title}</span> <span className="muted">— {describeAction(e)}</span>
          {isSub && e.parentTitle ? <span className="muted small"> ({e.parentTitle})</span> : null}
        </div>
        {e.changes.map((c) => {
          const d = describeChange(c);
          return (
            <div key={c.field} className="change">
              {e.changes.length > 1 ? <span className="muted small">{d.label}:</span> : null}
              <span className="change-from">{d.from}</span>
              <ArrowRightIcon size={14} className="muted" />
              <span className="change-to">{d.to}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ActionIcon({ action }: { action: ActivityAction }) {
  switch (action) {
    case "Created":
      return <PlusIcon size={14} />;
    case "Completed":
      return <CheckIcon size={14} />;
    case "Deleted":
      return <TrashIcon size={14} />;
    case "Reopened":
    case "Updated":
      return <PencilIcon size={14} />;
  }
}

// ---------------------------------------------------------------------------

function Journal() {
  const [date, setDate] = useState(() => toDateKey(new Date()));
  const { data: entry, isLoading } = useJournal(date);
  const { data: recent = [] } = useJournalList();

  const day = fromDateKey(date);
  const isToday = date === toDateKey(new Date());
  const others = recent.filter((j) => j.date !== date);

  return (
    <section className="side-col stack-16" aria-label="Gündəlik jurnal">
      <div className="card stack-16">
        <div className="row space-between">
          <h2 className="card-title">Gündəlik jurnal</h2>
          <div className="row gap-2">
            <button
              type="button"
              className="icon-btn"
              aria-label="Əvvəlki gün"
              onClick={() => setDate(toDateKey(subDays(day, 1)))}
            >
              <ChevronLeftIcon size={16} />
            </button>
            <span className="small strong nowrap">{format(day, "d MMMM", { locale: az })}</span>
            <button
              type="button"
              className="icon-btn"
              aria-label="Növbəti gün"
              disabled={isToday}
              onClick={() => setDate(toDateKey(addDays(day, 1)))}
            >
              <ChevronRightIcon size={16} />
            </button>
          </div>
        </div>

        <DaySummary day={day} />

        {isLoading ? (
          <p className="muted">Yüklənir…</p>
        ) : (
          <JournalForm key={date} date={date} entry={entry ?? null} />
        )}
      </div>

      {others.length > 0 ? (
        <div className="card stack-4">
          <h3 className="section-label">Əvvəlki qeydlər</h3>
          {others.map((j) => (
              <button key={j.date} type="button" className="journal-item" onClick={() => setDate(j.date)}>
                <span className="row space-between gap-8">
                  <span className="strong">{format(fromDateKey(j.date), "d MMMM, EEEE", { locale: az })}</span>
                  <span className="mono muted small">{formatMinutes(j.minutesSpent)}</span>
                </span>
                <span className="muted small ellipsis">{j.done ?? j.learned ?? j.tomorrowPlan ?? "—"}</span>
              </button>
            ))}
        </div>
      ) : null}
    </section>
  );
}

function DaySummary({ day }: { day: Date }) {
  const { data } = useActivitySummary(startOfDay(day).toISOString(), endOfDay(day).toISOString());
  if (!data) return null;

  return (
    <div className="summary-box">
      <p className="summary-label">TARİXÇƏDƏN AVTOMATİK</p>
      <div className="row wrap gap-16 small">
        <span>
          <strong>{data.completed}</strong> tamamlandı
        </span>
        <span>
          <strong>{data.created}</strong> yaradıldı
        </span>
        <span>
          <strong>{data.updated}</strong> dəyişiklik
        </span>
      </div>
      {data.completedTitles.length > 0 ? <p className="small">{data.completedTitles.join(" · ")}</p> : null}
    </div>
  );
}

function JournalForm({ date, entry }: { date: string; entry: JournalEntry | null }) {
  const [done, setDone] = useState(entry?.done ?? "");
  const [learned, setLearned] = useState(entry?.learned ?? "");
  const [blockers, setBlockers] = useState(entry?.blockers ?? "");
  const [plan, setPlan] = useState(entry?.tomorrowPlan ?? "");
  const [hours, setHours] = useState(entry?.minutesSpent != null ? String(Math.floor(entry.minutesSpent / 60)) : "");
  const [minutes, setMinutes] = useState(entry?.minutesSpent != null ? String(entry.minutesSpent % 60) : "");
  const save = useSaveJournal();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const h = hours ? Number(hours) : 0;
    const m = minutes ? Number(minutes) : 0;
    save.mutate({
      date,
      input: {
        done: done || null,
        learned: learned || null,
        blockers: blockers || null,
        tomorrowPlan: plan || null,
        minutesSpent: hours || minutes ? h * 60 + m : null,
      },
    });
  };

  return (
    <form className="stack-12" onSubmit={submit}>
      <TextField id="j-done" label="Bu gün nə etdim?" value={done} onChange={setDone} rows={3} placeholder="Qısa yaz…" />
      <TextField id="j-learned" label="Nə öyrəndim?" value={learned} onChange={setLearned} placeholder="Yeni bir şey…" />
      <TextField id="j-blockers" label="Problemlər / blokerlər" value={blockers} onChange={setBlockers} placeholder="Nə mane oldu?" />
      <TextField id="j-plan" label="Sabah üçün plan" value={plan} onChange={setPlan} placeholder="Sabah ilk iş…" />

      <fieldset className="time-field">
        <legend>Sərf olunan vaxt</legend>
        <div className="row gap-8 align-end wrap">
          <label className="inline-field">
            <input
              type="number"
              min={0}
              max={24}
              className="input num"
              value={hours}
              onChange={(e) => setHours(e.target.value)}
            />
            saat
          </label>
          <label className="inline-field">
            <input
              type="number"
              min={0}
              max={59}
              className="input num"
              value={minutes}
              onChange={(e) => setMinutes(e.target.value)}
            />
            dəq
          </label>
          <span className="grow" />
          <button type="submit" className="btn btn-primary" disabled={save.isPending}>
            {save.isPending ? "Saxlanılır…" : "Yadda saxla"}
          </button>
        </div>
      </fieldset>
      {save.isSuccess ? <p className="muted small" role="status">Saxlanıldı.</p> : null}
      {save.error ? <p className="form-error">{save.error.message}</p> : null}
    </form>
  );
}

function TextField(props: {
  id: string;
  label: string;
  value: string;
  onChange: (v: string) => void;
  rows?: number;
  placeholder?: string;
}) {
  return (
    <div className="stack-6">
      <label htmlFor={props.id} className="field-label">
        {props.label}
      </label>
      <textarea
        id={props.id}
        className="input"
        rows={props.rows ?? 2}
        placeholder={props.placeholder}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
      />
    </div>
  );
}
