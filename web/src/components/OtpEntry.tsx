import { useEffect, useRef, useState, type FormEvent } from "react";
import { ApiError, messageOf } from "../api/client";
import type { OtpStarted } from "../api/types";

interface Props {
  started: OtpStarted;
  /** Checks the code. Throws with a readable message when it is wrong. */
  onVerify: (challengeId: string, code: string) => Promise<void>;
  /** Asks for a new code. */
  onResend: () => Promise<OtpStarted>;
  onBack: () => void;
  backLabel: string;
}

/** "We sent a code to ...": the box for the 6 digits, the resend countdown, and (on a developer machine) the code itself. */
export default function OtpEntry({ started: first, onVerify, onResend, onBack, backLabel }: Props) {
  const [started, setStarted] = useState(first);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [wait, setWait] = useState(first.resendAfterSeconds);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    input.current?.focus();
  }, [started.challengeId]);

  useEffect(() => {
    if (wait <= 0) return;
    const t = setTimeout(() => setWait((w) => w - 1), 1000);
    return () => clearTimeout(t);
  }, [wait]);

  const digits = code.replace(/\D/g, ""); // "123 456" works too

  const submit = async () => {
    if (digits.length !== 6 || busy) return;
    setBusy(true);
    setError(null);
    try {
      await onVerify(started.challengeId, digits);
      // On success the parent moves on and this box disappears.
    } catch (e) {
      setError(messageOf(e));
      setCode("");
      setBusy(false);
      input.current?.focus();
    }
  };

  // Check the code as soon as the sixth digit is typed.
  useEffect(() => {
    if (digits.length === 6) void submit();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [digits]);

  const resend = async () => {
    setBusy(true);
    setError(null);
    try {
      const next = await onResend();
      setStarted(next);
      setWait(next.resendAfterSeconds);
      setCode("");
    } catch (e) {
      setError(messageOf(e));
      if (e instanceof ApiError && e.retryAfter > 0) setWait(e.retryAfter);
    } finally {
      setBusy(false);
    }
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    void submit();
  };

  return (
    <form onSubmit={onSubmit} className="stack">
      <p>
        We sent a 6-digit code to <strong>{started.destinationMasked}</strong>. It works for {Math.round(started.expiresInSeconds / 60)} minutes.
      </p>

      {started.devCode && (
        <p className="dev-code">
          Development only: your code is <strong>{started.devCode}</strong>{" "}
          <button type="button" className="linklike" onClick={() => setCode(started.devCode ?? "")}>
            Fill it in
          </button>
        </p>
      )}

      <label htmlFor="otp-code">Enter the code</label>
      <input
        id="otp-code"
        ref={input}
        className="code-input"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9 ]*"
        maxLength={7}
        value={code}
        onChange={(e) => setCode(e.target.value)}
        disabled={busy}
      />

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}

      <button type="submit" className="btn primary" disabled={busy || digits.length !== 6}>
        {busy ? "Checking…" : "Verify"}
      </button>

      <div className="row wrap">
        <button type="button" className="linklike" onClick={() => void resend()} disabled={busy || wait > 0}>
          {wait > 0 ? `Send a new code in ${wait}s` : "Send a new code"}
        </button>
        <button type="button" className="linklike" onClick={onBack} disabled={busy}>
          {backLabel}
        </button>
      </div>
    </form>
  );
}
