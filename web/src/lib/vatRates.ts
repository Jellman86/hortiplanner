import { addDays } from "./days";

export interface RateChange {
  band: string;
  percent: number | string;
  from: string;
  setBy?: string | null;
}

/** A day as "1 April 2027". */
export function plainDay(day: string): string {
  return new Date(`${day}T12:00:00`).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

/**
 * Each change after the starting rates, newest first, as a sentence: what the rate was until when and who changed
 * it to what, or, for a second change on the same day, from what to what.
 */
export function rateHistory(changes: RateChange[]): string[] {
  return changes
    .map((change, index) => ({ change, index }))
    .filter(({ change }) => change.setBy)
    .map(({ change, index }) => {
      const band = change.band === "Standard" ? "standard" : "reduced";
      const before = changes
        .slice(0, index)
        .filter((earlier) => earlier.band === change.band)
        .at(-1);
      const to = Number(change.percent);

      if (!before) {
        return `${change.setBy} set the ${band} rate to ${to}% on ${plainDay(change.from)}.`;
      }

      return before.from === change.from
        ? `${change.setBy} changed the ${band} rate again on ${plainDay(change.from)}, from ${Number(before.percent)}% to ${to}%.`
        : `The ${band} rate was ${Number(before.percent)}% until ${plainDay(addDays(change.from, -1))}. ${change.setBy} changed it to ${to}% on ${plainDay(change.from)}.`;
    })
    .reverse();
}
