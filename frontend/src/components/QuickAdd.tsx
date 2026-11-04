import { useMemo, useState, type FormEvent } from "react";
import { useCategories, useTaskMutations } from "../api/hooks";
import type { CreateTaskInput } from "../api/types";
import { formatDue } from "../lib/dates";
import { priorityLabel, recurrenceLabel } from "../lib/labels";
import { parseQuickAdd } from "../lib/quickAdd";
import { PlusIcon } from "./Icons";

interface Props {
  /** Defaults applied when the line itself doesn't say (e.g. the current category page). */
  defaults?: Partial<CreateTaskInput>;
  onCreated?: (id: string) => void;
}

export function QuickAdd({ defaults, onCreated }: Props) {
  const [text, setText] = useState("");
  const { data: categories = [] } = useCategories();
  const { create } = useTaskMutations();

  const parsed = useMemo(() => parseQuickAdd(text, categories), [text, categories]);
  const category = categories.find((c) => c.id === (parsed.categoryId ?? defaults?.categoryId));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!parsed.title) return;
    create.mutate(
      {
        ...defaults,
        title: parsed.title,
        tags: parsed.tags,
        priority: parsed.priority ?? defaults?.priority,
        categoryId: parsed.categoryId ?? defaults?.categoryId ?? null,
        dueAt: parsed.dueAt?.toISOString() ?? defaults?.dueAt ?? null,
        recurrence: parsed.recurrence ?? defaults?.recurrence,
      },
      {
        onSuccess: (task) => {
          setText("");
          onCreated?.(task.id);
        },
      },
    );
  };

  const hasHints =
    parsed.priority || parsed.dueAt || category || parsed.tags.length > 0 || parsed.recurrence;

  return (
    <form className="card quick-add" onSubmit={submit}>
      <div className="quick-add-line">
        <PlusIcon className="accent" />
        <label className="sr-only" htmlFor="quick-add">
          Sürətli tapşırıq əlavə et
        </label>
        <input
          id="quick-add"
          className="bare-input grow"
          placeholder="Tapşırıq yaz… məs: Hesabat hazırla sabah 15:00 #iş !yüksək @İş"
          value={text}
          onChange={(e) => setText(e.target.value)}
          autoComplete="off"
        />
        <button type="submit" className="btn btn-dark btn-sm" disabled={!parsed.title || create.isPending}>
          Əlavə et
        </button>
      </div>

      {hasHints ? (
        <div className="quick-add-hints" aria-live="polite">
          {parsed.priority ? <span className="chip-static">{priorityLabel(parsed.priority)}</span> : null}
          {parsed.dueAt ? <span className="chip-static">{formatDue(parsed.dueAt.toISOString())}</span> : null}
          {category ? (
            <span className="chip-static">
              <span className="dot" style={{ background: category.color }} />
              {category.name}
            </span>
          ) : null}
          {parsed.recurrence ? <span className="chip-static">{recurrenceLabel(parsed.recurrence)}</span> : null}
          {parsed.tags.map((t) => (
            <span key={t} className="chip-static tag-text">
              #{t}
            </span>
          ))}
        </div>
      ) : (
        <p className="quick-add-help">
          <code>#teq</code> <code>!yüksək</code> <code>@Kateqoriya</code> <code>sabah 15:00</code>{" "}
          <code>20.11</code> <code>hər gün</code>
        </p>
      )}

      {create.error ? <p className="form-error">{create.error.message}</p> : null}
    </form>
  );
}
