import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, ApiError, messageOf } from "../api/client";
import type { Job, Offer, ProviderProfile, ServiceCategory } from "../api/types";
import { ALL_CATEGORIES, CATEGORY_LABEL, INCIDENT_LABEL, STATUS_LABEL, formatKm, formatTime } from "../api/labels";
import { useAuth } from "../auth";
import { useProviderInbox } from "../hooks/useLive";
import MapView, { getMyLocation, type LatLng } from "../components/MapView";

export default function ProviderHome() {
  const { reloadMe } = useAuth();
  const [profile, setProfile] = useState<ProviderProfile | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadProfile = useCallback(async () => {
    try {
      setProfile(await api.get<ProviderProfile>("/api/providers/me"));
      setError(null);
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) setProfile(null);
      else setError(messageOf(e));
    } finally {
      setLoaded(true);
    }
  }, []);

  useEffect(() => {
    void loadProfile();
  }, [loadProfile]);

  if (!loaded) {
    return (
      <main>
        <p className="muted">Loading…</p>
      </main>
    );
  }

  if (error) {
    return (
      <main>
        <p className="error" role="alert">
          {error}
        </p>
      </main>
    );
  }

  if (!profile) {
    return (
      <RegisterForm
        onDone={async () => {
          await reloadMe();
          await loadProfile();
        }}
      />
    );
  }

  return <Dashboard profile={profile} reload={loadProfile} />;
}

// ---------------------------------------------------------------- registration

function RegisterForm({ onDone }: { onDone: () => Promise<void> }) {
  const [businessName, setBusinessName] = useState("");
  // If they signed in with a mobile number, offer it as the number customers can call (they can change it).
  const { me } = useAuth();
  const [phone, setPhone] = useState(me?.loginPhone ?? "");
  const [licence, setLicence] = useState("");
  const [description, setDescription] = useState("");
  const [categories, setCategories] = useState<ServiceCategory[]>([]);
  const [radiusKm, setRadiusKm] = useState(15);
  const [location, setLocation] = useState<LatLng | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = (c: ServiceCategory) =>
    setCategories((cur) => (cur.includes(c) ? cur.filter((x) => x !== c) : [...cur, c]));

  const locate = async () => {
    try {
      setLocation(await getMyLocation());
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!location || categories.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/providers/register", {
        businessName: businessName.trim(),
        description: description.trim() || null,
        licenseNumber: licence.trim() || null,
        phone: phone.trim() || null,
        categories,
        latitude: location.lat,
        longitude: location.lng,
        serviceRadiusMeters: radiusKm * 1000,
      });
      await onDone();
    } catch (err) {
      setError(messageOf(err));
      setBusy(false);
    }
  };

  return (
    <main>
      <h1>Offer your services</h1>
      <p className="lead">Register your business. After an admin approves it, you receive requests from drivers near you.</p>

      <form onSubmit={submit} className="stack-lg">
        <div className="stack">
          <label htmlFor="biz">Business name</label>
          <input id="biz" value={businessName} onChange={(e) => setBusinessName(e.target.value)} maxLength={200} required />
          <label htmlFor="phone">Phone number customers can call</label>
          <input id="phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
          <label htmlFor="lic">Licence or registration number (optional)</label>
          <input id="lic" value={licence} onChange={(e) => setLicence(e.target.value)} />
          <label htmlFor="about">About your service (optional)</label>
          <textarea id="about" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>

        <fieldset className="stack">
          <legend>Services you provide</legend>
          <div className="chips">
            {ALL_CATEGORIES.map((c) => (
              <label key={c} className={`chip${categories.includes(c) ? " on" : ""}`}>
                <input type="checkbox" checked={categories.includes(c)} onChange={() => toggle(c)} />
                {CATEGORY_LABEL[c]}
              </label>
            ))}
          </div>
        </fieldset>

        <div className="stack">
          <label htmlFor="radius">How far will you travel?</label>
          <select id="radius" value={radiusKm} onChange={(e) => setRadiusKm(Number(e.target.value))}>
            {[5, 10, 15, 25, 50].map((k) => (
              <option key={k} value={k}>
                {k} km
              </option>
            ))}
          </select>
        </div>

        <section className="stack">
          <h2>Where are you based?</h2>
          <div className="row">
            <button type="button" className="btn" onClick={locate}>
              Use my location
            </button>
            <span className="muted">Or tap the map to place the pin.</span>
          </div>
          <MapView value={location} onChange={setLocation} height={280} />
        </section>

        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="btn primary" disabled={busy || !location || categories.length === 0 || !businessName.trim()}>
          {busy ? "Registering…" : "Register"}
        </button>
        {(!location || categories.length === 0) && <p className="muted">Choose at least one service and place the pin to continue.</p>}
      </form>
    </main>
  );
}

// ---------------------------------------------------------------- dashboard

function Dashboard({ profile, reload }: { profile: ProviderProfile; reload: () => Promise<void> }) {
  const navigate = useNavigate();
  const approved = profile.approvalStatus === "Approved";
  const [offers, setOffers] = useState<Offer[]>([]);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newSpot, setNewSpot] = useState<LatLng | null>(null);

  const loadWork = useCallback(async () => {
    if (!approved) return;
    try {
      const [o, j] = await Promise.all([api.get<Offer[]>("/api/providers/me/offers"), api.get<Job[]>("/api/providers/me/jobs")]);
      setOffers(o);
      setJobs(j);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [approved]);

  useEffect(() => {
    void loadWork();
    const t = setInterval(() => void loadWork(), 15000);
    return () => clearInterval(t);
  }, [loadWork]);

  useProviderInbox(approved, () => void loadWork());

  const setAvailability = async (isAvailable: boolean) => {
    try {
      await api.put("/api/providers/me/availability", { isAvailable });
      await reload();
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const useMyLocation = async () => {
    try {
      setNewSpot(await getMyLocation());
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const saveLocation = async () => {
    if (!newSpot) return;
    try {
      await api.put("/api/providers/me/location", { latitude: newSpot.lat, longitude: newSpot.lng });
      setNewSpot(null);
      await reload();
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const respond = async (o: Offer, accept: boolean) => {
    try {
      await api.post(`/api/cases/${o.caseId}/offers/${o.offerId}/${accept ? "accept" : "decline"}`);
      if (accept) navigate(`/case/${o.caseId}`);
      else await loadWork();
    } catch (e) {
      setError(messageOf(e));
      await loadWork();
    }
  };

  const base: LatLng | null = profile.latitude !== null && profile.longitude !== null ? { lat: profile.latitude, lng: profile.longitude } : null;

  return (
    <main className="stack-lg">
      <header>
        <h1>{profile.businessName}</h1>
        {profile.reviewCount > 0 && (
          <p className="muted">
            {profile.averageRating.toFixed(1)} stars from {profile.reviewCount} {profile.reviewCount === 1 ? "review" : "reviews"}
          </p>
        )}
      </header>

      {profile.approvalStatus === "Pending" && (
        <p className="banner">
          Your registration is waiting for approval. You can't receive requests until an admin approves it. In local testing, sign in
          with the admin email (set in <code>Auth:AdminEmails</code>) in another window and open Admin.
        </p>
      )}
      {(profile.approvalStatus === "Rejected" || profile.approvalStatus === "Suspended") && (
        <p className="banner warn">Your registration is {profile.approvalStatus.toLowerCase()}. Contact support to find out why.</p>
      )}

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      {approved && (
        <>
          <section className="stack">
            <label className="switch">
              <input type="checkbox" checked={profile.isAvailable} onChange={(e) => void setAvailability(e.target.checked)} />
              <span>{profile.isAvailable ? "You're available for requests" : "You're offline. Turn this on to receive requests."}</span>
            </label>
          </section>

          <section className="stack">
            <h2>Requests for you</h2>
            {offers.length === 0 ? (
              <p className="muted">
                {profile.isAvailable ? "Nothing right now. New requests appear here the moment they come in." : "Go available to start receiving requests."}
              </p>
            ) : (
              <ul className="rows">
                {offers.map((o) => (
                  <li key={o.offerId} className="offer">
                    <div>
                      <strong>{CATEGORY_LABEL[o.category]}</strong>
                      <div className="muted">
                        {INCIDENT_LABEL[o.incidentType]}, {formatKm(o.distanceMeters)} away
                      </div>
                      {o.description && <p className="said">&ldquo;{o.description}&rdquo;</p>}
                      <Countdown until={o.expiresAt} />
                    </div>
                    <div className="row">
                      <button className="btn primary" onClick={() => void respond(o, true)}>
                        Accept
                      </button>
                      <button className="btn quiet" onClick={() => void respond(o, false)}>
                        Decline
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="stack">
            <h2>Your jobs</h2>
            {jobs.length === 0 ? (
              <p className="muted">Accepted requests show up here so you can update their progress.</p>
            ) : (
              <ul className="rows">
                {jobs.map((j) => (
                  <li key={`${j.caseId}-${j.category}`}>
                    <Link to={`/case/${j.caseId}`} className="row-link">
                      <span className="row-title">
                        {CATEGORY_LABEL[j.category]}, {INCIDENT_LABEL[j.incidentType]}
                      </span>
                      <span className={`status status-${j.status}`}>{STATUS_LABEL[j.status]}</span>
                      <span className="muted">{formatTime(j.createdAt)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}

      <section className="stack">
        <h2>Your base location</h2>
        <p className="muted">Requests are matched to you by distance from here. You can change it when you move.</p>
        <div className="row wrap">
          <button className="btn" onClick={useMyLocation}>
            Use my location
          </button>
          {newSpot && (
            <button className="btn primary" onClick={() => void saveLocation()}>
              Save new location
            </button>
          )}
        </div>
        <MapView value={newSpot ?? base} onChange={setNewSpot} height={260} />
        <p className="muted">Services: {profile.categories.map((c) => CATEGORY_LABEL[c]).join(", ")}. Travel up to {profile.serviceRadiusMeters / 1000} km.</p>
      </section>
    </main>
  );
}

function Countdown({ until }: { until: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  const left = Math.max(0, Math.floor((new Date(until).getTime() - now) / 1000));
  const text = left === 0 ? "This request has expired." : `Respond within ${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`;
  return <p className="countdown">{text}</p>;
}
