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

/** Work recorded on this device that the server refused, as an item of its own, first in the list. */
export function withRefused(alerts: Alert[], refused: number): Alert[] {
  if (refused === 0) {
    return alerts;
  }

  const one = refused === 1;
  return [
    {
      kind: "Refused",
      title: one
        ? "1 thing you recorded couldn't be saved"
        : `${refused} things you recorded couldn't be saved`,
      why: one
        ? "It's still on this device. You can try it again, or forget it, from Record work."
        : "They're still on this device. You can try each one again, or forget it, from Record work.",
      where: "record",
      count: refused,
      level: "Problem",
    },
    ...alerts,
  ];
}

/** The bell's name, which carries the number, so the badge itself isn't read out. */
export function bellName(count: number): string {
  return count === 0
    ? "Needs a look, nothing just now"
    : count === 1
      ? "Needs a look, 1 thing"
      : `Needs a look, ${count} things`;
}

/** The number on the bell: nothing at nought, and "9+" above nine (the bell's name still gives the true number). */
export function badgeText(count: number): string {
  return count === 0 ? "" : count > 9 ? "9+" : String(count);
}

/**
 * What to say when the list grows while the app is open: the new item if there's one, or how many more. Nothing is
 * said on first loading, when the bell's name already says it, nor when the list shrinks.
 */
export function announcement(
  before: Alert[] | null,
  after: Alert[],
): string | null {
  if (before === null || after.length <= before.length) {
    return null;
  }

  const known = new Set(before.map((alert) => alert.kind));
  const added = after.filter((alert) => !known.has(alert.kind));

  return added.length === 1
    ? `Something new needs a look: ${added[0].title}.`
    : `${after.length - before.length} more things need a look.`;
}
