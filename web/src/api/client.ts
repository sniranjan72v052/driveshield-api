// Thin fetch wrapper for the DriveShield API, with sign-in tokens.
//
// Signing in returns an access token (15 minutes, kept in memory) and a refresh token (30 days, kept in
// localStorage so people stay signed in after closing the tab). When the access token is about to run out,
// or the API answers 401, the refresh token is swapped for a new pair. Only one swap runs at a time.
//
// Where the refresh token lives is a trade-off: localStorage can be read by any script running on the page, so keep
// the site free of third-party scripts. A later hardening step is to move it into an httpOnly cookie.

import type { AuthResponse } from "./types";

const BASE: string = import.meta.env.VITE_API_URL ?? "http://localhost:5000";
const REFRESH_KEY = "driveshield.refreshToken";

export const apiBase = BASE;

export class ApiError extends Error {
  status: number;
  /** Seconds to wait before trying again, when the server says so (HTTP 429). */
  retryAfter: number;
  constructor(status: number, message: string, retryAfter = 0) {
    super(message);
    this.status = status;
    this.retryAfter = retryAfter;
  }
}

export function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : "Something went wrong.";
}

const UNREACHABLE = `Can't reach the server at ${BASE}. Check that the API is running.`;

// ---------------------------------------------------------------- session

let accessToken: string | null = null;
let accessExpiresAt = 0; // milliseconds since 1970
let refreshing: Promise<AuthResponse | null> | null = null;
let sessionEndedHandler: (() => void) | null = null;

function readRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_KEY);
  } catch {
    return null;
  }
}

function writeRefreshToken(token: string | null): void {
  try {
    if (token) localStorage.setItem(REFRESH_KEY, token);
    else localStorage.removeItem(REFRESH_KEY);
  } catch {
    /* storage unavailable (private window): the sign-in lasts until the tab closes */
  }
}

export function hasStoredSession(): boolean {
  return readRefreshToken() !== null;
}

export function getRefreshToken(): string | null {
  return readRefreshToken();
}

/** Called when the server says the sign-in is no longer valid, so the app can show the sign-in screen. */
export function onSessionEnded(handler: (() => void) | null): void {
  sessionEndedHandler = handler;
}

export function startSession(auth: { accessToken: string; expiresIn: number; refreshToken: string }): void {
  accessToken = auth.accessToken;
  accessExpiresAt = Date.now() + auth.expiresIn * 1000;
  writeRefreshToken(auth.refreshToken);
}

export function endSession(): void {
  accessToken = null;
  accessExpiresAt = 0;
  writeRefreshToken(null);
}

async function postRefresh(token: string): Promise<AuthResponse | null> {
  let res: Response;
  try {
    res = await fetch(BASE + "/api/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: token }),
    });
  } catch {
    throw new ApiError(0, UNREACHABLE); // a network problem does not end the sign-in
  }
  if (res.status === 401) return null;
  if (!res.ok) throw new ApiError(res.status, "We couldn't refresh your sign-in. Please try again.");
  return (await res.json()) as AuthResponse;
}

async function runRefresh(): Promise<AuthResponse | null> {
  const used = readRefreshToken();
  if (!used) return null;

  let auth = await postRefresh(used);
  if (!auth) {
    // Another tab may have swapped the token a moment ago and saved the newer one. Try that once.
    const latest = readRefreshToken();
    if (latest && latest !== used) auth = await postRefresh(latest);
  }
  if (!auth) {
    endSession();
    sessionEndedHandler?.();
    return null;
  }
  startSession(auth);
  return auth;
}

/** Swaps the refresh token for a new pair. Returns null when the person has to sign in again. */
export function refreshSession(): Promise<AuthResponse | null> {
  if (!refreshing) {
    refreshing = runRefresh().finally(() => {
      refreshing = null;
    });
  }
  return refreshing;
}

/** A valid access token, refreshing first if it is about to expire. Empty text when signed out. */
export async function getAccessToken(): Promise<string> {
  if (!accessToken || Date.now() > accessExpiresAt - 30_000) await refreshSession();
  return accessToken ?? "";
}

// ---------------------------------------------------------------- requests

async function send(method: string, path: string, body: unknown, token: string): Promise<Response> {
  const isForm = body instanceof FormData;
  const headers: Record<string, string> = {};
  // A FormData body (photo uploads) must go out as it is: the browser sets its own multipart Content-Type.
  if (body !== undefined && !isForm) headers["Content-Type"] = "application/json";
  if (token) headers["Authorization"] = `Bearer ${token}`;
  return fetch(BASE + path, {
    method,
    headers,
    body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
  });
}

async function request<T>(method: string, path: string, body?: unknown, anonymous = false): Promise<T> {
  let res: Response;
  try {
    res = await send(method, path, body, anonymous ? "" : await getAccessToken());
    if (res.status === 401 && !anonymous && (await refreshSession())) {
      res = await send(method, path, body, accessToken ?? "");
    }
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(0, UNREACHABLE);
  }

  if (!res.ok) {
    let message = `The request failed (${res.status}).`;
    let retryAfter = 0;
    try {
      const data = await res.json();
      if (data && typeof data.error === "string") message = data.error;
      if (data && typeof data.retryAfterSeconds === "number") retryAfter = data.retryAfterSeconds;
    } catch {
      /* no JSON body */
    }
    if (res.status === 429 && message.startsWith("The request failed")) {
      message = "Too many attempts. Please wait a moment and try again.";
    }
    throw new ApiError(res.status, message, retryAfter);
  }

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body ?? {}),
  put: <T>(path: string, body?: unknown) => request<T>("PUT", path, body ?? {}),
  patch: <T>(path: string, body?: unknown) => request<T>("PATCH", path, body ?? {}),
  delete: <T = void>(path: string) => request<T>("DELETE", path),
  /** For the sign-in calls, which are made before there is a token. */
  postAnonymous: <T>(path: string, body: unknown) => request<T>("POST", path, body, true),
};

/** Downloads a protected file (such as a case photo) with the sign-in token, since <img src> cannot send one. */
export async function fetchBlob(path: string): Promise<Blob> {
  let res: Response;
  try {
    res = await send("GET", path, undefined, await getAccessToken());
    if (res.status === 401 && (await refreshSession())) {
      res = await send("GET", path, undefined, accessToken ?? "");
    }
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(0, UNREACHABLE);
  }
  if (!res.ok) throw new ApiError(res.status, `The file could not be loaded (${res.status}).`);
  return res.blob();
}

/** SignalR hub address. The connection gets its token from getAccessToken (see hooks/useLive.ts). */
export function hubUrl(): string {
  return `${BASE}/hubs/case`;
}
