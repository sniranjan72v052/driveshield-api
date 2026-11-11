import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, messageOf } from "../api/client";
import type { AdminPhoto, AdminPhotoPage } from "../api/types";
import { INCIDENT_LABEL, STATUS_LABEL, formatTime } from "../api/labels";
import { useAuth } from "../auth";
import AdminNav from "../components/AdminNav";
import PhotoThumb from "../components/PhotoThumb";

const PAGE = 24;

/**
 * Every photo still on a case, newest first. Open a photo to see it larger and remove it if it should not be there.
 * An admin has to give a reason, which is saved with the removal. The owner then sees a short "removed by support" note.
 */
export default function AdminPhotos() {
  const { me } = useAuth();
  const [items, setItems] = useState<AdminPhoto[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (before: string | null) => {
    const query = `?take=${PAGE}${before ? `&before=${encodeURIComponent(before)}` : ""}`;
    return api.get<AdminPhotoPage>(`/api/admin/photos${query}`);
  }, []);

  const first = useCallback(async () => {
    try {
      const page = await load(null);
      setItems(page.items);
      setNext(page.nextBefore);
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [load]);

  useEffect(() => {
    if (me?.role === "Admin") void first();
  }, [me?.role, first]);

  const more = async () => {
    if (!next) return;
    setLoadingMore(true);
    try {
      const page = await load(next);
      setItems((cur) => [...(cur ?? []), ...page.items]);
      setNext(page.nextBefore);
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setLoadingMore(false);
    }
  };

  if (me?.role !== "Admin") {
    return (
      <main>
        <h1>Admin</h1>
        <p>This area is for administrators.</p>
      </main>
    );
  }

  return (
    <main>
      <h1>Photo review</h1>
      <AdminNav />
      <p className="muted">
        The newest photos on every case. Open one to remove it if it should not be there. You will be asked for a reason.
      </p>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {items && items.length === 0 && <p>There are no photos to review.</p>}

      {items && items.length > 0 && (
        <ul className="review-grid">
          {items.map((p) => (
            <li key={p.photoId} className="review-card">
              <PhotoThumb
                caseId={p.caseId}
                photo={{ id: p.photoId, contentType: "", removed: null, removalReason: null, canRemove: true }}
                mine={false}
                isAdmin
                // After a removal the photo is simply taken out of the queue.
                onChanged={() => setItems((cur) => (cur ?? []).filter((x) => x.photoId !== p.photoId))}
              />
              <div className="review-meta">
                <strong>{INCIDENT_LABEL[p.incidentType]}</strong>
                <span className="muted">
                  {STATUS_LABEL[p.step]}, added by {p.actor === "Admin" ? "support" : p.actor.toLowerCase()}
                </span>
                <span className="muted">{formatTime(p.at)}</span>
                <Link to={`/case/${p.caseId}`}>Open case</Link>
              </div>
            </li>
          ))}
        </ul>
      )}

      {next && (
        <p>
          <button className="btn" onClick={() => void more()} disabled={loadingMore}>
            {loadingMore ? "Loading…" : "Show older photos"}
          </button>
        </p>
      )}
    </main>
  );
}
