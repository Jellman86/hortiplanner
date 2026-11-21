import type { Schemas } from "./types";
import { money, plantName } from "./work";

// The words of Figures, What the plants cost and made. Every figure that counts is with VAT while the nursery
// isn't registered for VAT and before VAT once it is; the other is there to compare.

export type MoneyFigures = Schemas["Money"];
export type PlantMoney = Schemas["PlantMoney"];
export type GroupMoney = Schemas["GroupMoney"];
export type StartedIn = Schemas["StartedIn"];
type Pair = Schemas["MoneyPair"];
type Counts = Schemas["VatCounts"];

export const startedChoices: { value: StartedIn; label: string }[] = [
  { value: "ThisYear", label: "Started this year" },
  { value: "LastYear", label: "Started last year" },
  { value: "All", label: "All of them" },
];

export const notKnownYet = "Not known yet";

/** The figure that counts, as a number. */
export function counting(pair: Pair, counts: Counts): number {
  return Number(counts === "BeforeVat" ? pair.beforeVat : pair.withVat);
}

/** The other figure, to compare. */
export function compared(pair: Pair, counts: Counts): number {
  return Number(counts === "BeforeVat" ? pair.withVat : pair.beforeVat);
}

/** "£260.33 before VAT" or "£312.40 with VAT", for under a tile; nothing when the two are the same. */
export function comparedWords(pair: Pair, counts: Counts): string {
  if (money(counting(pair, counts)) === money(compared(pair, counts))) {
    return "";
  }

  return `${money(compared(pair, counts))} ${counts === "BeforeVat" ? "with VAT" : "before VAT"}`;
}

/** The two cost columns, the one that counts first. */
export function costOrder(counts: Counts): ("withVat" | "beforeVat")[] {
  return counts === "BeforeVat"
    ? ["beforeVat", "withVat"]
    : ["withVat", "beforeVat"];
}

/** A cost cell: the amount, "so far" while a batch has no answer, or "Not known yet". */
export function costCell(
  cost: Pair | null,
  complete: boolean,
  side: "withVat" | "beforeVat",
): string {
  if (!cost) {
    return notKnownYet;
  }

  const amount = money(Number(cost[side]));
  return complete ? amount : `${amount} so far`;
}

/** A cell of a figure that counts, or "Not known yet". */
export function figureCell(pair: Pair | null, counts: Counts): string {
  return pair ? money(counting(pair, counts)) : notKnownYet;
}

/** The amber notice while batches have no answer. */
export function unansweredSentence(count: number): string {
  return count === 1
    ? "1 batch has no cost yet, so the figures for its plant are incomplete."
    : `${count} batches have no cost yet, so the figures for their plants are incomplete.`;
}

/** Said beneath the amber notice: what was spent and made so far leaves the unanswered batches out. */
export const soFarNote =
  "What was spent and made so far counts only the batches that have a cost.";

/** The muted note about batches nobody knows the cost of. */
export function notKnownSentence(count: number): string {
  return count === 1
    ? "1 batch is left out of the cost and profit because nobody knows what it cost. What it took is still counted."
    : `${count} batches are left out of the cost and profit because nobody knows what they cost. What they took is still counted.`;
}

/** Said when no batch was started in the time chosen and nothing was sold. */
export function emptySentence(started: StartedIn): string {
  return started === "ThisYear"
    ? "No batches have been started this year yet."
    : started === "LastYear"
      ? "No batches were started last year."
      : "Nothing has a cost or a sale recorded yet.";
}

/**
 * The till beside sales recorded from trays, neither said to be inside the other. Both are what customers paid,
 * VAT included, whichever figures count, as that's what a till total is.
 */
export function tillSentence(
  till: Schemas["Till"],
  started: StartedIn,
): string {
  const taken = money(Number(till.taken));
  const trays = money(Number(till.fromTrays.withVat));
  return started === "ThisYear"
    ? `The till took ${taken} this year. Sales recorded from trays came to ${trays} in the same time.`
    : started === "LastYear"
      ? `The till took ${taken} last year. Sales recorded from trays came to ${trays} in the same time.`
      : `The till has taken ${taken} altogether, and sales recorded from trays have come to ${trays}.`;
}

/** Said on a plant's card when some of its takings came from sales that didn't name a batch. */
export function unbatchedSentence(plant: PlantMoney, counts: Counts): string {
  return plant.unbatched
    ? `${money(counting(plant.unbatched, counts))} of these takings is from plants sold without saying which batch, so it's in the plant's takings but not in any batch's.`
    : "";
}

/** The same, said with the plant's name, for the note under the table. */
export function unbatchedNote(plant: PlantMoney, counts: Counts): string {
  return plant.unbatched
    ? `${money(counting(plant.unbatched, counts))} of the takings for ${plantName(plant.commonName, plant.variety)} is from plants sold without saying which batch, so it's in the plant's takings but not in any batch's.`
    : "";
}

/**
 * The tiles: what was taken, spent, made and lost, with "so far" while a batch has no answer. A figure nobody can
 * work out yet is one sentence in place of the amount.
 */
export function tiles(figures: MoneyFigures): {
  key: string;
  amount: string;
  words: string;
  compare: string;
}[] {
  const { totals, counts } = figures;
  const sofar = totals.complete ? "" : " so far";
  const unknown = (key: string, words: string) => ({
    key,
    amount: "",
    words,
    compare: "",
  });
  const made = totals.made ? counting(totals.made, counts) : null;

  return [
    {
      key: "taken",
      amount: money(counting(totals.taken, counts)),
      words: "taken from plants",
      compare: "",
    },
    totals.spent
      ? {
          key: "spent",
          amount: money(counting(totals.spent, counts)),
          words: `spent${sofar} on what went into them`,
          compare: comparedWords(totals.spent, counts),
        }
      : unknown(
          "spent",
          "What was spent on what went into them isn't known yet.",
        ),
    made === null
      ? unknown("made", "What was made isn't known yet.")
      : {
          key: "made",
          amount: money(Math.abs(made)),
          words: made < 0 ? `more spent than taken${sofar}` : `made${sofar}`,
          compare: "",
        },
    totals.lost
      ? {
          key: "lost",
          amount: money(counting(totals.lost, counts)),
          words: "went on plants that were lost",
          compare: comparedWords(totals.lost, counts),
        }
      : unknown("lost", "What went on plants that were lost isn't known yet."),
  ];
}
