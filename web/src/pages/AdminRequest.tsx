import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { AdminCandidate, AdminCaseDetail, AdminSlot } from "../api/types";
import { CATEGORY_LABEL, INCIDENT_LABEL, STATUS_LABEL, formatKm, formatTime } from "../api/labels";
import AdminGate from "../components/AdminGate";
import VehicleSummary from "../components/VehicleSummary";

const RANGES = [5, 10, 15, 20, 30, 50];

/** One request: who it is for, what it needs, who holds each service, and the tools to assign, unassign or swap. */
export default function AdminRequest() {
  const { id } = useParams();
  const [detail, setDetail] = useState<AdminCaseDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setDetail(await api.get<AdminCaseDetail>(`/api/admin/cases/${id}`));
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [id]);

  useEffect(() => {
    void load();
    const t = setInterval(() => void load(), 15000);
    return () => clearInterval(t);
  }, [load]);

  const changed = async (text: string) => {
    setMessage(text);
    await load();
  };

  return (
    <AdminGate title="Request">
      <p>
        <Link to="/admin/requests">&larr; All requests</Link>
      </p>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {!detail && !error && <p className="muted">Loading…</p>}

      {detail && (
        <div className="stack-lg">
          <section className="stack">
            <div className="row wrap">
              <h2>{INCIDENT_LABEL[detail.incidentType]}</h2>
              <span className={`status status-${detail.status}`}>{STATUS_LABEL[detail.status]}</span>
            </div>
            {detail.description && <p className="said">&ldquo;{detail.description}&rdquo;</p>}
            <p className="muted">
              Reported {formatTime(detail.createdAt)}
              {detail.resolvedAt ? `, resolved ${formatTime(detail.resolvedAt)}` : ""}
              {detail.address ? `. ${detail.address}` : ""}
            </p>
            <p>
              Customer:{" "}
              <Link to={`/admin/requests?customerId=${detail.customerId}&status=all`}>
                {detail.customerName || detail.customerEmail || detail.customerPhone || "Customer"}
              </Link>
              <span className="muted">
                {" "}
                {[detail.customerPhone, detail.customerEmail].filter(Boolean).join(" · ")}
              </span>
            </p>
            {detail.vehicle ? <VehicleSummary vehicle={detail.vehicle} /> : <p className="muted">No vehicle was chosen.</p>}
            <p>
              <Link to={`/case/${detail.id}`}>Open the full case, with photos and comments</Link>
            </p>
          </section>

          {detail.status === "Escalated" && (
            <p className="banner warn">Nobody has taken this request. Assign a provider below.</p>
          )}
          {!detail.canAssign && !detail.canUnassign && (
            <p className="muted">This request is {STATUS_LABEL[detail.status].toLowerCase()}, so providers can no longer be changed.</p>
          )}
          {message && (
            <p role="status" className="banner">
              {message}
            </p>
          )}

          <section className="stack-lg">
            <h2>Services needed</h2>
            {detail.slots.map((slot) => (
              <SlotCard key={slot.category} detail={detail} slot={slot} onChanged={changed} />
            ))}
          </section>
        </div>
      )}
    </AdminGate>
  );
}

function SlotCard({
  detail,
  slot,
  onChanged,
}: {
  detail: AdminCaseDetail;
  slot: AdminSlot;
  onChanged: (message: string) => Promise<void>;
}) {
  const [picking, setPicking] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const holder = slot.assigned;

  const unassign = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.post(`/api/admin/cases/${detail.id}/unassign`, { category: slot.category, reason: reason.trim() || null });
      setRemoving(false);
      setReason("");
      await onChanged(`${holder?.businessName ?? "The provider"} was taken off. You can now choose someone else.`);
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="slot stack">
      <div className="row wrap slot-head">
        <h3>{CATEGORY_LABEL[slot.category]}</h3>
        {holder ? <span className="tag tag-on">Assigned</span> : <span className="tag tag-off">Nobody yet</span>}
      </div>

      {holder ? (
        <p>
          <strong>{holder.businessName}</strong>
          {holder.phone && <span className="muted"> · {holder.phone}</span>}
        </p>
      ) : (
        <p className="muted">
          {slot.openOffers > 0
            ? `${slot.openOffers} nearby ${slot.openOffers === 1 ? "provider has" : "providers have"} been asked and not answered yet. You can assign someone now instead.`
            : "No provider is on this service."}
        </p>
      )}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {!picking && !removing && (
        <div className="row wrap">
          {detail.canAssign && (
            <button type="button" className="btn primary" onClick={() => setPicking(true)}>
              {holder ? "Reassign to someone else" : "Assign a provider"}
            </button>
          )}
          {holder && detail.canUnassign && (
            <button type="button" className="btn" onClick={() => setRemoving(true)}>
              Unassign
            </button>
          )}
        </div>
      )}

      {removing && holder && (
        <div className="stack slot-confirm">
          <label htmlFor={`reason-${slot.category}`}>Why is {holder.businessName} being taken off? (optional, the customer sees it)</label>
          <input id={`reason-${slot.category}`} value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} />
          <div className="row wrap">
            <button type="button" className="btn emergency-lite" disabled={busy} onClick={() => void unassign()}>
              {busy ? "Working…" : "Take them off"}
            </button>
            <button type="button" className="btn quiet" disabled={busy} onClick={() => setRemoving(false)}>
              Keep them
            </button>
          </div>
        </div>
      )}

      {picking && (
        <CandidatePicker
          detail={detail}
          slot={slot}
          onCancel={() => setPicking(false)}
          onAssigned={async (name) => {
            setPicking(false);
            await onChanged(holder ? `Reassigned to ${name}.` : `${name} was assigned.`);
          }}
        />
      )}
    </div>
  );
}

/** Providers near the request, nearest first. The range buttons (15 km, 20 km and others) narrow the list. */
function CandidatePicker({
  detail,
  slot,
  onCancel,
  onAssigned,
}: {
  detail: AdminCaseDetail;
  slot: AdminSlot;
  onCancel: () => void;
  onAssigned: (businessName: string) => Promise<void>;
}) {
  const [range, setRange] = useState(15);
  const [availableOnly, setAvailableOnly] = useState(false);
  const [list, setList] = useState<AdminCandidate[] | null>(null);
  const [note, setNote] = useState("");
  const [working, setWorking] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let current = true;
    setList(null);
    const p = new URLSearchParams({ category: slot.category, radiusKm: String(range), availableOnly: String(availableOnly) });
    api
      .get<AdminCandidate[]>(`/api/admin/cases/${detail.id}/candidates?${p}`)
      .then((rows) => {
        if (!current) return;
        setList(rows);
        setError(null);
      })
      .catch((e) => current && setError(messageOf(e)));
    return () => {
      current = false;
    };
  }, [detail.id, slot.category, range, availableOnly]);

  const assign = async (c: AdminCandidate) => {
    setWorking(c.providerId);
    setError(null);
    try {
      await api.post(`/api/admin/cases/${detail.id}/assign`, {
        category: slot.category,
        providerId: c.providerId,
        replace: slot.assigned !== null,
        note: note.trim() || null,
      });
      await onAssigned(c.businessName);
    } catch (e) {
      setError(messageOf(e));
      setWorking(null);
    }
  };

  return (
    <div className="stack slot-confirm">
      <h4>Providers for {CATEGORY_LABEL[slot.category].toLowerCase()}, nearest first</h4>

      <div className="filters" role="group" aria-label="Distance from the request">
        {RANGES.map((km) => (
          <button
            key={km}
            type="button"
            className={`filter${range === km ? " on" : ""}`}
            aria-pressed={range === km}
            onClick={() => setRange(km)}
          >
            Within {km} km
          </button>
        ))}
      </div>
      <label className="switch">
        <input type="checkbox" checked={availableOnly} onChange={(e) => setAvailableOnly(e.target.checked)} />
        <span>Only providers who are available now</span>
      </label>

      <label htmlFor={`note-${slot.category}`}>Note for the customer (optional)</label>
      <input id={`note-${slot.category}`} value={note} maxLength={300} onChange={(e) => setNote(e.target.value)} />

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {list === null && !error && <p className="muted">Looking…</p>}
      {list && list.length === 0 && (
        <p>No approved provider offering this service is within {range} km. Try a wider range.</p>
      )}

      {list && list.length > 0 && (
        <ul className="rows">
          {list.map((c) => (
            <li key={c.providerId} className="offer">
              <div>
                <div className="row wrap">
                  <strong>{c.businessName}</strong>
                  <span className={`tag ${c.isAvailable ? "tag-on" : "tag-off"}`}>{c.isAvailable ? "Available" : "Offline"}</span>
                </div>
                <div>
                  {formatKm(c.distanceMeters)} away
                  {!c.withinOwnRadius && <span className="muted"> (further than they usually travel)</span>}
                </div>
                <div className="muted">
                  {c.activeJobs} active {c.activeJobs === 1 ? "job" : "jobs"}
                  {c.reviewCount > 0 ? ` · ${c.averageRating.toFixed(1)} stars from ${c.reviewCount}` : " · no reviews yet"}
                  {c.phone ? ` · ${c.phone}` : ""}
                </div>
              </div>
              <div className="row">
                {c.assigned ? (
                  <span className="muted">Already assigned</span>
                ) : (
                  <button type="button" className="btn primary" disabled={working !== null} onClick={() => void assign(c)}>
                    {working === c.providerId ? "Assigning…" : slot.assigned ? "Reassign here" : "Assign"}
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <div>
        <button type="button" className="btn quiet" onClick={onCancel} disabled={working !== null}>
          Close
        </button>
      </div>
    </div>
  );
}
