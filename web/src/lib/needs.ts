import type { Schemas } from "./types";

export type Alert = Schemas["Alert"];

/** Where each item is dealt with, as its button says: "Go to …". */
export const placeNames: Record<string, string> = {
  record: "Record work",
  data: "Data and backups",
  nursery: "Nursery details",
  invoices: "Invoices",
  production: "Production",
  jobs: "Jobs",
};

/** The card's coloured edge: red for a problem, amber for something with a date, blue for the rest. */
export function levelClass(alert: Alert): string {
  return alert.level === "Problem"
    ? "level-problem"
    : alert.level === "Soon"
      ? "level-soon"
      : "";
}

/**
 * The items to show on Record work: jobs due are left out there, because Record work lists them itself, ready to
 * tick off.
 */
export function forRecordWork(alerts: Alert[]): Alert[] {
  return alerts.filter((alert) => alert.kind !== "JobsDue");
}
