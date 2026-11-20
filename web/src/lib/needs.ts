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
 * The items to show on Record work: jobs due and stopped backups are left out there, because Record work shows them
 * itself, as the jobs to tick off and the red card about the backups.
 */
export function forRecordWork(alerts: Alert[]): Alert[] {
  return alerts.filter(
    (alert) => alert.kind !== "JobsDue" && alert.kind !== "BackupsStopped",
  );
}
