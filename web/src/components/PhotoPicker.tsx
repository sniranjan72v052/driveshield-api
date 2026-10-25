import { useEffect, useState, type ChangeEvent } from "react";
import { ACCEPT_ATTR, MAX_PHOTOS, prepareImage } from "../api/images";
import { messageOf } from "../api/client";

interface Props {
  files: File[];
  onChange: (files: File[]) => void;
  disabled?: boolean;
  /** How many photos may be chosen. Defaults to five; the "report a problem" form allows two. */
  max?: number;
}

/** Choose photos (camera or gallery on a phone), see them, and remove any before posting. */
export default function PhotoPicker({ files, onChange, disabled, max = MAX_PHOTOS }: Props) {
  const [previews, setPreviews] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const urls = files.map((f) => URL.createObjectURL(f));
    setPreviews(urls);
    return () => urls.forEach((u) => URL.revokeObjectURL(u));
  }, [files]);

  const pick = async (e: ChangeEvent<HTMLInputElement>) => {
    const chosen = Array.from(e.target.files ?? []);
    e.target.value = ""; // lets the same photo be chosen again after removing it
    if (chosen.length === 0) return;

    const room = max - files.length;
    if (room <= 0) {
      setError(`You can add up to ${max} photo${max === 1 ? "" : "s"} at a time.`);
      return;
    }

    setBusy(true);
    setError(null);
    const added: File[] = [];
    let problem: string | null = chosen.length > room ? `You can add up to ${max} photo${max === 1 ? "" : "s"} at a time.` : null;
    for (const f of chosen.slice(0, room)) {
      try {
        added.push(await prepareImage(f));
      } catch (err) {
        problem = messageOf(err);
      }
    }
    onChange([...files, ...added]);
    setError(problem);
    setBusy(false);
  };

  const remove = (index: number) => onChange(files.filter((_, i) => i !== index));

  return (
    <div className="picker">
      <label className={`btn filepick${disabled || busy || files.length >= max ? " disabled" : ""}`}>
        <input
          type="file"
          accept={ACCEPT_ATTR}
          multiple
          onChange={(e) => void pick(e)}
          disabled={disabled || busy || files.length >= max}
        />
        {busy ? "Preparing photos…" : files.length > 0 ? "Add more photos" : "Add photos"}
      </label>

      {files.length > 0 && (
        <ul className="picker-list">
          {files.map((f, i) => (
            <li key={`${f.name}-${f.size}-${i}`}>
              {previews[i] && <img src={previews[i]} alt={`Selected photo ${i + 1}`} />}
              <button type="button" className="picker-remove" aria-label={`Remove photo ${i + 1}`} onClick={() => remove(i)} disabled={disabled}>
                ×
              </button>
            </li>
          ))}
        </ul>
      )}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
