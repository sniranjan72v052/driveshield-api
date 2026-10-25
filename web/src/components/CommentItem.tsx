import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { api, messageOf } from "../api/client";
import type { CaseComment } from "../api/types";
import { formatTime } from "../api/labels";
import PhotoThumb from "./PhotoThumb";

interface Props {
  caseId: string;
  comment: CaseComment;
  isAdmin: boolean;
  /** Called after an edit or delete, so the page can reload the case. */
  onChanged: () => void | Promise<void>;
}

type Mode = "view" | "edit" | "delete";

/**
 * One post under a step. People can edit the words of their own post and delete it while the case is open.
 * Admins can remove anyone's post (they must say why). A deleted post stays in the thread as a short placeholder.
 */
export default function CommentItem({ caseId, comment, isAdmin, onChanged }: Props) {
  const [mode, setMode] = useState<Mode>("view");
  const [text, setText] = useState(comment.body ?? "");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const box = useRef<HTMLTextAreaElement>(null);
  const reasonBox = useRef<HTMLInputElement>(null);
  const textId = useId();
  const reasonId = useId();

  useEffect(() => {
    if (mode === "edit") box.current?.focus();
    if (mode === "delete") reasonBox.current?.focus();
  }, [mode]);

  // An admin removing someone else's post must say why. Authors deleting their own do not.
  const needsReason = isAdmin && !comment.mine;

  if (comment.removed) {
    return (
      <li className="comment removed">
        <div className="comment-meta">
          <strong>{comment.authorLabel}</strong>
          <time dateTime={comment.at}>{formatTime(comment.at)}</time>
        </div>
        <p className="comment-body muted">
          {comment.removed === "Support" ? "This post was removed by the support team." : "This post was deleted by its author."}
          {comment.removalReason && <span className="photo-removed-reason">Reason: {comment.removalReason}</span>}
        </p>
      </li>
    );
  }

  const photosLeft = comment.photos.filter((p) => !p.removed).length;
  const unchanged = text.trim() === (comment.body ?? "");
  const emptyAndNoPhotos = text.trim() === "" && photosLeft === 0;

  const close = () => {
    setMode("view");
    setText(comment.body ?? "");
    setReason("");
    setError(null);
  };

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (unchanged || emptyAndNoPhotos) return;
    setBusy(true);
    setError(null);
    try {
      await api.patch(`/api/cases/${caseId}/comments/${comment.id}`, { body: text.trim() });
      setMode("view");
      await onChanged();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (e: FormEvent) => {
    e.preventDefault();
    if (needsReason && !reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const query = needsReason ? `?reason=${encodeURIComponent(reason.trim())}` : "";
      await api.delete(`/api/cases/${caseId}/comments/${comment.id}${query}`);
      await onChanged(); // this item turns into a placeholder when the case reloads
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <li className={`comment by-${comment.actor.toLowerCase()}`}>
      <div className="comment-meta">
        <strong>{comment.authorLabel}</strong>
        <span>
          {comment.editedAt && <span className="edited">edited </span>}
          <time dateTime={comment.at}>{formatTime(comment.at)}</time>
        </span>
      </div>

      {mode === "edit" ? (
        <form className="stack" onSubmit={save}>
          <label htmlFor={textId} className="sr-only">
            Edit your comment
          </label>
          <textarea id={textId} ref={box} rows={3} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} />
          {emptyAndNoPhotos && <p className="muted">A post needs words or a photo. To remove it completely, delete it instead.</p>}
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="row wrap">
            <button type="submit" className="btn primary" disabled={busy || unchanged || emptyAndNoPhotos}>
              {busy ? "Saving…" : "Save"}
            </button>
            <button type="button" className="btn quiet" onClick={close} disabled={busy}>
              Cancel
            </button>
          </div>
        </form>
      ) : (
        <>
          {comment.body && <p className="comment-body">{comment.body}</p>}
          {comment.photos.length > 0 && (
            <div className="photos">
              {comment.photos.map((p) => (
                <PhotoThumb key={p.id} caseId={caseId} photo={p} mine={comment.mine} isAdmin={isAdmin} onChanged={onChanged} />
              ))}
            </div>
          )}
        </>
      )}

      {mode === "delete" && (
        <form className="stack comment-confirm" onSubmit={remove}>
          {needsReason ? (
            <>
              <label htmlFor={reasonId}>Why is this post being removed? It is saved with the removal.</label>
              <input id={reasonId} ref={reasonBox} value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} />
            </>
          ) : (
            <p>Delete this post{photosLeft > 0 ? " and its photos" : ""}? This can't be undone.</p>
          )}
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <div className="row wrap">
            <button type="submit" className="btn emergency-lite" disabled={busy || (needsReason && !reason.trim())}>
              {busy ? "Working…" : needsReason ? "Remove post" : "Delete post"}
            </button>
            <button type="button" className="btn quiet" onClick={close} disabled={busy}>
              Keep it
            </button>
          </div>
        </form>
      )}

      {mode === "view" && (comment.canEdit || comment.canDelete) && (
        <div className="comment-actions">
          {comment.canEdit && (
            <button type="button" className="linklike" onClick={() => setMode("edit")}>
              Edit
            </button>
          )}
          {comment.canDelete && (
            <button type="button" className="linklike" onClick={() => setMode("delete")}>
              {needsReason ? "Remove" : "Delete"}
            </button>
          )}
        </div>
      )}
    </li>
  );
}
