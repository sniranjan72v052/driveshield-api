import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import {
  api,
  endSession,
  getRefreshToken,
  hasStoredSession,
  messageOf,
  onSessionEnded,
  refreshSession,
  startSession,
} from "./api/client";
import type { AuthResponse, Me, OtpStarted } from "./api/types";

interface AuthState {
  me: Me | null;
  /** False until we know whether the person is already signed in. */
  ready: boolean;
  /** Set when a saved sign-in could not be checked because the server could not be reached. */
  error: string | null;
  retry: () => Promise<void>;
  /** Sends a 6-digit code to the email or mobile number. */
  startSignIn: (identifier: string) => Promise<OtpStarted>;
  /** Checks the code and signs in. Throws with a readable message when it is wrong. */
  finishSignIn: (challengeId: string, code: string) => Promise<void>;
  signOut: () => Promise<void>;
  setName: (name: string) => Promise<void>;
  setMe: (me: Me) => void;
  reloadMe: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Is there a saved sign-in? Swap it for fresh tokens and load who the person is.
  const restore = useCallback(async () => {
    setError(null);
    if (!hasStoredSession()) {
      setMe(null);
      setReady(true);
      return;
    }
    try {
      const auth = await refreshSession();
      setMe(auth ? auth.user : null);
    } catch (e) {
      setMe(null);
      setError(messageOf(e));
    } finally {
      setReady(true);
    }
  }, []);

  useEffect(() => {
    void restore();
  }, [restore]);

  // If the server later says the sign-in is no longer valid, go back to the sign-in screen.
  useEffect(() => {
    onSessionEnded(() => setMe(null));
    return () => onSessionEnded(null);
  }, []);

  const reloadMe = useCallback(async () => {
    try {
      setMe(await api.get<Me>("/api/me"));
    } catch {
      /* keep what we have; the next request will show the problem */
    }
  }, []);

  const startSignIn = useCallback(
    (identifier: string) => api.postAnonymous<OtpStarted>("/api/auth/otp/start", { identifier }),
    [],
  );

  const finishSignIn = useCallback(async (challengeId: string, code: string) => {
    const auth = await api.postAnonymous<AuthResponse>("/api/auth/otp/verify", { challengeId, code });
    startSession(auth);
    setMe(auth.user);
  }, []);

  const signOut = useCallback(async () => {
    const token = getRefreshToken();
    endSession();
    setMe(null); // signed out on screen straight away
    if (token) {
      try {
        await api.postAnonymous("/api/auth/logout", { refreshToken: token });
      } catch {
        /* the token runs out by itself */
      }
    }
  }, []);

  const setName = useCallback(async (name: string) => {
    setMe(await api.put<Me>("/api/me", { displayName: name }));
  }, []);

  return (
    <AuthContext.Provider
      value={{ me, ready, error, retry: restore, startSignIn, finishSignIn, signOut, setName, setMe, reloadMe }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
