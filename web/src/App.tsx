import { useState, type ReactNode } from "react";
import { BrowserRouter, Link, NavLink, Navigate, Route, Routes, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { AuthProvider, useAuth } from "./auth";
import { apiBase } from "./api/client";
import Brand from "./components/Brand";
import Home from "./pages/Home";
import SignIn from "./pages/SignIn";
import NamePrompt from "./pages/NamePrompt";
import Account from "./pages/Account";
import Vehicles from "./pages/Vehicles";
import VehiclePrompt from "./pages/VehiclePrompt";
import Report from "./pages/Report";
import MyCases from "./pages/MyCases";
import CasePage from "./pages/CasePage";
import ProviderHome from "./pages/ProviderHome";
import Admin from "./pages/Admin";
import AdminPhotos from "./pages/AdminPhotos";
import AdminRequests from "./pages/AdminRequests";
import AdminRequest from "./pages/AdminRequest";
import AdminCustomers from "./pages/AdminCustomers";
import AdminProviders from "./pages/AdminProviders";

/** A header with just the logo, for the one-off screens shown before the app proper. */
function Plain({ children }: { children: ReactNode }) {
  return (
    <>
      <header className="topbar">
        <Brand />
      </header>
      {children}
    </>
  );
}

/** A signed-out visitor asked for a page that needs an account: sign in, then carry on to it. */
function ToSignIn() {
  const { pathname, search } = useLocation();
  const intent = pathname.startsWith("/provider") ? "provider" : pathname.startsWith("/help") ? "customer" : null;
  const next = encodeURIComponent(pathname + search);
  return <Navigate replace to={`/signin?next=${next}${intent ? `&intent=${intent}` : ""}`} />;
}

/** Someone who just signed in is sent to the page they were heading for. */
function AfterSignIn() {
  const [params] = useSearchParams();
  const next = params.get("next");
  const safe = next && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/signin") ? next : "/";
  return <Navigate replace to={safe} />;
}

function Shell() {
  const { me, ready, error, retry, signOut } = useAuth();
  const navigate = useNavigate();
  const leave = async () => {
    await signOut();
    navigate("/");
  };
  // The name and vehicle questions are shown once per visit and can be skipped:
  // someone asking for help should not be held up.
  const [skippedName, setSkippedName] = useState(false);
  const [skippedVehicle, setSkippedVehicle] = useState(false);

  if (!ready) {
    return (
      <Plain>
        <main>
          <p className="muted">Loading…</p>
        </main>
      </Plain>
    );
  }

  // A saved sign-in exists, but the server could not be reached to check it.
  if (!me && error) {
    return (
      <Plain>
        <main>
          <h1>Can't reach DriveShield</h1>
          <p className="error" role="alert">
            {error}
          </p>
          <p>
            The app is looking for the API at <code>{apiBase}</code>. Start the API in Visual Studio (press F5), then try again.
          </p>
          <div className="row wrap">
            <button className="btn primary" onClick={() => void retry()}>
              Try again
            </button>
            <button className="btn quiet" onClick={() => void signOut()}>
              Sign out
            </button>
          </div>
        </main>
      </Plain>
    );
  }

  // ---------------------------------------------------------------- visitors who are not signed in
  if (!me) {
    return (
      <>
        <header className="topbar">
          <Brand />
          <nav aria-label="Main">
            <NavLink to="/" end>
              Home
            </NavLink>
          </nav>
          <div className="topbar-actions">
            <Link className="btn primary" to="/signin">
              Sign in
            </Link>
            <Link className="btn primary hide-small" to="/signin?next=%2Fhelp&intent=customer">
              Get started
            </Link>
          </div>
        </header>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/signin" element={<SignIn />} />
          <Route path="*" element={<ToSignIn />} />
        </Routes>
      </>
    );
  }

  // ---------------------------------------------------------------- signed in
  if (!me.displayName && !skippedName) {
    return (
      <Plain>
        <NamePrompt onSkip={() => setSkippedName(true)} />
      </Plain>
    );
  }
  // A new customer is asked for their vehicle once (providers and admins never are). It can be skipped and added later.
  if (me.role === "Customer" && me.vehicleCount === 0 && !skippedVehicle) {
    return (
      <Plain>
        <VehiclePrompt onSkip={() => setSkippedVehicle(true)} />
      </Plain>
    );
  }

  const shownName = me.displayName || me.email || me.loginPhone || "Account";

  return (
    <>
      <header className="topbar">
        <Brand />
        <nav aria-label="Main">
          <NavLink to="/" end>
            Home
          </NavLink>
          {me.role === "Admin" ? (
            <NavLink to="/admin/requests">Admin</NavLink>
          ) : (
            <>
              <NavLink to="/help">Get help</NavLink>
              <NavLink to="/cases">My cases</NavLink>
              <NavLink to="/provider">Provider</NavLink>
            </>
          )}
        </nav>
        <div className="who">
          <NavLink to="/account" className="who-name">
            {shownName}
          </NavLink>
          <button className="btn quiet" onClick={() => void leave()}>
            Sign out
          </button>
        </div>
      </header>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/signin" element={<AfterSignIn />} />
        <Route path="/help" element={<Report />} />
        <Route path="/cases" element={<MyCases />} />
        <Route path="/case/:id" element={<CasePage />} />
        <Route path="/provider" element={<ProviderHome />} />
        <Route path="/account" element={<Account />} />
        <Route path="/vehicles" element={me.role === "Customer" ? <Vehicles /> : <Navigate to="/" replace />} />
        <Route path="/admin" element={<Admin />} />
        <Route path="/admin/photos" element={<AdminPhotos />} />
        <Route path="/admin/requests" element={<AdminRequests />} />
        <Route path="/admin/requests/:id" element={<AdminRequest />} />
        <Route path="/admin/customers" element={<AdminCustomers />} />
        <Route path="/admin/providers" element={<AdminProviders />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Shell />
      </AuthProvider>
    </BrowserRouter>
  );
}
