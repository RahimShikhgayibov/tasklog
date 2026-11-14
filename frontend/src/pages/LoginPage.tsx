import { useState, type FormEvent } from "react";
import { useLogin } from "../api/hooks";
import { CheckIcon } from "../components/Icons";

export function LoginPage() {
  const [password, setPassword] = useState("");
  const login = useLogin();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (password) login.mutate(password);
  };

  return (
    <main className="login-screen">
      <form className="card login-card" onSubmit={submit}>
        <div className="brand">
          <span className="brand-mark">
            <CheckIcon size={18} />
          </span>
          <span className="brand-name">Tasklog</span>
        </div>
        <h1 className="login-title">Daxil ol</h1>

        {/* Lets password managers save and fill the login. */}
        <input type="text" name="username" autoComplete="username" value="tasklog" readOnly hidden />

        <div className="stack-6">
          <label htmlFor="password" className="field-label">
            Şifrə
          </label>
          <input
            id="password"
            name="password"
            type="password"
            className="input"
            autoComplete="current-password"
            autoFocus
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>

        {login.error ? (
          <p className="form-error" role="alert">
            {login.error.message}
          </p>
        ) : null}

        <button type="submit" className="btn btn-primary" disabled={!password || login.isPending}>
          {login.isPending ? "Yoxlanılır…" : "Daxil ol"}
        </button>
      </form>
    </main>
  );
}

/** Shown while the first request is in flight — on the free plan the server may be waking up. */
export function StartingScreen({ slow, error, onRetry }: { slow: boolean; error: boolean; onRetry: () => void }) {
  return (
    <main className="login-screen">
      <div className="card login-card center-text">
        <div className="brand center-row">
          <span className="brand-mark">
            <CheckIcon size={18} />
          </span>
          <span className="brand-name">Tasklog</span>
        </div>
        {error ? (
          <>
            <p className="strong">Serverə qoşulmaq alınmadı.</p>
            <button type="button" className="btn btn-outline" onClick={onRetry}>
              Yenidən yoxla
            </button>
          </>
        ) : (
          <>
            <div className="spinner" aria-hidden="true" />
            <p className="muted" role="status">
              {slow ? "Server oyanır — bu bir dəqiqəyə qədər çəkə bilər…" : "Yüklənir…"}
            </p>
          </>
        )}
      </div>
    </main>
  );
}
