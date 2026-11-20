import type { Schemas } from "./types";
import type { WorkRequest } from "./work";
import { money } from "./work";

export type Costing = Schemas["BatchCosting"];
export type CostEntry = Schemas["CostEntry"];
export type MoneyPair = Schemas["MoneyPair"];
type Source = Schemas["BatchSource"];

/**
 * Pounds and pence as typed: "2.99", "£2.99" or "3". Null for anything else, including more than two places of
 * pence. Nought is allowed, as it means it cost nothing.
 */
export function readMoney(text: string): number | null {
  const match = /^£?\s*(\d{1,5})(?:\.(\d{1,2}))?$/.exec(text.trim());
  return match ? Number(`${match[1]}.${match[2] ?? "0"}`) : null;
}

export const moneyProblem =
  "Please put the cost in as pounds and pence, such as 2.99.";

/** What the batch's own cost is of: its seed, the plants bought in, or the cuttings. */
export function materialWord(source: Source): string {
  return source === "SeedLot"
    ? "seed"
    : source === "BoughtIn"
      ? "plants"
      : "cuttings";
}

const capital = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** A day as "3 March", with the year when it isn't this year. */
export function costDay(moment: string, today = new Date()): string {
  const day = new Date(moment);
  return day.toLocaleDateString("en-GB", {
    day: "numeric",
    month: "long",
    ...(day.getFullYear() === today.getFullYear() ? {} : { year: "numeric" }),
  });
}

const percent = (rate: number | string | null | undefined) =>
  `${Number(rate ?? 0)}%`;

/** The first entry a cost was corrected from, for who added it and when, however many times its VAT was changed. */
export function firstOf(entry: CostEntry, history: CostEntry[]): CostEntry {
  let first = entry;

  for (;;) {
    const earlier = history.find(
      (candidate) =>
        candidate.id === first.replacesId &&
        candidate.answer === "Amount" &&
        Number(candidate.amountPaid) === Number(first.amountPaid),
    );

    if (!earlier) {
      return first;
    }

    first = earlier;
  }
}

/** The sentence about the VAT in a cost: assumed or said, with VAT in it or added to it, or none. */
export function vatSentence(entry: CostEntry, what: string): string {
  const rate = Number(entry.vatRate ?? 0);

  if (!entry.vatSaid) {
    return rate === 0
      ? `This plant has no VAT on it, so the ${what} is taken as having none either.`
      : `Nobody has said how much of it was VAT, so it's taken as including VAT at ${percent(rate)}.`;
  }

  if (rate === 0) {
    return "You said there was no VAT on it, so both figures are the same.";
  }

  return entry.includesVat
    ? `You said it included VAT at ${percent(rate)}.`
    : `You said ${money(Number(entry.amountPaid))} was the price before VAT, and VAT at ${percent(rate)} was added.`;
}

/** What's said beneath a cost: who added it and when, then its VAT. */
export function entrySentence(
  entry: CostEntry,
  history: CostEntry[],
  what: string,
): string {
  const first = firstOf(entry, history);
  return `${first.addedBy} added the cost of the ${what} on ${costDay(first.addedAt)}. ${vatSentence(entry, what)}`;
}

/** What's said beneath anything else added to a batch. */
export function otherSentence(entry: CostEntry, history: CostEntry[]): string {
  const first = firstOf(entry, history);
  return `${first.addedBy} added this on ${costDay(first.addedAt)}. ${vatSentence(entry, "cost")}`;
}

/** The line beneath the seed or plants, for every state but a typed amount. */
export function materialSentence(costing: Costing): string {
  const what = materialWord(costing.source);
  const material = costing.material;

  switch (material.state) {
    case "Free":
      return `${material.entry?.addedBy ?? "Someone"} said on ${costDay(material.entry?.addedAt ?? "")} that the ${what} cost nothing.`;
    case "NotKnown":
      return `You said nobody knows what the ${what} cost, so this batch has no total.`;
    case "Cuttings":
      return "These were cuttings taken here, so they cost nothing to buy.";
    case "Purchase": {
      const purchase = material.purchase;
      const from = `The ${what} ${what === "seed" ? "is" : "are"} from the purchase from ${purchase?.supplier} on ${costDay(`${purchase?.purchasedOn}T12:00:00`)}, which gives the cost and its VAT.`;

      if (!purchase?.shared) {
        return from;
      }

      return purchase.equally
        ? `${from} Other batches were started from the same purchase, so the cost is shared equally between them, because one was sown without a count.`
        : `${from} Other batches were started from the same purchase, so this is this batch's share of the cost, by how many plants each one started with.`;
    }
    default:
      return `Nobody has said what the ${what} for this batch cost, so we can't work out what the batch has cost.`;
  }
}

/** Said when a purchase has taken over from a cost typed in. */
export function supersededSentence(entry: CostEntry): string {
  const typed =
    entry.answer === "Free"
      ? `the answer that it cost nothing, given on ${costDay(entry.addedAt)},`
      : `the ${money(Number(entry.amountPaid))} typed in on ${costDay(entry.addedAt)}`;
  return `The purchase now gives this batch's cost, so ${typed} is no longer counted.`;
}

/** An amount with what VAT was in it: "£2.99 including VAT at 20%", "£2.49 before VAT at 20%" or "£2.99 with no VAT". */
export function amountWithVat(entry: CostEntry): string {
  const paid = money(Number(entry.amountPaid));
  const rate = Number(entry.vatRate ?? 0);

  if (rate === 0) {
    return `${paid} with no VAT`;
  }

  return entry.includesVat
    ? `${paid} including VAT at ${percent(rate)}`
    : `${paid} before VAT at ${percent(rate)}`;
}

/**
 * An earlier answer, kept in the list once something has replaced it, in three parts so only the amount is
 * struck through: the word before it, the amount, and the rest.
 */
export function replacedParts(
  entry: CostEntry,
  history: CostEntry[],
): { lead: string; struck: string; rest: string } {
  const day = costDay(entry.addedAt);

  switch (entry.answer) {
    case "Amount": {
      const removed = history.some(
        (later) => later.replacesId === entry.id && later.answer === "Removed",
      );
      return {
        lead: removed ? "Removed: " : "Replaced: ",
        struck: amountWithVat(entry),
        rest: `${entry.note ? ` for ${entry.note}` : ""}, added by ${entry.addedBy} on ${day}.`,
      };
    }
    case "Free":
      return {
        lead: `Replaced: ${entry.addedBy} said on ${day} that it cost nothing.`,
        struck: "",
        rest: "",
      };
    case "NotKnown":
      return {
        lead: `Replaced: ${entry.addedBy} said on ${day} that nobody knows what it cost.`,
        struck: "",
        rest: "",
      };
    default:
      return {
        lead: `Replaced: ${entry.addedBy} removed the cost on ${day}.`,
        struck: "",
        rest: "",
      };
  }
}

/** The same, as one sentence. */
export function replacedSentence(
  entry: CostEntry,
  history: CostEntry[] = [],
): string {
  const parts = replacedParts(entry, history);
  return `${parts.lead}${parts.struck}${parts.rest}`;
}

/** The row's name in the table. */
export function rowName(entry: CostEntry | null, source: Source): string {
  return entry?.kind === "Other"
    ? `Something else: ${entry.note}`
    : capital(materialWord(source));
}

/** Which figures count, said above the table. */
export function countsSentence(registered: boolean): string {
  return registered
    ? "The nursery is registered for VAT, so the VAT on what it buys can be claimed back. The figures before VAT are the ones that count, and the figures with VAT are there to compare."
    : "The nursery isn't registered for VAT, so the VAT on what it buys can't be claimed back. The figures with VAT are the ones that count, and the figures before VAT are there to compare.";
}

/** Said after the VAT on a cost is changed. */
export function madeSentence(cost: MoneyPair): string {
  return `That makes it ${money(Number(cost.withVat))} with VAT and ${money(Number(cost.beforeVat))} before VAT.`;
}

type CostBody = Schemas["CostRequest"];

/** A cost, as work: sent now, or kept on the phone until there's a signal. */
export function costWork(
  batchId: string,
  body: Omit<CostBody, "eventId" | "occurredAt"> & { occurredAt?: string },
): WorkRequest {
  return {
    url: `/api/batches/${batchId}/costs`,
    body: {
      ...body,
      eventId: crypto.randomUUID(),
      occurredAt: body.occurredAt ?? new Date().toISOString(),
    },
  };
}
