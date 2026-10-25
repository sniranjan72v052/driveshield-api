import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";
import { getAccessToken, hubUrl } from "../api/client";

export type LiveState = "connecting" | "live" | "offline";

// The hub needs the sign-in token. SignalR asks for it on every connect and reconnect, so it is never stale.
function buildConnection() {
  return new HubConnectionBuilder().withUrl(hubUrl(), { accessTokenFactory: getAccessToken }).withAutomaticReconnect().build();
}

/**
 * Subscribes to live updates for one case. Whenever the server announces a change (progress, a new, edited or
 * removed post), and right after connecting or reconnecting, onChange is called so the page can reload the case.
 */
export function useCaseLive(caseId: string | undefined, onChange: () => void): LiveState {
  const [state, setState] = useState<LiveState>("connecting");
  const callback = useRef(onChange);
  callback.current = onChange;

  useEffect(() => {
    if (!caseId) return;
    let stopped = false;
    const conn = buildConnection();

    const join = async () => {
      try {
        await conn.invoke("JoinCase", caseId);
        if (!stopped) setState("live");
        callback.current();
      } catch {
        if (!stopped) setState("offline");
      }
    };

    const changed = (e: { caseId: string }) => {
      if (e.caseId === caseId) callback.current();
    };
    conn.on("CaseStatusChanged", changed);
    conn.on("CommentAdded", changed);
    // A post was edited, or a post or photo was deleted or removed.
    conn.on("CommentChanged", changed);
    conn.onreconnecting(() => setState("connecting"));
    conn.onreconnected(() => void join());
    conn.onclose(() => {
      if (!stopped) setState("offline");
    });
    conn.start().then(join).catch(() => {
      if (!stopped) setState("offline");
    });

    return () => {
      stopped = true;
      void conn.stop();
    };
  }, [caseId]);

  return state;
}

/** Providers: get told straight away when a new request is offered to them. */
export function useProviderInbox(enabled: boolean, onOffer: () => void): void {
  const callback = useRef(onOffer);
  callback.current = onOffer;

  useEffect(() => {
    if (!enabled) return;
    const conn = buildConnection();
    const join = async () => {
      try {
        await conn.invoke("JoinProviderInbox");
      } catch {
        /* the page also polls, so a missed join is not fatal */
      }
    };
    conn.on("NewOffer", () => callback.current());
    conn.onreconnected(() => void join());
    conn.start().then(join).catch(() => {});
    return () => {
      void conn.stop();
    };
  }, [enabled]);
}
