import { api } from "../api/client";
import type { Alert } from "./needs";

// What needs this person's look, fetched from the server: when the app starts, when it comes back into view, every
// five minutes while it's in view, after work is recorded and when the list is opened. Nothing is pushed.
export const needs = $state({
  alerts: [] as Alert[],
  loaded: false,
  checkedAt: null as Date | null,
  offline: false,
});

const every = 5 * 60 * 1000;

export async function loadNeeds() {
  try {
    const { data } = await api.GET("/api/alerts");

    if (data) {
      needs.alerts = data;
      needs.loaded = true;
      needs.checkedAt = new Date();
      needs.offline = false;
    }
  } catch {
    // No signal: the last list stays, with when it was checked.
    needs.offline = true;
  }
}

/** Keeps the list fresh while the app is in view. */
export function watchNeeds() {
  let timer: ReturnType<typeof setInterval> | undefined;

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

  void loadNeeds();
  start();
  document.addEventListener("visibilitychange", visible);

  return () => {
    clearInterval(timer);
    document.removeEventListener("visibilitychange", visible);
  };
}
