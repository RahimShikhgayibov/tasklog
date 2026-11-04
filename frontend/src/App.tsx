import { Navigate, Route, Routes } from "react-router";
import { Sidebar } from "./components/Sidebar";
import { LogPage } from "./pages/LogPage";
import { StatsPage } from "./pages/StatsPage";
import { TasksPage } from "./pages/TasksPage";

export function App() {
  return (
    <div className="app">
      <Sidebar />
      <main className="content">
        <Routes>
          <Route path="/" element={<TasksPage view="today" />} />
          <Route path="/upcoming" element={<TasksPage view="upcoming" />} />
          <Route path="/all" element={<TasksPage view="all" />} />
          <Route path="/completed" element={<TasksPage view="completed" />} />
          <Route path="/category/:id" element={<TasksPage view="category" />} />
          <Route path="/log" element={<LogPage />} />
          <Route path="/stats" element={<StatsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
