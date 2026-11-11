import { Link } from "react-router-dom";
import { useAuth } from "../auth";
import { ALL_CATEGORIES, CATEGORY_LABEL, INCIDENTS } from "../api/labels";
import type { ServiceCategory } from "../api/types";
import { CarGlyph, IncidentIcon, PinGlyph, ShieldTick, TickGlyph } from "../components/Icons";
import SiteFooter from "../components/SiteFooter";

const STEPS = [
  { title: "Report the incident", text: "Choose what happened, place a pin on the map and add up to 2 photos." },
  { title: "Nearby help is alerted", text: "Approved providers close to you are asked, and the first to accept takes your request." },
  { title: "Follow every step", text: "Watch progress live, from accepted and on the way to resolved, with photos and notes at each step." },
  { title: "Rate the service", text: "Tell others how it went with a star rating and a short review." },
];

const SERVICE_TEXT: Record<ServiceCategory, string> = {
  InsuranceClaimHelp: "Paperwork, photos and guidance for your claim.",
  TowingOnSite: "A tow to the workshop, or a fix where you stand.",
  RoadsideAssistance: "Jump-starts, flat tyres, fuel and lock-outs.",
  LegalSupport: "Advice on fines, towing and accident reports.",
  WorkshopGuidance: "Repair and estimate advice before you pay.",
  EvVehicleAdvice: "Charging, battery and range help for electric cars.",
  WarrantyAssistance: "Find out what your warranty covers and how to claim.",
};

/** The front page. Visitors see how it works and can sign in; signed-in people get straight to their next step. */
export default function Home() {
  const { me } = useAuth();
  const signedIn = !!me;
  const isAdmin = me?.role === "Admin";
  const firstName = me?.displayName?.trim().split(/\s+/)[0];

  // Where each button goes. Signed-out visitors sign in first and are sent on afterwards.
  const join = (next: string, intent: "customer" | "provider") => `/signin?next=${encodeURIComponent(next)}&intent=${intent}`;
  const helpTo = signedIn ? "/help" : join("/help", "customer");
  const providerTo = signedIn ? "/provider" : join("/provider", "provider");

  return (
    <main className="home">
      <section className="hero">
        <div className="hero-inner">
          <div>
            <p className="eyebrow">Help when the road gets tough</p>
            <h1>
              {signedIn && firstName ? (
                <>
                  Welcome back, <span className="accent">{firstName}.</span>
                </>
              ) : (
                <>
                  Every journey deserves a <span className="accent">safer tomorrow.</span>
                </>
              )}
            </h1>
            <p className="lead">Reliable vehicle assistance, nearby service providers and clear updates from first alert to resolution.</p>

            <div className="hero-actions">
              {!signedIn && (
                <>
                  <Link className="btn primary big" to="/signin">
                    Sign in
                  </Link>
                  <Link className="btn big" to={join("/help", "customer")}>
                    Join as a customer
                  </Link>
                  <Link className="btn big" to={providerTo}>
                    Register as provider
                  </Link>
                </>
              )}
              {signedIn && !isAdmin && (
                <>
                  <Link className="btn primary big" to="/help">
                    Get help now
                  </Link>
                  <Link className="btn big" to="/cases">
                    My cases
                  </Link>
                  <Link className="btn big" to="/provider">
                    {me?.providerId ? "Provider dashboard" : "Register as provider"}
                  </Link>
                </>
              )}
              {isAdmin && (
                <>
                  <Link className="btn primary big" to="/admin/requests">
                    Open requests
                  </Link>
                  <Link className="btn big" to="/admin/providers">
                    Providers
                  </Link>
                  <Link className="btn big" to="/admin">
                    Approvals
                  </Link>
                </>
              )}
            </div>

            <ul className="trust">
              <li>
                <ShieldTick /> Customer-first support
              </li>
              <li>
                <PinGlyph size={20} /> Nearby help within 15 km
              </li>
            </ul>
          </div>

          <div className="hero-art" aria-hidden>
            <div className="art-ring" />
            <div className="art-ring-inner" />
            <div className="art-tile">
              <CarGlyph />
            </div>
            <div className="art-pin">
              <PinGlyph />
            </div>
            <div className="art-status">
              <span className="dot" />
              <div>
                <strong>Help is on the way</strong>
                <span>Track each service update</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="section" aria-labelledby="how">
        <div className="section-head">
          <p className="eyebrow">How it works</p>
          <h2 id="how">From first alert to a resolved problem</h2>
        </div>
        <ol className="steps">
          {STEPS.map((s) => (
            <li key={s.title}>
              <h3>{s.title}</h3>
              <p>{s.text}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="section" aria-labelledby="incidents" style={{ paddingTop: 0 }}>
        <div className="section-head">
          <p className="eyebrow">What happened?</p>
          <h2 id="incidents">Help for every kind of incident</h2>
        </div>
        <ul className="cards">
          {INCIDENTS.map((i) => (
            <li key={i.type}>
              <Link className="card-link" to={helpTo}>
                <IncidentIcon type={i.type} />
                <strong>{i.label}</strong>
                <span>{i.hint}</span>
              </Link>
            </li>
          ))}
        </ul>
      </section>

      <section className="section" aria-labelledby="services" style={{ paddingTop: 0 }}>
        <div className="section-head">
          <p className="eyebrow">Services</p>
          <h2 id="services">The right expert, close by</h2>
        </div>
        <ul className="services">
          {ALL_CATEGORIES.map((c) => (
            <li key={c}>
              <TickGlyph />
              <div>
                <strong>{CATEGORY_LABEL[c]}</strong>
                <span>{SERVICE_TEXT[c]}</span>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="band">
        <div className="band-inner">
          <div>
            <h2>Run a garage, towing or support service?</h2>
            <p>Register your business, choose the services you offer and how far you will travel, and receive requests from drivers near you.</p>
          </div>
          <Link className="btn big" to={providerTo}>
            {me?.providerId ? "Open provider dashboard" : "Register as provider"}
          </Link>
        </div>
      </section>

      <SiteFooter />
    </main>
  );
}
