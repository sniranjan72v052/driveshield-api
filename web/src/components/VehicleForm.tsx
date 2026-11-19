import { useState, type FormEvent } from "react";
import { api, messageOf } from "../api/client";
import type { Vehicle, VehicleInput } from "../api/types";

interface Props {
  /** When given, the form edits this vehicle. Otherwise it adds a new one. */
  vehicle?: Vehicle;
  submitLabel?: string;
  onSaved: (vehicle: Vehicle) => void | Promise<void>;
  onCancel?: () => void;
  cancelLabel?: string;
}

/** Make, model and registration number, plus optional electric and insurance details. */
export default function VehicleForm({ vehicle, submitLabel = "Save vehicle", onSaved, onCancel, cancelLabel = "Cancel" }: Props) {
  const [make, setMake] = useState(vehicle?.make ?? "");
  const [model, setModel] = useState(vehicle?.model ?? "");
  const [reg, setReg] = useState(vehicle?.registrationNumber ?? "");
  const [electric, setElectric] = useState(vehicle?.isElectric ?? false);
  const [insurer, setInsurer] = useState(vehicle?.insurerName ?? "");
  const [policy, setPolicy] = useState(vehicle?.policyNumber ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ready = make.trim() && model.trim() && reg.trim();

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready) return;
    setBusy(true);
    setError(null);
    const body: Partial<VehicleInput> = {
      make: make.trim(),
      model: model.trim(),
      registrationNumber: reg.trim(),
      isElectric: electric,
      insurerName: insurer.trim() || null,
      policyNumber: policy.trim() || null,
    };
    try {
      const saved = vehicle
        ? await api.put<Vehicle>(`/api/vehicles/${vehicle.id}`, body)
        : await api.post<Vehicle>("/api/vehicles", body);
      await onSaved(saved);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  const id = vehicle?.id ?? "new";

  return (
    <form onSubmit={submit} className="stack">
      <label htmlFor={`v-make-${id}`}>Make</label>
      <input id={`v-make-${id}`} value={make} maxLength={60} onChange={(e) => setMake(e.target.value)} placeholder="Maruti, Tata, Hyundai" required />

      <label htmlFor={`v-model-${id}`}>Model</label>
      <input id={`v-model-${id}`} value={model} maxLength={60} onChange={(e) => setModel(e.target.value)} placeholder="Swift, Nexon, Creta" required />

      <label htmlFor={`v-reg-${id}`}>Registration number</label>
      <input
        id={`v-reg-${id}`}
        value={reg}
        maxLength={15}
        autoCapitalize="characters"
        onChange={(e) => setReg(e.target.value)}
        placeholder="MH12 AB 1234"
        required
      />

      <label className="switch">
        <input type="checkbox" checked={electric} onChange={(e) => setElectric(e.target.checked)} />
        <span>This is an electric vehicle</span>
      </label>

      <details open={!!(insurer || policy)}>
        <summary>Insurance details (optional, helps with claims)</summary>
        <div className="stack">
          <label htmlFor={`v-ins-${id}`}>Insurance company</label>
          <input id={`v-ins-${id}`} value={insurer} maxLength={100} onChange={(e) => setInsurer(e.target.value)} />
          <label htmlFor={`v-pol-${id}`}>Policy number</label>
          <input id={`v-pol-${id}`} value={policy} maxLength={50} onChange={(e) => setPolicy(e.target.value)} />
          <p className="muted">Shared only with you, our support team, and the provider who takes your request.</p>
        </div>
      </details>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <div className="row wrap">
        <button type="submit" className="btn primary" disabled={busy || !ready}>
          {busy ? "Saving…" : submitLabel}
        </button>
        {onCancel && (
          <button type="button" className="btn quiet" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </button>
        )}
      </div>
    </form>
  );
}
