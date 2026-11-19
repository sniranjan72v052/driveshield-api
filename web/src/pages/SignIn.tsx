import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { messageOf } from "../api/client";
import type { OtpStarted } from "../api/types";
import { useAuth } from "../auth";
import OtpEntry from "../components/OtpEntry";

/** Customers and providers sign in the same way: an email address or mobile number, then a 6-digit code. No password. */
export default function SignIn() {
  const { startSignIn, finishSignIn } = useAuth();
  const [params] = useSearchParams();
  const intent = params.get("intent");
  const [identifier, setIdentifier] = useState("");
  const [started, setStarted] = useState<OtpStarted | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = async (e: FormEvent) => {
    e.preventDefault();
    const value = identifier.trim();
    if (!value) return;
    setBusy(true);
    setError(null);
    try {
      setStarted(await startSignIn(value));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="signin">
      <h1>{intent === "customer" ? "Join as a customer" : intent === "provider" ? "Register as a provider" : "Sign in"}</h1>
      <p className="lead">
        {intent === "provider"
          ? "Sign in or create your account first, then register your business."
          : intent === "customer"
            ? "Create your account in a minute. We'll send a code to your email or mobile number."
            : "Welcome back. We'll send a code to your email or mobile number."}
      </p>

      {started ? (
        <OtpEntry
          started={started}
          onVerify={finishSignIn}
          onResend={() => startSignIn(identifier.trim())}
          onBack={() => setStarted(null)}
          backLabel="Use a different email or number"
        />
      ) : (
        <form onSubmit={send} className="stack">
          <label htmlFor="who">Email or mobile number</label>
          <input
            id="who"
            value={identifier}
            onChange={(e) => setIdentifier(e.target.value)}
            autoFocus
            autoComplete="username"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            placeholder="you@example.com or 98765 43210"
          />
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" className="btn primary" disabled={busy || !identifier.trim()}>
            {busy ? "Sending…" : "Send me a code"}
          </button>
          <p className="note">
            We'll send a 6-digit code. There is no password to remember. Drivers and service providers sign in the same
            way. If you are new, your account is created when you enter the code.
          </p>
        </form>
      )}
    </main>
  );
}
