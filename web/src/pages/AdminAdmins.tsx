import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, messageOf } from "../api/client";
import type { AdminAccount } from "../api/types";
import { useAuth } from "../auth";
import AdminGate from "../components/AdminGate";

/** Super admins add and remove admins here. Super admins themselves are set in the API settings. */
export default function AdminAdmins() {
  const { me } = useAuth();
  const [rows, setRows] = useState<AdminAccount[] | null>(null);
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRows(await api.get<AdminAccount[]>("/api/admin/admins"));
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, []);

  useEffect(() => {
    if (me?.role === "SuperAdmin") void load();
  }, [me?.role, load]);

  const add = async (e: FormEvent) => {
    e.preventDefault();
    if (!email.trim()) return;
    setBusy(true);
    try {
      await api.post("/api/admin/admins", { email: email.trim() });
      setEmail("");
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (row: AdminAccount) => {
    if (!window.confirm(`Remove admin access for ${row.email ?? "this person"}?`)) return;
    try {
      await api.delete(`/api/admin/admins/${row.id}`);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  };

  if (me?.role !== "SuperAdmin") {
    return (
      <AdminGate title="Admins">
        <p>Only a super admin can manage admins.</p>
      </AdminGate>
    );
  }

  return (
    <AdminGate title="Admins">
      <form onSubmit={add} className="stack">
        <label htmlFor="admin-email">Add an admin by email</label>
        <input
          id="admin-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="name@example.com"
          autoCapitalize="none"
        />
        <button className="btn primary" type="submit" disabled={busy || !email.trim()}>
          {busy ? "Adding…" : "Make admin"}
        </button>
        <p className="note">They sign in with a code sent to this email. No account is needed first.</p>
      </form>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {rows === null ? (
        <p>Loading…</p>
      ) : (
        <ul className="list">
          {rows.map((r) => (
            <li key={r.id}>
              <strong>{r.email ?? r.loginPhone ?? "No email"}</strong> {r.role === "SuperAdmin" ? "· Super admin" : "· Admin"}
              <div className="note">{r.source}</div>
              {r.removable && (
                <button className="btn quiet" onClick={() => void remove(r)}>
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </AdminGate>
  );
}
