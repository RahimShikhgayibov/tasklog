import type { Category, Priority, Task } from "../api/types";
import { formatDue, formatTime } from "../lib/dates";
import { priorityLabel, recurrenceLabel } from "../lib/labels";
import { CheckIcon, ClockIcon, RepeatIcon, SubtaskIcon } from "./Icons";

export function PriorityPill({ priority }: { priority: Priority }) {
  return <span className={`pill pill-${priority.toLowerCase()}`}>{priorityLabel(priority)}</span>;
}

interface Props {
  task: Task;
  category?: Category;
  selected: boolean;
  overdue?: boolean;
  onSelect: () => void;
  onToggle: () => void;
}

export function TaskRow({ task, category, selected, overdue, onSelect, onToggle }: Props) {
  if (task.isCompleted) {
    return (
      <div className={selected ? "task-row done selected" : "task-row done"}>
        <button type="button" className="check-hit" aria-label="Tamamlanmamış kimi işarələ" onClick={onToggle}>
          <span className="check checked">
            <CheckIcon size={13} strokeWidth={3} />
          </span>
        </button>
        <button type="button" className="task-main" onClick={onSelect}>
          <span className="task-title">{task.title}</span>
        </button>
        {task.completedAt ? <span className="mono muted small">{formatTime(new Date(task.completedAt))}</span> : null}
      </div>
    );
  }

  const doneSubs = task.subTasks.filter((s) => s.isDone).length;

  return (
    <div className={selected ? "task-row selected" : "task-row"}>
      <button type="button" className="check-hit" aria-label="Tamamlandı kimi işarələ" onClick={onToggle}>
        <span className="check" />
      </button>
      {/* The whole text area opens the detail panel. */}
      <button type="button" className="task-main" onClick={onSelect} aria-pressed={selected}>
        <span className="task-title">{task.title}</span>
        <span className="task-meta">
          <PriorityPill priority={task.priority} />
          {task.dueAt ? (
            <span className={overdue ? "meta-item overdue" : "meta-item"}>
              <ClockIcon size={13} />
              {formatDue(task.dueAt)}
            </span>
          ) : null}
          {category ? (
            <span className="meta-item">
              <span className="dot" style={{ background: category.color }} />
              {category.name}
            </span>
          ) : null}
          {task.subTasks.length > 0 ? (
            <span className="meta-item">
              <SubtaskIcon size={13} />
              {doneSubs}/{task.subTasks.length}
            </span>
          ) : null}
          {task.recurrence !== "None" ? (
            <span className="meta-item recur">
              <RepeatIcon size={13} />
              {recurrenceLabel(task.recurrence)}
            </span>
          ) : null}
          {task.tags.map((t) => (
            <span key={t} className="tag-text">
              #{t}
            </span>
          ))}
        </span>
      </button>
    </div>
  );
}
