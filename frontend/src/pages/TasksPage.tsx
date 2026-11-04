import { useEffect, useMemo, useState, type ComponentProps } from "react";
import { useParams, useSearchParams } from "react-router";
import type { TaskQuery } from "../api/client";
import { useCategories, useTaskMutations, useTasks } from "../api/hooks";
import type { Category, Task } from "../api/types";
import { SearchIcon } from "../components/Icons";
import { QuickAdd } from "../components/QuickAdd";
import { TaskDetail } from "../components/TaskDetail";
import { TaskRow } from "../components/TaskRow";
import { formatDayHeading, formatLongDate, todayRange, toDateKey } from "../lib/dates";

export type TaskView = "today" | "upcoming" | "all" | "completed" | "category";

type Filter = "all" | "high" | "overdue" | "recurring";

const FILTERS: { value: Filter; label: string }[] = [
  { value: "all", label: "Hamısı" },
  { value: "high", label: "Yüksək prioritet" },
  { value: "overdue", label: "Gecikən" },
  { value: "recurring", label: "Təkrarlanan" },
];

export function TasksPage({ view }: { view: TaskView }) {
  const { id: categoryId } = useParams();
  const [params, setParams] = useSearchParams();
  const selectedId = params.get("task");
  const tag = params.get("tag") ?? undefined;

  const [filter, setFilter] = useState<Filter>("all");
  const [searchText, setSearchText] = useState("");
  const search = useDebounced(searchText.trim(), 250);

  const { data: categories = [] } = useCategories();
  const categoryById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const category = categoryId ? categoryById.get(categoryId) : undefined;

  // Recomputed each render; cheap. Day boundaries are local, sent as UTC.
  const now = new Date();
  const { start, end } = todayRange(now);

  const base: TaskQuery = { search: search || undefined, tag };
  const openQuery: TaskQuery = useMemo(() => {
    switch (view) {
      case "today":
        return { ...base, status: "open", dueBefore: end.toISOString() };
      case "upcoming":
        return { ...base, status: "open", dueAfter: end.toISOString() };
      case "completed":
        return { ...base, status: "completed", limit: 200 };
      case "category":
        return { ...base, status: "open", categoryId };
      default:
        return { ...base, status: "open" };
    }
  }, [view, categoryId, search, tag, toDateKey(now)]);

  const doneTodayQuery: TaskQuery = { status: "completed", completedAfter: start.toISOString(), search: base.search, tag };

  const main = useTasks(openQuery);
  const doneToday = useTasks(doneTodayQuery, view === "today");
  const m = useTaskMutations();

  const select = (id: string | null) => {
    const next = new URLSearchParams(params);
    if (id) next.set("task", id);
    else next.delete("task");
    setParams(next, { replace: true });
  };

  const toggle = (t: Task) => (t.isCompleted ? m.reopen.mutate(t.id) : m.complete.mutate(t.id));

  const isOverdue = (t: Task) => !t.isCompleted && !!t.dueAt && new Date(t.dueAt) < now;

  const applyFilter = (list: Task[]) =>
    list.filter((t) => {
      if (filter === "high") return t.priority === "High";
      if (filter === "overdue") return isOverdue(t);
      if (filter === "recurring") return t.recurrence !== "None";
      return true;
    });

  const tasks = applyFilter(main.data ?? []);
  const completedToday = applyFilter(doneToday.data ?? []);

  const rowProps = (t: Task) => ({
    task: t,
    category: t.categoryId ? categoryById.get(t.categoryId) : undefined,
    selected: t.id === selectedId,
    overdue: isOverdue(t),
    onSelect: () => select(t.id === selectedId ? null : t.id),
    onToggle: () => toggle(t),
  });

  const heading = headingFor(view, category);

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <p className="eyebrow">{view === "today" ? formatLongDate(now) : heading.sub}</p>
          <h1 className="page-title">
            {category ? <span className="swatch lg" style={{ background: category.color }} /> : null}
            {heading.title}
          </h1>
        </div>
        <label className="search">
          <SearchIcon size={16} />
          <span className="sr-only">Axtar</span>
          <input
            type="search"
            placeholder="Tapşırıq axtar…"
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
          />
        </label>
      </header>

      <div className="split">
        <section className="list-col" aria-label="Tapşırıq siyahısı">
          {view !== "completed" ? (
            <QuickAdd
              defaults={{
                categoryId: view === "category" ? categoryId : undefined,
                // On "Today", a line without a date still lands in today.
                dueAt: view === "today" ? new Date(end.getTime() - 59_999).toISOString() : undefined,
              }}
              onCreated={(id) => select(id)}
            />
          ) : null}

          <div className="row space-between wrap gap-12">
            <div className="chips" role="group" aria-label="Filtr">
              {FILTERS.map((f) => (
                <button
                  key={f.value}
                  type="button"
                  className={filter === f.value ? "chip active" : "chip"}
                  aria-pressed={filter === f.value}
                  onClick={() => setFilter(f.value)}
                >
                  {f.label}
                </button>
              ))}
              {tag ? (
                <button
                  type="button"
                  className="chip active"
                  onClick={() => {
                    const next = new URLSearchParams(params);
                    next.delete("tag");
                    setParams(next);
                  }}
                >
                  #{tag} ✕
                </button>
              ) : null}
            </div>
          </div>

          {main.isLoading ? <p className="muted">Yüklənir…</p> : null}
          {main.error ? <ErrorBox message={main.error.message} /> : null}

          {view === "today" ? (
            <TodayLists
              tasks={tasks}
              completed={completedToday}
              isOverdue={isOverdue}
              rowProps={rowProps}
              loaded={!main.isLoading}
            />
          ) : view === "upcoming" || view === "completed" ? (
            <GroupedByDay tasks={tasks} by={view === "completed" ? "completedAt" : "dueAt"} rowProps={rowProps} />
          ) : (
            <TaskSection title={null} tasks={tasks} rowProps={rowProps} empty={!main.isLoading} />
          )}
        </section>

        {selectedId ? <TaskDetail taskId={selectedId} onClose={() => select(null)} /> : null}
      </div>
    </div>
  );
}

type RowProps = (t: Task) => ComponentProps<typeof TaskRow>;

function TodayLists({
  tasks,
  completed,
  isOverdue,
  rowProps,
  loaded,
}: {
  tasks: Task[];
  completed: Task[];
  isOverdue: (t: Task) => boolean;
  rowProps: RowProps;
  loaded: boolean;
}) {
  const overdue = tasks.filter(isOverdue);
  const today = tasks.filter((t) => !isOverdue(t));
  const total = tasks.length + completed.length;
  const pct = total === 0 ? 0 : Math.round((completed.length / total) * 100);

  return (
    <>
      {total > 0 ? (
        <div className="progress-line">
          <span className="muted small nowrap">
            {completed.length} / {total} tamamlandı
          </span>
          <div className="progress">
            <div style={{ width: `${pct}%` }} />
          </div>
          <span className="small strong">{pct}%</span>
        </div>
      ) : null}

      {overdue.length > 0 ? (
        <TaskSection title={`Gecikən · ${overdue.length}`} tone="danger" tasks={overdue} rowProps={rowProps} />
      ) : null}
      <TaskSection title={`Bu gün · ${today.length}`} tasks={today} rowProps={rowProps} empty={loaded && total === 0} />
      {completed.length > 0 ? (
        <TaskSection title={`Tamamlanan · ${completed.length}`} tasks={completed} rowProps={rowProps} />
      ) : null}
    </>
  );
}

function GroupedByDay({ tasks, by, rowProps }: { tasks: Task[]; by: "dueAt" | "completedAt"; rowProps: RowProps }) {
  const groups = new Map<string, Task[]>();
  for (const t of tasks) {
    const iso = t[by];
    if (!iso) continue;
    const key = toDateKey(new Date(iso));
    groups.set(key, [...(groups.get(key) ?? []), t]);
  }

  if (groups.size === 0) return <EmptyState />;

  return (
    <>
      {[...groups.entries()].map(([key, list]) => (
        <TaskSection
          key={key}
          title={`${formatDayHeading(new Date(list[0]![by]!))} · ${list.length}`}
          tasks={list}
          rowProps={rowProps}
        />
      ))}
    </>
  );
}

function TaskSection({
  title,
  tasks,
  rowProps,
  tone,
  empty,
}: {
  title: string | null;
  tasks: Task[];
  rowProps: RowProps;
  tone?: "danger";
  empty?: boolean;
}) {
  return (
    <div className="stack-8">
      {title ? <h2 className={tone === "danger" ? "section-title danger" : "section-title"}>{title}</h2> : null}
      {tasks.map((t) => (
        <TaskRow key={t.id} {...rowProps(t)} />
      ))}
      {tasks.length === 0 && empty ? <EmptyState /> : null}
    </div>
  );
}

function EmptyState() {
  return (
    <div className="empty">
      <p className="strong">Burada tapşırıq yoxdur.</p>
      <p className="muted small">Yuxarıdakı sətirdən yenisini əlavə et.</p>
    </div>
  );
}

function ErrorBox({ message }: { message: string }) {
  return (
    <div className="error-box" role="alert">
      <strong>Serverlə əlaqə alınmadı.</strong> Backend işləyir? ({message})
    </div>
  );
}

function headingFor(view: TaskView, category?: Category): { title: string; sub: string } {
  switch (view) {
    case "today":
      return { title: "Bu gün", sub: "" };
    case "upcoming":
      return { title: "Gələcək", sub: "Bu gündən sonrakı son tarixlər" };
    case "completed":
      return { title: "Tamamlananlar", sub: "Son tamamlanan 200 tapşırıq" };
    case "category":
      return { title: category?.name ?? "Kateqoriya", sub: "Açıq tapşırıqlar" };
    default:
      return { title: "Bütün tapşırıqlar", sub: "Bütün açıq tapşırıqlar" };
  }
}

function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}
