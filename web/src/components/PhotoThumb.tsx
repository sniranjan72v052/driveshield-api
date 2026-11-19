import { useEffect, useId, useRef, useState } from "react";
import { api, fetchBlob, messageOf } from "../api/client";
import type { CasePhoto } from "../api/types";

interface Props {
  caseId: string;
  photo: CasePhoto;
  /** The signed-in person wrote the post this photo belongs to. */
  mine: boolean;
  /** The signed-in person is an admin. */
  isAdmin: boolean;
  /** Called after the photo was deleted or removed, so the page can reload the case. */
  onChanged: () => void | Promise<void>;
}

/** What to say in place of a photo that is gone. The admin's reason is only ever sent to admins. */
export function RemovedPhoto({ photo }: { photo: CasePhoto }) {
  const who = photo.removed === "Support" ? "removed by the support team" : "deleted by its owner";
  return (
    <span className="photo-removed">
      Photo {who}
      {photo.removalReason && <span className="photo-removed-reason">Reason: {photo.removalReason}</span>}
    </span>
  );
}

/**
 * A small photo from a case. The file needs sign-in, so it is downloaded with the token and shown from memory.
 * Click it to see it larger. The owner (while the case is open) and admins also get a delete button there.
 */
export default function PhotoThumb({ caseId, photo, mine, isAdmin, onChanged }: Props) {
  const gone = photo.removed !== null;
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [asking, setAsking] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const reasonId = useId();

  useEffect(() => {
    if (gone) return;
    let cancelled = false;
    let objectUrl: string | null = null;
    setUrl(null);
    setFailed(false);
    fetchBlob(`/api/cases/${caseId}/photos/${photo.id}`)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [caseId, photo.id, gone]);

  if (gone) return <RemovedPhoto photo={photo} />;
  if (failed) return <span className="photo-missing">Photo unavailable</span>;
  if (!url) return <span className="photo-loading" role="status" aria-label="Loading photo" />;

  // An admin taking down someone else's photo must say why. Owners deleting their own do not.
  const needsReason = isAdmin && !mine;

  const reset = () => {
    setAsking(false);
    setReason("");
    setError(null);
  };

  const remove = async () => {
    if (needsReason && !reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const query = needsReason ? `?reason=${encodeURIComponent(reason.trim())}` : "";
      await api.delete(`/api/cases/${caseId}/photos/${photo.id}${query}`);
      dialog.current?.close();
      reset();
      await onChanged();
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <button type="button" className="photo-thumb" onClick={() => dialog.current?.showModal()} aria-label="View photo larger">
        <img src={url} alt="Photo added to this case" loading="lazy" />
      </button>
      <dialog
        ref={dialog}
        className="lightbox"
        aria-label="Photo"
        onClose={reset}
        onClick={(e) => {
          if (e.target === e.currentTarget) e.currentTarget.close(); // click outside the picture
        }}
      >
        <img src={url} alt="Photo added to this case, full size" />

        {photo.canRemove && asking && (
          <div className="stack lightbox-confirm">
            {needsReason ? (
              <>
                <label htmlFor={reasonId}>Why is this being removed? It is saved with the removal.</label>
                <input id={reasonId} value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} autoFocus />
              </>
            ) : (
              <p>Delete this photo? This can't be undone.</p>
            )}
            {error && (
              <p className="error" role="alert">
                {error}
              </p>
            )}
            <div className="row wrap">
              <button type="button" className="btn emergency-lite" onClick={() => void remove()} disabled={busy || (needsReason && !reason.trim())}>
                {busy ? "Removing…" : needsReason ? "Remove photo" : "Delete photo"}
              </button>
              <button type="button" className="btn quiet" onClick={reset} disabled={busy}>
                Keep it
              </button>
            </div>
          </div>
        )}

        {!asking && (
          <div className="row lightbox-actions">
            {photo.canRemove && (
              <button type="button" className="btn quiet" onClick={() => setAsking(true)}>
                {needsReason ? "Remove photo" : "Delete photo"}
              </button>
            )}
            <form method="dialog">
              <button className="btn">Close</button>
            </form>
          </div>
        )}
      </dialog>
    </>
  );
}
