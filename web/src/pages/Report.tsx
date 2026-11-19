import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, messageOf } from "../api/client";
import { useAuth } from "../auth";
import type { IncidentType, ServiceCategory, Vehicle } from "../api/types";
import { ALL_CATEGORIES, CATEGORY_LABEL, INCIDENTS } from "../api/labels";
import { IncidentIcon } from "../components/Icons";
import MapView, { getMyLocation, type LatLng } from "../components/MapView";
import PhotoPicker from "../components/PhotoPicker";

export default function Report() {
  const navigate = useNavigate();
  const { me } = useAuth();
  const isCustomer = me?.role === "Customer";
  const [type, setType] = useState<IncidentType | null>(null);
  const [location, setLocation] = useState<LatLng | null>(null);
  const [description, setDescription] = useState("");
  const [extra, setExtra] = useState<ServiceCategory[]>([]);
  const [files, setFiles] = useState<File[]>([]);
  const [vehicles, setVehicles] = useState<Vehicle[]>([]);
  const [vehicleId, setVehicleId] = useState("");
  const [locating, setLocating] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // The customer's saved vehicles. If they have just one it is chosen for them.
  useEffect(() => {
    if (!isCustomer) return;
    api
      .get<Vehicle[]>("/api/vehicles")
      .then((list) => {
        setVehicles(list);
        if (list.length > 0) setVehicleId(list[0].id);
      })
      .catch(() => {
        /* the vehicle is optional, so a failed lookup must not stop someone asking for help */
      });
  }, [isCustomer]);

  const useMyLocation = async () => {
    setLocating(true);
    setError(null);
    try {
      setLocation(await getMyLocation());
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setLocating(false);
    }
  };

  const toggle = (c: ServiceCategory) =>
    setExtra((cur) => (cur.includes(c) ? cur.filter((x) => x !== c) : [...cur, c]));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!type || !location) return;
    setBusy(true);
    setError(null);
    try {
      const created = await api.post<{ id: string }>("/api/cases", {
        incidentType: type,
        latitude: location.lat,
        longitude: location.lng,
        description: description.trim() || null,
        vehicleId: isCustomer && vehicleId ? vehicleId : null,
        categories: extra.length ? extra : null,
      });

      // Photos go up as the first comment, on the "Reported" step. The request itself is already sent,
      // so a failed upload must not lose it.
      let notice: string | undefined;
      if (files.length > 0) {
        try {
          const form = new FormData();
          form.append("step", "Reported");
          files.forEach((f) => form.append("photos", f, f.name));
          await api.post(`/api/cases/${created.id}/comments`, form);
        } catch {
          notice = "Your request was sent, but the photos didn't upload. Add them again under \"Reported\" below.";
        }
      }
      navigate(`/case/${created.id}`, { state: notice ? { notice } : undefined });
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <main>
      <h1>What happened?</h1>

      <form onSubmit={submit} className="stack-lg">
        <fieldset className="plates">
          <legend className="sr-only">Type of incident</legend>
          {INCIDENTS.map((i) => (
            <label key={i.type} className={`plate${type === i.type ? " selected" : ""}`}>
              <input type="radio" name="incident" value={i.type} checked={type === i.type} onChange={() => setType(i.type)} />
              <IncidentIcon type={i.type} />
              <span className="plate-title">{i.label}</span>
              <span className="plate-hint">{i.hint}</span>
            </label>
          ))}
        </fieldset>

        <section className="stack">
          <h2>Where are you?</h2>
          <div className="row">
            <button type="button" className="btn" onClick={useMyLocation} disabled={locating}>
              {locating ? "Finding you…" : "Use my location"}
            </button>
            <span className="muted">{location ? "Pin placed. Tap the map to adjust it." : "Or tap the map to place the pin."}</span>
          </div>
          <MapView value={location} onChange={setLocation} />
        </section>

        {isCustomer && (
        <section className="stack">
          <h2>Which vehicle?</h2>
          {vehicles.length === 0 ? (
            <p className="muted">
              Providers help faster when they know the vehicle. <Link to="/vehicles">Add your vehicle</Link> (you can do this later too).
            </p>
          ) : (
            <>
              <select id="vehicle" aria-label="Which vehicle" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)}>
                {vehicles.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.make} {v.model}, {v.registrationNumber}
                  </option>
                ))}
                <option value="">Not one of these</option>
              </select>
              <p className="muted">
                <Link to="/vehicles">Add or change vehicles</Link>
              </p>
            </>
          )}
        </section>
        )}

        <section className="stack">
          <label htmlFor="desc">What do we need to know? (optional)</label>
          <textarea
            id="desc"
            rows={3}
            maxLength={2000}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="For example: front tyre burst, two people in the car, near the petrol pump"
          />
        </section>

        <section className="stack">
          <h2>Photos (optional)</h2>
          <p className="muted">Up to 2 photos of the damage or the spot help the provider come prepared.</p>
          <PhotoPicker files={files} onChange={setFiles} disabled={busy} max={2} />
        </section>

        <details className="stack">
          <summary>Choose the help you need</summary>
          <p className="muted">Leave this blank and we pick the right help for the incident.</p>
          <div className="chips">
            {ALL_CATEGORIES.map((c) => (
              <label key={c} className={`chip${extra.includes(c) ? " on" : ""}`}>
                <input type="checkbox" checked={extra.includes(c)} onChange={() => toggle(c)} />
                {CATEGORY_LABEL[c]}
              </label>
            ))}
          </div>
        </details>

        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}

        <button type="submit" className="btn emergency" disabled={!type || !location || busy}>
          {busy ? "Sending…" : "Get help now"}
        </button>
        {(!type || !location) && <p className="muted">Choose what happened and place the pin to continue.</p>}
      </form>
    </main>
  );
}
