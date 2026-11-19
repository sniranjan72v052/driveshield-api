import { useCallback, useEffect, useState } from "react";
import { api, messageOf } from "../api/client";
<<<<<<< HEAD
import { isStaff } from "../api/types";
=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
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
<<<<<<< HEAD
    if (isStaff(me?.role)) void load();
=======
    if (me?.role === "Admin") void load();
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
  }, [me?.role, load]);

  const decide = async (p: PendingProvider, action: "approve" | "reject") => {
    try {
      await api.post(`/api/admin/providers/${p.id}/${action}`);
      await load();
    } catch (e) {
      setError(messageOf(e));
    }
  };

<<<<<<< HEAD
  if (!isStaff(me?.role)) {
=======
  if (me?.role !== "Admin") {
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
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
<<<<<<< HEAD
                  {p.licenseNumber ? `Licence or GST ${p.licenseNumber}. ` : "No licence or GST number given. "}
                  Registered {formatTime(p.createdAt)}.
                </div>
                {(p.address || p.city || p.state || p.pinCode) && (
                  <div className="muted">
                    {[p.address, p.city, p.state, p.pinCode].filter(Boolean).join(", ")}
                    {p.zone ? ` (zone ${p.zone})` : ""}
                  </div>
                )}
                {(p.managerName || p.managerEmail) && (
                  <div className="muted">Manager: {[p.managerName, p.managerEmail].filter(Boolean).join(", ")}</div>
                )}
                {(p.panNumber || p.bankAccountNumber || p.ifscCode) && (
                  <div className="muted">
                    {p.panNumber ? `PAN ${p.panNumber}. ` : ""}
                    {p.bankAccountNumber ? `Account ${p.bankAccountNumber}. ` : ""}
                    {p.ifscCode ? `IFSC ${p.ifscCode}.` : ""}
                  </div>
                )}
                {(p.tdsPercentage != null || p.paintDiscount != null || p.paintDiscountOem != null || p.imdCode) && (
                  <div className="muted">
                    {p.tdsPercentage != null ? `TDS ${p.tdsPercentage}%. ` : ""}
                    {p.paintDiscount != null ? `Paint discount ${p.paintDiscount}%. ` : ""}
                    {p.paintDiscountOem != null ? `Paint discount OEM ${p.paintDiscountOem}%. ` : ""}
                    {p.imdCode ? `IMD ${p.imdCode}.` : ""}
                  </div>
                )}
=======
                  {p.licenseNumber ? `Licence ${p.licenseNumber}. ` : "No licence number given. "}
                  Registered {formatTime(p.createdAt)}.
                </div>
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
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
