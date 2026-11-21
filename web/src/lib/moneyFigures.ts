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
  { value: "ThisYear", label: "This year" },
  { value: "LastYear", label: "Last year" },
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

/** The muted note about batches nobody knows the cost of. */
export function notKnownSentence(count: number): string {
  return count === 1
    ? "1 batch is left out because nobody knows what it cost."
    : `${count} batches are left out because nobody knows what they cost.`;
}

/** The till beside sales recorded from trays, neither said to be inside the other. */
export function tillSentence(
  till: Schemas["Till"],
  started: StartedIn,
  counts: Counts,
): string {
  const taken = money(Number(till.taken));
  const trays = money(counting(till.fromTrays, counts));
  const first =
    started === "ThisYear"
      ? `The till took ${taken} this year.`
      : started === "LastYear"
        ? `The till took ${taken} last year.`
        : `The till has taken ${taken} altogether.`;
  return `${first} Sales recorded from trays came to ${trays} in the same time.`;
}

/** Said of a plant's takings when some came from sales that didn't name a batch. */
export function unbatchedSentence(plant: PlantMoney, counts: Counts): string {
  return plant.unbatched
    ? `${money(counting(plant.unbatched, counts))} of this was sold without saying which batch, so it's in the plant's takings but not in any batch's.`
    : "";
}

/** The same, said with the plant's name, for the note under the table. */
export function unbatchedNote(plant: PlantMoney, counts: Counts): string {
  return plant.unbatched
    ? `${plantName(plant.commonName, plant.variety)}: ${unbatchedSentence(plant, counts)}`
    : "";
}

/** The tiles: what was taken, spent, made and lost, with "so far" while a batch has no answer. */
export function tiles(
  figures: MoneyFigures,
): { key: string; amount: string; words: string; compare: string }[] {
  const { totals, counts } = figures;
  const sofar = totals.complete ? "" : " so far";
  const made = counting(totals.made, counts);

  return [
    {
      key: "taken",
      amount: money(counting(totals.taken, counts)),
      words: "taken from plants",
      compare: "",
    },
    {
      key: "spent",
      amount: money(counting(totals.spent, counts)),
      words: `spent${sofar} on what went into them`,
      compare: comparedWords(totals.spent, counts),
    },
    {
      key: "made",
      amount: money(Math.abs(made)),
      words: made < 0 ? `lost${sofar} overall` : `made${sofar}`,
      compare: "",
    },
    {
      key: "lost",
      amount: money(counting(totals.lost, counts)),
      words: "went on plants that were lost",
      compare: comparedWords(totals.lost, counts),
    },
  ];
}
