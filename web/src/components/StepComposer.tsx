import { useState, type FormEvent } from "react";
import { api, messageOf } from "../api/client";
import type { CaseStatus } from "../api/types";
import PhotoPicker from "./PhotoPicker";

interface Props {
  caseId: string;
  step: CaseStatus;
  stepLabel: string;
  onPosted: () => void | Promise<void>;
}

/** "Add a photo or comment" under one step of the route. Closed until asked for. */
export default function StepComposer({ caseId, step, stepLabel, onPosted }: Props) {
  const [open, setOpen] = useState(false);
  const [body, setBody] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!open) {
    return (
      <button type="button" className="linklike add-update" onClick={() => setOpen(true)}>
        Add a photo or comment
      </button>
    );
  }

  const close = () => {
    setOpen(false);
    setBody("");
    setFiles([]);
    setError(null);
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!body.trim() && files.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append("step", step);
      if (body.trim()) form.append("body", body.trim());
      files.forEach((f) => form.append("photos", f, f.name));
      await api.post(`/api/cases/${caseId}/comments`, form);
      close();
      await onPosted();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  const fieldId = `comment-${step}`;

  return (
    <form className="composer stack" onSubmit={submit} aria-label={`Add to: ${stepLabel}`}>
      <label htmlFor={fieldId}>Comment on &ldquo;{stepLabel}&rdquo;</label>
      <textarea
        id={fieldId}
        rows={3}
        maxLength={1000}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        placeholder="For example: damage to the left door, or the new part fitted"
      />
      <PhotoPicker files={files} onChange={setFiles} disabled={busy} max={step === "Reported" ? 2 : undefined} />
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="row">
        <button type="submit" className="btn primary" disabled={busy || (!body.trim() && files.length === 0)}>
          {busy ? "Posting…" : "Post"}
        </button>
        <button type="button" className="btn quiet" onClick={close} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  );
}
