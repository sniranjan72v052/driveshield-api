import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { CaseDetail, CaseStatus, ProviderOnCase } from "../api/types";
import { ACTION_LABEL, CATEGORY_LABEL, INCIDENT_LABEL, REVIEW_TAGS, STATUS_LABEL } from "../api/labels";
import { useAuth } from "../auth";
import { useCaseLive } from "../hooks/useLive";
import MapView from "../components/MapView";
import ProgressRoute from "../components/ProgressRoute";
import VehicleSummary from "../components/VehicleSummary";

export default function CasePage() {
  const { id } = useParams();
  const location = useLocation();
  // A message passed from the page that sent us here, for example "your photos didn't upload".
  const notice = (location.state as { notice?: string } | null)?.notice;
  const { me } = useAuth();
  const [detail, setDetail] = useState<CaseDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setDetail(await api.get<CaseDetail>(`/api/cases/${id}`));
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [id]);

  const live = useCaseLive(id, load);

  useEffect(() => {
    void load();
  }, [load]);

  // If the live connection is down, fall back to refreshing every 15 seconds.
  useEffect(() => {
    if (live === "live") return;
    const t = setInterval(() => void load(), 15000);
    return () => clearInterval(t);
  }, [live, load]);

  if (!detail) {
    return (
      <main>
        {error ? (
          <p className="error" role="alert">
            {error}
          </p>
        ) : (
          <p className="muted">Loading your case…</p>
        )}
      </main>
    );
  }

  const isProvider = !!me?.providerId && detail.providers.some((p) => p.providerId === me.providerId);
  const actions = detail.allowedNextStatuses.filter((s) => ACTION_LABEL[s]);
  const accepted = detail.providers.filter((p) => p.offerStatus === "Accepted");
  const toReview = isProvider ? [] : accepted.filter((p) => !p.reviewed);
  const canReview = detail.status === "Resolved" || detail.status === "Reviewed";

  const move = async (status: CaseStatus) => {
    if (status === "Cancelled" && !window.confirm("Cancel this request?")) return;
    setBusy(true);
    setError(null);
    try {
      await api.post(`/api/cases/${detail.id}/status`, { status, note: note.trim() || null });
      setNote("");
      await load();
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="case">
      <header className="case-head">
        <Link to={isProvider ? "/provider" : "/cases"} className="back">
          Back
        </Link>
        <h1>{INCIDENT_LABEL[detail.incidentType]}</h1>
        <p className="muted live-state">
          {live === "live" ? "Updates are live" : live === "connecting" ? "Connecting for live updates…" : "Live updates are off. Refreshing every 15 seconds."}
        </p>
      </header>

      {detail.status === "Cancelled" && <p className="banner">This request was cancelled.</p>}
      {detail.status === "Escalated" && (
        <p className="banner warn">
          No provider has taken this yet. Our team has been alerted and will arrange help.
        </p>
      )}

      {notice && <p className="banner warn">{notice}</p>}

      <div className="case-grid">
        <section className="stack-lg">
          <ProgressRoute
            caseId={detail.id}
            status={detail.status}
            timeline={detail.timeline}
            comments={detail.comments}
            canPost={detail.canComment}
            isAdmin={me?.role === "Admin"}
            onPosted={load}
          />

          {detail.description && <p className="said">&ldquo;{detail.description}&rdquo;</p>}

          {actions.length > 0 && (
            <div className="stack">
              <label htmlFor="note">Add a note (optional)</label>
              <input id="note" value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
              <div className="row wrap">
                {actions.map((s) => (
                  <button
                    key={s}
                    className={`btn ${s === "Cancelled" || s === "Escalated" ? "quiet" : "primary"}`}
                    disabled={busy}
                    onClick={() => void move(s)}
                  >
                    {ACTION_LABEL[s]}
                  </button>
                ))}
              </div>
            </div>
          )}

          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}

          {!isProvider && accepted.length > 0 && (
            <section className="stack">
              <h2>Your help</h2>
              <ul className="rows">
                {accepted.map((p) => (
                  <li key={p.providerId} className="provider-row">
                    <div>
                      <strong>{p.businessName}</strong>
                      <div className="muted">{CATEGORY_LABEL[p.category]}</div>
                    </div>
                    <div className="provider-side">
                      {p.reviewCount > 0 && (
                        <span className="muted">
                          {p.averageRating.toFixed(1)} stars from {p.reviewCount}
                        </span>
                      )}
                      {p.phone && <a href={`tel:${p.phone}`}>Call {p.phone}</a>}
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {canReview && toReview.length > 0 && (
            <section className="stack-lg">
              <h2>How was the service?</h2>
              {toReview.map((p) => (
                <ReviewForm key={p.providerId} caseId={detail.id} provider={p} onDone={load} />
              ))}
            </section>
          )}
          {detail.status === "Reviewed" && !isProvider && <p>Thank you for your review.</p>}
        </section>

        <aside className="stack">
          <h2>{detail.locationIsApproximate ? "Approximate location" : "Location"}</h2>
          <MapView value={{ lat: detail.latitude, lng: detail.longitude }} approximate={detail.locationIsApproximate} height={300} />
          {detail.locationIsApproximate && (
            <p className="muted">The exact spot is shared with you once you accept this request.</p>
          )}
          {detail.vehicle && (
            <>
              <h2>Vehicle</h2>
              <VehicleSummary vehicle={detail.vehicle} />
            </>
          )}
          <p className="muted">Status: {STATUS_LABEL[detail.status]}</p>
        </aside>
      </div>
    </main>
  );
}

function ReviewForm({ caseId, provider, onDone }: { caseId: string; provider: ProviderOnCase; onDone: () => Promise<void> }) {
  const [rating, setRating] = useState(0);
  const [tags, setTags] = useState<string[]>([]);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = (t: string) => setTags((cur) => (cur.includes(t) ? cur.filter((x) => x !== t) : [...cur, t]));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (rating < 1) return;
    setBusy(true);
    setError(null);
    try {
      await api.post(`/api/cases/${caseId}/providers/${provider.providerId}/review`, {
        rating,
        tags,
        comment: comment.trim() || null,
      });
      await onDone();
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="stack review">
      <h3>{provider.businessName}</h3>
      <div className="stars" role="radiogroup" aria-label={`Rating for ${provider.businessName}`}>
        {[1, 2, 3, 4, 5].map((n) => (
          <button
            type="button"
            key={n}
            role="radio"
            aria-checked={rating === n}
            aria-label={`${n} ${n === 1 ? "star" : "stars"}`}
            className={`star${n <= rating ? " on" : ""}`}
            onClick={() => setRating(n)}
          >
            ★
          </button>
        ))}
      </div>
      <div className="chips">
        {REVIEW_TAGS.map((t) => (
          <label key={t} className={`chip${tags.includes(t) ? " on" : ""}`}>
            <input type="checkbox" checked={tags.includes(t)} onChange={() => toggle(t)} />
            {t}
          </label>
        ))}
      </div>
      <label htmlFor={`c-${provider.providerId}`}>Comment (optional)</label>
      <textarea id={`c-${provider.providerId}`} rows={3} maxLength={2000} value={comment} onChange={(e) => setComment(e.target.value)} />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" className="btn primary" disabled={rating < 1 || busy}>
        Submit review
      </button>
    </form>
  );
}
