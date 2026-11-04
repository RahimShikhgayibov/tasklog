import { format, subDays } from "date-fns";
import { az } from "date-fns/locale";
import { useState } from "react";
import { useStats } from "../api/hooks";
import type { DayStat, GroupStat, Habit, Stats } from "../api/types";
import { CheckIcon } from "../components/Icons";
import { fromDateKey, toDateKey } from "../lib/dates";
import { priorityLabel } from "../lib/labels";
import type { Priority } from "../api/types";

type Period = "week" | "month" | "year";

const PERIODS: { value: Period; label: string; days: number }[] = [
  { value: "week", label: "Həftə", days: 7 },
  { value: "month", label: "Ay", days: 30 },
  { value: "year", label: "İl", days: 365 },
];

const PRIORITY_COLORS: Record<string, string> = { High: "#C2410C", Medium: "#B7791F", Low: "#6B7280" };

export function StatsPage() {
  const [period, setPeriod] = useState<Period>("week");
  const days = PERIODS.find((p) => p.value === period)!.days;

  const today = new Date();
  const from = toDateKey(subDays(today, days - 1));
  const to = toDateKey(today);
  const { data, isLoading, error } = useStats(from, to);

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <p className="eyebrow">
            Son {days} gün · {format(fromDateKey(from), "d MMM", { locale: az })} –{" "}
            {format(today, "d MMM", { locale: az })}
          </p>
          <h1 className="page-title">Statistika</h1>
        </div>
        <div className="segmented" role="group" aria-label="Dövr">
          {PERIODS.map((p) => (
            <button
              key={p.value}
              type="button"
              aria-pressed={period === p.value}
              className={period === p.value ? "active" : ""}
              onClick={() => setPeriod(p.value)}
            >
              {p.label}
            </button>
          ))}
        </div>
      </header>

      {isLoading ? <p className="muted">Yüklənir…</p> : null}
      {error ? <p className="form-error">{error.message}</p> : null}
      {data ? <StatsBody data={data} period={period} /> : null}
    </div>
  );
}

function StatsBody({ data, period }: { data: Stats; period: Period }) {
  const delta = data.completedCount - data.previousCompletedCount;
  const rate = data.completionRate === null ? "—" : `${Math.round(data.completionRate * 100)}%`;
  const completedOfCreated = data.byPriority.reduce((s, g) => s + g.completed, 0);

  return (
    <div className="stack-20">
      <div className="kpis">
        <Kpi
          label="Tamamlanan"
          value={String(data.completedCount)}
          note={delta === 0 ? "Əvvəlki dövrlə eyni" : `${delta > 0 ? "+" : ""}${delta} əvvəlki dövrə görə`}
          tone={delta > 0 ? "good" : undefined}
        />
        <Kpi label="Tamamlanma faizi" value={rate} note={`${data.createdCount} yaradılandan ${completedOfCreated}-i`} />
        <Kpi
          label="Gecikən"
          value={String(data.overdueCount)}
          note="Son tarixi keçmiş, açıq"
          tone={data.overdueCount > 0 ? "danger" : undefined}
        />
        <Kpi
          label="Ən uzun seriya"
          value={data.bestStreak ? `${data.bestStreak.days} gün` : "—"}
          note={data.bestStreak?.title ?? "Təkrarlanan tapşırıq yoxdur"}
        />
      </div>

      <div className="split">
        <section className="card list-col stack-16" aria-label="Gün üzrə">
          <div className="row space-between wrap gap-12">
            <h2 className="card-title">{period === "year" ? "Ay üzrə" : "Gün üzrə"}: yaradılan və tamamlanan</h2>
            <div className="row gap-16 small">
              <span className="legend">
                <span className="swatch" style={{ background: "var(--accent-soft-strong)" }} />
                Yaradılan
              </span>
              <span className="legend">
                <span className="swatch" style={{ background: "var(--accent)" }} />
                Tamamlanan
              </span>
            </div>
          </div>
          <BarChart buckets={period === "year" ? byMonth(data.perDay) : byDay(data.perDay, period)} />
        </section>

        <section className="card side-col stack-16" aria-label="Bölgü">
          <h2 className="card-title">Kateqoriya üzrə</h2>
          {data.byCategory.length === 0 ? <p className="muted small">Bu dövrdə yaradılmış tapşırıq yoxdur.</p> : null}
          {data.byCategory.map((g) => (
            <GroupBar key={g.key} group={g} color={g.color ?? "#6B7280"} />
          ))}
          <h2 className="card-title">Prioritet üzrə</h2>
          {data.byPriority.map((g) => (
            <GroupBar
              key={g.key}
              group={{ ...g, name: priorityLabel(g.key as Priority) }}
              color={PRIORITY_COLORS[g.key] ?? "#6B7280"}
              pill={g.key.toLowerCase()}
            />
          ))}
        </section>
      </div>

      <Habits habits={data.habits} />
    </div>
  );
}

function Kpi({ label, value, note, tone }: { label: string; value: string; note: string; tone?: "good" | "danger" }) {
  return (
    <div className="card kpi">
      <span className="muted small strong">{label}</span>
      <span className={tone === "danger" ? "kpi-value danger" : "kpi-value"}>{value}</span>
      <span className={tone === "good" ? "small good strong" : "small muted"}>{note}</span>
    </div>
  );
}

interface Bucket {
  key: string;
  label: string;
  sub: string;
  created: number;
  completed: number;
}

function byDay(days: DayStat[], period: Period): Bucket[] {
  return days.map((d) => {
    const date = fromDateKey(d.date);
    return {
      key: d.date,
      label: period === "week" ? format(date, "EEEEEE", { locale: az }) : format(date, "d"),
      sub: period === "week" ? format(date, "d") : "",
      created: d.created,
      completed: d.completed,
    };
  });
}

function byMonth(days: DayStat[]): Bucket[] {
  const map = new Map<string, Bucket>();
  for (const d of days) {
    const date = fromDateKey(d.date);
    const key = format(date, "yyyy-MM");
    const b = map.get(key) ?? { key, label: format(date, "LLL", { locale: az }), sub: "", created: 0, completed: 0 };
    b.created += d.created;
    b.completed += d.completed;
    map.set(key, b);
  }
  return [...map.values()];
}

function BarChart({ buckets }: { buckets: Bucket[] }) {
  const max = Math.max(1, ...buckets.map((b) => Math.max(b.created, b.completed)));
  const dense = buckets.length > 14;

  return (
    <div className="chart">
      <div
        className="chart-bars"
        role="img"
        aria-label={`Qrafik: ${buckets.map((b) => `${b.label} ${b.sub} — ${b.completed} tamamlandı, ${b.created} yaradıldı`).join("; ")}`}
      >
        {buckets.map((b) => (
          <div key={b.key} className="chart-col" title={`${b.label} ${b.sub}: ${b.completed} / ${b.created}`}>
            {!dense ? <span className="small strong">{b.completed}</span> : null}
            <div className="chart-pair">
              <div className="bar created" style={{ height: `${(b.created / max) * 100}%` }} />
              <div className="bar completed" style={{ height: `${(b.completed / max) * 100}%` }} />
            </div>
          </div>
        ))}
      </div>
      <div className="chart-labels" aria-hidden="true">
        {buckets.map((b, i) => (
          <div key={b.key} className="chart-label">
            {!dense || i % 5 === 0 ? (
              <>
                <span className="small strong">{b.label}</span>
                {b.sub ? <span className="tiny muted">{b.sub}</span> : null}
              </>
            ) : null}
          </div>
        ))}
      </div>
    </div>
  );
}

function GroupBar({ group, color, pill }: { group: GroupStat; color: string; pill?: string }) {
  const pct = group.total === 0 ? 0 : (group.completed / group.total) * 100;
  return (
    <div className="stack-6">
      <div className="row space-between small">
        {pill ? (
          <span className={`pill pill-${pill}`}>{group.name}</span>
        ) : (
          <span className="row gap-8 strong">
            <span className="swatch" style={{ background: color }} />
            {group.name}
          </span>
        )}
        <span className="muted">
          <strong className="ink">{group.completed}</strong> / {group.total}
        </span>
      </div>
      <div className="track">
        <div style={{ width: `${pct}%`, background: color }} />
      </div>
    </div>
  );
}

function Habits({ habits }: { habits: Habit[] }) {
  // The grid always shows the last 7 days, whatever the period.
  const lastDays = habits[0]?.days.slice(-7) ?? [];

  return (
    <section className="card stack-12" aria-label="Təkrarlanan tapşırıqlar">
      <div className="row space-between wrap gap-12">
        <h2 className="card-title">Təkrarlanan tapşırıqlar — vərdiş izləmə</h2>
        <span className="small muted">Dolu = edildi · Boş = edilmədi</span>
      </div>
      {habits.length === 0 ? (
        <p className="muted small">
          Hələ təkrarlanan tapşırıq yoxdur. Tapşırığa “hər gün” yaz və ya detallarda Təkrar seç.
        </p>
      ) : (
        <div className="scroll-x">
          <table className="habits">
            <thead>
              <tr>
                <th scope="col">Tapşırıq</th>
                {lastDays.map((d) => (
                  <th key={d.date} scope="col" className="center">
                    {format(fromDateKey(d.date), "EEEEEE", { locale: az })}
                    <span className="tiny muted block">{format(fromDateKey(d.date), "d")}</span>
                  </th>
                ))}
                <th scope="col" className="right">
                  Seriya
                </th>
              </tr>
            </thead>
            <tbody>
              {habits.map((h) => (
                <tr key={h.seriesId}>
                  <th scope="row">
                    <span className="strong">{h.title}</span>
                  </th>
                  {h.days.slice(-7).map((d) => (
                    <td key={d.date} className="center">
                      <span
                        className={d.done ? "habit-cell on" : "habit-cell"}
                        aria-label={`${d.date}: ${d.done ? "edildi" : "edilmədi"}`}
                        role="img"
                      >
                        {d.done ? <CheckIcon size={12} strokeWidth={3.2} /> : null}
                      </span>
                    </td>
                  ))}
                  <td className="right strong">{h.currentStreak === null ? "—" : `${h.currentStreak} gün`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
