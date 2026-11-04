import { useState, type FormEvent } from "react";
import { NavLink } from "react-router";
import { useCategories, useCategoryMutations, useTaskCounts } from "../api/hooks";
import { todayRange } from "../lib/dates";
import {
  CalendarIcon,
  ChartIcon,
  CheckCircleIcon,
  CheckIcon,
  HistoryIcon,
  InboxIcon,
  PlusIcon,
  SunIcon,
} from "./Icons";

const PALETTE = ["#3451D1", "#C2410C", "#0F766E", "#7C3AED", "#B7791F", "#BE185D"];

const navClass = ({ isActive }: { isActive: boolean }) => (isActive ? "nav-link active" : "nav-link");

export function Sidebar() {
  const todayEnd = todayRange().end.toISOString();
  const { data: counts } = useTaskCounts(todayEnd);
  const { data: categories = [] } = useCategories();

  return (
    <nav className="sidebar" aria-label="Əsas menyu">
      <div className="brand">
        <span className="brand-mark">
          <CheckIcon size={18} />
        </span>
        <span className="brand-name">Tasklog</span>
      </div>

      <div className="nav-group">
        <NavLink to="/" end className={navClass}>
          <SunIcon />
          <span className="nav-label">Bu gün</span>
          {counts ? <span className="nav-count">{counts.today}</span> : null}
        </NavLink>
        <NavLink to="/upcoming" className={navClass}>
          <CalendarIcon />
          <span className="nav-label">Gələcək</span>
          {counts ? <span className="nav-count">{counts.upcoming}</span> : null}
        </NavLink>
        <NavLink to="/all" className={navClass}>
          <InboxIcon />
          <span className="nav-label">Bütün tapşırıqlar</span>
          {counts ? <span className="nav-count">{counts.allOpen}</span> : null}
        </NavLink>
        <NavLink to="/completed" className={navClass}>
          <CheckCircleIcon />
          <span className="nav-label">Tamamlananlar</span>
        </NavLink>
      </div>

      <div className="nav-group">
        <p className="nav-heading">KATEQORİYALAR</p>
        {categories.map((c) => (
          <NavLink key={c.id} to={`/category/${c.id}`} className={navClass}>
            <span className="swatch" style={{ background: c.color }} />
            <span className="nav-label">{c.name}</span>
            <span className="nav-count">{counts?.byCategory[c.id] ?? 0}</span>
          </NavLink>
        ))}
        <AddCategory nextColor={PALETTE[categories.length % PALETTE.length] ?? "#3451D1"} />
      </div>

      <div className="nav-group">
        <p className="nav-heading">JURNAL</p>
        <NavLink to="/log" className={navClass}>
          <HistoryIcon />
          <span className="nav-label">Tarixçə və jurnal</span>
        </NavLink>
        <NavLink to="/stats" className={navClass}>
          <ChartIcon />
          <span className="nav-label">Statistika</span>
        </NavLink>
      </div>
    </nav>
  );
}

function AddCategory({ nextColor }: { nextColor: string }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [color, setColor] = useState(nextColor);
  const { create } = useCategoryMutations();

  if (!open) {
    return (
      <button
        type="button"
        className="nav-link nav-button"
        onClick={() => {
          setColor(nextColor);
          setOpen(true);
        }}
      >
        <PlusIcon />
        <span className="nav-label">Kateqoriya əlavə et</span>
      </button>
    );
  }

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    create.mutate(
      { name: name.trim(), color },
      {
        onSuccess: () => {
          setName("");
          setOpen(false);
        },
      },
    );
  };

  return (
    <form className="add-category" onSubmit={submit}>
      <label className="sr-only" htmlFor="new-category-name">
        Kateqoriya adı
      </label>
      <input
        id="new-category-name"
        className="input"
        autoFocus
        placeholder="Kateqoriya adı"
        value={name}
        onChange={(e) => setName(e.target.value)}
        onKeyDown={(e) => e.key === "Escape" && setOpen(false)}
      />
      <div className="color-row" role="radiogroup" aria-label="Rəng">
        {PALETTE.map((c) => (
          <button
            key={c}
            type="button"
            role="radio"
            aria-checked={c === color}
            aria-label={c}
            className={c === color ? "color-dot selected" : "color-dot"}
            style={{ background: c }}
            onClick={() => setColor(c)}
          />
        ))}
      </div>
      {create.error ? <p className="form-error">{create.error.message}</p> : null}
      <div className="row gap-8">
        <button type="submit" className="btn btn-dark btn-sm" disabled={create.isPending}>
          Əlavə et
        </button>
        <button type="button" className="btn btn-ghost btn-sm" onClick={() => setOpen(false)}>
          Ləğv et
        </button>
      </div>
    </form>
  );
}
