import { useState, type FormEvent } from "react";
import { messageOf } from "../api/client";
import { useAuth } from "../auth";

/** Shown once after a first sign-in. Skippable, because someone asking for help after an accident should not be held up. */
export default function NamePrompt({ onSkip }: { onSkip: () => void }) {
  const { setName } = useAuth();
  const [name, setNameInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await setName(name.trim());
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <main className="signin">
      <h1>Welcome</h1>
      <form onSubmit={save} className="stack">
        <label htmlFor="your-name">What should we call you?</label>
        <input id="your-name" value={name} onChange={(e) => setNameInput(e.target.value)} maxLength={80} autoComplete="name" autoFocus />
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <div className="row wrap">
          <button type="submit" className="btn primary" disabled={busy || !name.trim()}>
            Continue
          </button>
          <button type="button" className="btn quiet" onClick={onSkip} disabled={busy}>
            Skip for now
          </button>
        </div>
      </form>
    </main>
  );
}
