import { useState, type FormEvent } from "react";
import { api, messageOf } from "../api/client";
import type { Me, OtpStarted } from "../api/types";
import { Link } from "react-router-dom";
import { useAuth } from "../auth";
import OtpEntry from "../components/OtpEntry";

type Method = "email" | "phone";

/** Name, and the ways to sign in. Adding the missing one (email or mobile) keeps a person on one account. */
export default function Account() {
  const { me, setName, setMe } = useAuth();
  const [name, setNameInput] = useState(me?.displayName ?? "");
  const [savingName, setSavingName] = useState(false);
  const [nameMessage, setNameMessage] = useState<string | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);

  const [adding, setAdding] = useState<Method | null>(null);
  const [identifier, setIdentifier] = useState("");
  const [started, setStarted] = useState<OtpStarted | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!me) return null;

  const saveName = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim() || name.trim() === me.displayName) return;
    setSavingName(true);
    setNameError(null);
    setNameMessage(null);
    try {
      await setName(name.trim());
      setNameMessage("Saved.");
    } catch (err) {
      setNameError(messageOf(err));
    } finally {
      setSavingName(false);
    }
  };

  const startAdd = (method: Method) => {
    setAdding(method);
    setIdentifier("");
    setStarted(null);
    setError(null);
  };

  const cancelAdd = () => {
    setAdding(null);
    setStarted(null);
    setError(null);
  };

  const sendCode = async (e: FormEvent) => {
    e.preventDefault();
    if (!identifier.trim()) return;
    setBusy(true);
    setError(null);
    try {
      setStarted(await api.post<OtpStarted>("/api/auth/link/start", { identifier: identifier.trim() }));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  const verify = async (challengeId: string, code: string) => {
    const updated = await api.post<Me>("/api/auth/link/verify", { challengeId, code });
    setMe(updated);
    cancelAdd();
  };

  const row = (method: Method, label: string, value: string | null) => (
    <li className="method">
      <div>
        <strong>{label}</strong>
        <div className={value ? "" : "muted"}>{value ?? "Not added"}</div>
      </div>
      {!value && adding !== method && (
        <button type="button" className="btn" onClick={() => startAdd(method)}>
          Add
        </button>
      )}
    </li>
  );

  return (
    <main className="account">
      <h1>Your account</h1>
      <p className="muted">
        Signed in as {me.email ?? me.loginPhone ?? "this device"}. Account type: {me.role === "Admin" ? "Admin" : me.role === "Provider" ? "Provider" : "Customer"}.
      </p>

      <form onSubmit={saveName} className="stack">
        <label htmlFor="acct-name">Your name</label>
        <input id="acct-name" value={name} maxLength={80} autoComplete="name" onChange={(e) => setNameInput(e.target.value)} />
        {nameError && (
          <p className="error" role="alert">
            {nameError}
          </p>
        )}
        {nameMessage && <p role="status">{nameMessage}</p>}
        <div>
          <button type="submit" className="btn primary" disabled={savingName || !name.trim() || name.trim() === me.displayName}>
            {savingName ? "Saving…" : "Save name"}
          </button>
        </div>
      </form>

      {me.role === "Customer" && (
      <section className="stack">
        <h2>Your vehicles</h2>
        <p className="muted">
          {me.vehicleCount > 0
            ? `You have ${me.vehicleCount} saved ${me.vehicleCount === 1 ? "vehicle" : "vehicles"}.`
            : "Add your vehicle so providers know what they are coming to."}
        </p>
        <p>
          <Link to="/vehicles">{me.vehicleCount > 0 ? "Manage your vehicles" : "Add a vehicle"}</Link>
        </p>
      </section>
      )}

      <section className="stack">
        <h2>How you sign in</h2>
        <p className="muted">
          You can sign in with either one. Add the other so you always reach the same account, whichever you use.
        </p>
        <ul className="rows">
          {row("email", "Email", me.email)}
          {row("phone", "Mobile number", me.loginPhone)}
        </ul>

        {adding && !started && (
          <form onSubmit={sendCode} className="stack">
            <label htmlFor="add-id">{adding === "email" ? "Email address to add" : "Mobile number to add"}</label>
            <input
              id="add-id"
              type={adding === "email" ? "email" : "tel"}
              autoComplete={adding === "email" ? "email" : "tel"}
              autoFocus
              value={identifier}
              onChange={(e) => setIdentifier(e.target.value)}
              placeholder={adding === "email" ? "you@example.com" : "98765 43210"}
            />
            {error && (
              <p className="error" role="alert">
                {error}
              </p>
            )}
            <div className="row wrap">
              <button type="submit" className="btn primary" disabled={busy || !identifier.trim()}>
                {busy ? "Sending…" : "Send me a code"}
              </button>
              <button type="button" className="btn quiet" onClick={cancelAdd} disabled={busy}>
                Cancel
              </button>
            </div>
          </form>
        )}

        {adding && started && (
          <OtpEntry
            started={started}
            onVerify={verify}
            onResend={() => api.post<OtpStarted>("/api/auth/link/start", { identifier: identifier.trim() })}
            onBack={cancelAdd}
            backLabel="Cancel"
          />
        )}
      </section>
    </main>
  );
}
