import { useCallback, useEffect, useState } from "react";
import { api, messageOf } from "../api/client";
import type { PendingProvider } from "../api/types";
import { CATEGORY_LABEL, formatTime } from "../api/labels";
import { useAuth } from "../auth";
import AdminNav from "../components/AdminNav";

export default function Admin() {
  const { me } = useAuth();
  const [pending, setPending] = useState<PendingProvider[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setPending(await api.get<PendingProvider[]>("/api/admin/providers/pending"));
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, []);

  useEffect(() => {
    if (me?.role === "Admin") void load();
  }, [me?.role, load]);

  const decide = async (p: PendingProvider, action: "approve" | "reject") => {
    try {
      await api.post(`/api/admin/providers/${p.id}/${action}`);
      await load();
    } catch (e) {
      setError(messageOf(e));
    }
  };

  if (me?.role !== "Admin") {
    return (
      <main>
        <h1>Admin</h1>
        <p>
          This area is for administrators. To make your account an admin, add the email you sign in with to{" "}
          <code>Auth:AdminEmails</code> in the API settings, restart the API, and sign in again.
        </p>
      </main>
    );
  }

  return (
    <main>
      <h1>Provider approvals</h1>
      <AdminNav />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {pending && pending.length === 0 && <p>No registrations are waiting for approval.</p>}
      {pending && pending.length > 0 && (
        <ul className="rows">
          {pending.map((p) => (
            <li key={p.id} className="offer">
              <div>
                <strong>{p.businessName}</strong>
                <div className="muted">
                  {p.categories.map((c) => CATEGORY_LABEL[c]).join(", ")}
                </div>
                <div className="muted">
                  {p.phone ? `Phone ${p.phone}. ` : ""}
                  {p.licenseNumber ? `Licence ${p.licenseNumber}. ` : "No licence number given. "}
                  Registered {formatTime(p.createdAt)}.
                </div>
              </div>
              <div className="row">
                <button className="btn primary" onClick={() => void decide(p, "approve")}>
                  Approve
                </button>
                <button className="btn quiet" onClick={() => void decide(p, "reject")}>
                  Reject
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
