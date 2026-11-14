import { useEffect, useState } from "react";
import { Navigate, Route, Routes } from "react-router";
import { useAuth } from "./api/hooks";
import { Sidebar } from "./components/Sidebar";
import { LoginPage, StartingScreen } from "./pages/LoginPage";
import { LogPage } from "./pages/LogPage";
import { StatsPage } from "./pages/StatsPage";
import { TasksPage } from "./pages/TasksPage";

export function App() {
  const auth = useAuth();
  const slow = useAfter(3000, auth.isPending);

  if (auth.isPending || (auth.isError && auth.data === undefined)) {
    return <StartingScreen slow={slow} error={auth.isError} onRetry={() => auth.refetch()} />;
  }
  if (!auth.data) return <LoginPage />;

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

/** true once `active` has stayed true for `ms` milliseconds. */
function useAfter(ms: number, active: boolean): boolean {
  const [done, setDone] = useState(false);
  useEffect(() => {
    if (!active) {
      setDone(false);
      return;
    }
    const id = setTimeout(() => setDone(true), ms);
    return () => clearTimeout(id);
  }, [ms, active]);
  return done;
}
