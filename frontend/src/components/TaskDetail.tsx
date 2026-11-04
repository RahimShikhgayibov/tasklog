import { useState, type FormEvent, type KeyboardEvent } from "react";
import { Link } from "react-router";
import { useActivity, useCategories, useTask, useTaskMutations } from "../api/hooks";
import type { Priority, Recurrence, Task, UpdateTaskInput } from "../api/types";
import { formatTime, fromLocalInputValue, toLocalInputValue } from "../lib/dates";
import { describeAction, describeChange, PRIORITIES, RECURRENCES } from "../lib/labels";
import { CheckIcon, CloseIcon, PlusIcon, TrashIcon } from "./Icons";

interface Props {
  taskId: string;
  onClose: () => void;
}

export function TaskDetail({ taskId, onClose }: Props) {
  const { data: task, isLoading, error } = useTask(taskId);

  if (isLoading) return <aside className="detail card muted">Yüklənir…</aside>;
  if (error || !task)
    return (
      <aside className="detail card">
        <p className="muted">Tapşırıq tapılmadı (silinmiş ola bilər).</p>
        <button type="button" className="btn btn-ghost btn-sm" onClick={onClose}>
          Bağla
        </button>
      </aside>
    );

  // Keyed by id so the text drafts reset when another task is selected.
  return <DetailForm key={task.id} task={task} onClose={onClose} />;
}

function toInput(t: Task): UpdateTaskInput {
  return {
    title: t.title,
    description: t.description,
    priority: t.priority,
    dueAt: t.dueAt,
    categoryId: t.categoryId,
    tags: t.tags,
    recurrence: t.recurrence,
  };
}

function DetailForm({ task, onClose }: { task: Task; onClose: () => void }) {
  const { data: categories = [] } = useCategories();
  const m = useTaskMutations();

  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description ?? "");
  const [tagText, setTagText] = useState(task.tags.map((t) => `#${t}`).join(" "));
  const [due, setDue] = useState(toLocalInputValue(task.dueAt));
  const [newSub, setNewSub] = useState("");

  const save = (patch: Partial<UpdateTaskInput>) => {
    const input = { ...toInput(task), ...patch };
    if (JSON.stringify(input) === JSON.stringify(toInput(task))) return; // nothing changed
    m.update.mutate({ id: task.id, input });
  };

  const saveTitle = () => {
    if (title.trim() && title.trim() !== task.title) save({ title: title.trim() });
    else setTitle(task.title);
  };

  const saveTags = () => {
    const tags = tagText
      .split(/[\s,]+/)
      .map((t) => t.replace(/^#/, ""))
      .filter(Boolean);
    save({ tags });
  };

  const addSub = (e: FormEvent) => {
    e.preventDefault();
    if (!newSub.trim()) return;
    m.addSubTask.mutate({ id: task.id, title: newSub.trim() }, { onSuccess: () => setNewSub("") });
  };

  const blurOnEnter = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") e.currentTarget.blur();
  };

  const doneSubs = task.subTasks.filter((s) => s.isDone).length;
  const error = m.update.error ?? m.addSubTask.error ?? m.complete.error;

  return (
    <aside className="detail card" aria-label="Tapşırıq detalları">
      <div className="row gap-8 align-start">
        <label className="sr-only" htmlFor="detail-title">
          Ad
        </label>
        <textarea
          id="detail-title"
          className="bare-input detail-title"
          rows={2}
          value={title}
          onChange={(e) => setTitle(e.target.value.replace(/\n/g, " "))}
          onBlur={saveTitle}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              e.currentTarget.blur();
            }
          }}
        />
        <button type="button" className="icon-btn" aria-label="Detalları bağla" onClick={onClose}>
          <CloseIcon />
        </button>
      </div>

      <div className="row gap-8 wrap">
        {task.isCompleted ? (
          <button type="button" className="btn btn-ghost btn-sm" onClick={() => m.reopen.mutate(task.id)}>
            Yenidən aç
          </button>
        ) : (
          <button type="button" className="btn btn-primary btn-sm" onClick={() => m.complete.mutate(task.id)}>
            <CheckIcon size={14} /> Tamamla
          </button>
        )}
        <button
          type="button"
          className="btn btn-ghost btn-sm danger"
          onClick={() => {
            if (window.confirm(`"${task.title}" silinsin? Tarixçədə qeydi qalacaq.`)) {
              m.remove.mutate(task.id, { onSuccess: onClose });
            }
          }}
        >
          <TrashIcon size={14} /> Sil
        </button>
      </div>

      <div className="field-grid">
        <label htmlFor="detail-priority">Prioritet</label>
        <select
          id="detail-priority"
          className="input"
          value={task.priority}
          onChange={(e) => save({ priority: e.target.value as Priority })}
        >
          {PRIORITIES.map((p) => (
            <option key={p.value} value={p.value}>
              {p.label}
            </option>
          ))}
        </select>

        <label htmlFor="detail-due">Son tarix</label>
        <div className="row gap-8">
          <input
            id="detail-due"
            type="datetime-local"
            className="input grow"
            value={due}
            onChange={(e) => setDue(e.target.value)}
            onBlur={() => save({ dueAt: fromLocalInputValue(due) })}
          />
          {task.dueAt ? (
            <button type="button" className="icon-btn" aria-label="Son tarixi sil" onClick={() => {
                setDue("");
                save({ dueAt: null });
              }}>
              <CloseIcon size={16} />
            </button>
          ) : null}
        </div>

        <label htmlFor="detail-category">Kateqoriya</label>
        <select
          id="detail-category"
          className="input"
          value={task.categoryId ?? ""}
          onChange={(e) => save({ categoryId: e.target.value || null })}
        >
          <option value="">Kateqoriyasız</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>

        <label htmlFor="detail-tags">Teqlər</label>
        <input
          id="detail-tags"
          className="input"
          placeholder="#backend #dotnet"
          value={tagText}
          onChange={(e) => setTagText(e.target.value)}
          onBlur={saveTags}
          onKeyDown={blurOnEnter}
        />

        <label htmlFor="detail-recurrence">Təkrar</label>
        <select
          id="detail-recurrence"
          className="input"
          value={task.recurrence}
          onChange={(e) => save({ recurrence: e.target.value as Recurrence })}
        >
          {RECURRENCES.map((r) => (
            <option key={r.value} value={r.value}>
              {r.label}
            </option>
          ))}
        </select>
      </div>

      <div className="stack-6">
        <label htmlFor="detail-description" className="section-label">
          Təsvir
        </label>
        <textarea
          id="detail-description"
          className="input"
          rows={3}
          placeholder="Qeyd əlavə et…"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          onBlur={() => save({ description: description.trim() || null })}
        />
      </div>

      {error ? <p className="form-error">{error.message}</p> : null}

      <section className="stack-6" aria-label="Alt tapşırıqlar">
        <div className="row space-between">
          <h3 className="section-label">Alt tapşırıqlar</h3>
          {task.subTasks.length > 0 ? (
            <span className="muted small">
              {doneSubs} / {task.subTasks.length}
            </span>
          ) : null}
        </div>
        {task.subTasks.length > 0 ? (
          <div className="progress thin">
            <div style={{ width: `${(doneSubs / task.subTasks.length) * 100}%` }} />
          </div>
        ) : null}
        <ul className="subtask-list">
          {task.subTasks.map((s) => (
            <li key={s.id} className="subtask">
              <label className="subtask-check">
                <input
                  type="checkbox"
                  checked={s.isDone}
                  onChange={(e) =>
                    m.updateSubTask.mutate({ id: task.id, subId: s.id, patch: { isDone: e.target.checked } })
                  }
                />
                <span className={s.isDone ? "strike muted" : ""}>{s.title}</span>
              </label>
              <button
                type="button"
                className="icon-btn subtle"
                aria-label={`"${s.title}" alt tapşırığını sil`}
                onClick={() => m.removeSubTask.mutate({ id: task.id, subId: s.id })}
              >
                <TrashIcon size={14} />
              </button>
            </li>
          ))}
        </ul>
        <form className="subtask-add" onSubmit={addSub}>
          <PlusIcon size={16} />
          <label className="sr-only" htmlFor="new-subtask">
            Alt tapşırıq əlavə et
          </label>
          <input
            id="new-subtask"
            className="bare-input grow"
            placeholder="Alt tapşırıq əlavə et"
            value={newSub}
            onChange={(e) => setNewSub(e.target.value)}
          />
        </form>
      </section>

      <TaskHistory taskId={task.id} />
    </aside>
  );
}

function TaskHistory({ taskId }: { taskId: string }) {
  const { data } = useActivity({ taskId, limit: 8 });
  const items = data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <section className="stack-8 history-mini" aria-label="Bu tapşırığın tarixçəsi">
      <div className="row space-between">
        <h3 className="section-label">Bu tapşırığın tarixçəsi</h3>
        <Link to="/log" className="link small">
          Hamısı
        </Link>
      </div>
      {items.length === 0 ? <p className="muted small">Hələ qeyd yoxdur.</p> : null}
      {items.map((e) => {
        const change = e.changes[0];
        const c = change ? describeChange(change) : null;
        return (
          <div key={e.id} className="history-mini-row">
            <span className="mono muted small">{formatTime(new Date(e.timestamp))}</span>
            <span>
              {e.entityType === "SubTask" ? <strong>{e.title}</strong> : null}
              {e.entityType === "SubTask" ? " — " : null}
              {describeAction(e)}
              {c && e.action === "Updated" ? (
                <span className="muted">
                  : {c.from} → {c.to}
                </span>
              ) : null}
            </span>
          </div>
        );
      })}
    </section>
  );
}
