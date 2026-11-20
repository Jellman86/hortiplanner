import { api } from "../api/client";
import type { Alert } from "./needs";

// What needs this person's look, fetched from the server: when the app starts, when it comes back into view or gets
// its signal back, every five minutes while it's in view, after work is recorded, on returning to Record work and on
// opening the list. Nothing is pushed.
export const needs = $state({
  alerts: [] as Alert[],
  loaded: false,
  /** When the list was last checked, in milliseconds. */
  checkedAt: null as number | null,
  offline: false,
  /** The server couldn't give the list, though there was a signal. */
  failed: false,
});

const every = 5 * 60 * 1000;

// Each load is numbered, so an answer that arrives after a later one, or after someone else has signed in, is dropped.
let latest = 0;

function reset() {
  latest += 1;
  needs.alerts = [];
  needs.loaded = false;
  needs.checkedAt = null;
  needs.offline = false;
  needs.failed = false;
}

export async function loadNeeds() {
  const mine = ++latest;

  try {
    const { data } = await api.GET("/api/alerts");

    if (mine !== latest) {
      return;
    }

    if (data) {
      needs.alerts = data;
      needs.loaded = true;
      needs.checkedAt = Date.now();
      needs.offline = false;
      needs.failed = false;
    } else {
      needs.failed = true;
    }
  } catch {
    // No signal: the last list stays, with when it was checked.
    if (mine === latest) {
      needs.offline = true;
    }
  }
}

/**
 * Keeps the list fresh while the app is in view, for the person signed in. It starts empty, so nobody sees the last
 * person's list, and is emptied again when they leave. The first load comes from the app's own refresh on starting.
 */
export function watchNeeds() {
  let timer: ReturnType<typeof setInterval> | undefined;
  reset();

  const start = () => {
    clearInterval(timer);
    timer = setInterval(() => void loadNeeds(), every);
  };

  const visible = () => {
    if (document.visibilityState === "visible") {
      void loadNeeds();
      start();
    } else {
      clearInterval(timer);
    }
  };

  const online = () => void loadNeeds();

  start();
  document.addEventListener("visibilitychange", visible);
  window.addEventListener("online", online);

  return () => {
    clearInterval(timer);
    document.removeEventListener("visibilitychange", visible);
    window.removeEventListener("online", online);
    reset();
  };
}
