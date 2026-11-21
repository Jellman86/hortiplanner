import type { Schemas } from "./types";
import type { WorkRequest } from "./work";
import { money, plantName } from "./work";

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

/** Half a price, in whole pence, rounding a half penny up: £1.15 is £0.58, and £2.99 is £1.50. */
export function halfOf(price: number): number {
  return Math.round(Math.round(price * 100) / 2) / 100;
}

/** Money to the penny, said as "about" when the figure isn't a whole number of pence, such as £0.71 for £0.7083. */
export function aboutMoney(value: number): string {
  const pence = value * 100;
  return Math.abs(pence - Math.round(pence)) > 1e-6
    ? `about ${money(value)}`
    : money(value);
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

type Missing = Schemas["MissingCost"];

/** "C000101", "C000101 and C000102", or "C000101, C000102 and C000103". */
function trayList(trays: string[]): string {
  return trays.length < 2
    ? trays.join("")
    : `${trays.slice(0, -1).join(", ")} and ${trays.at(-1)}`;
}

/**
 * A batch waiting for its cost, as a short name for its card and a sentence beneath: "Lavender 'Hidcote', batch
 * B000042" and "It was sown on 3 March and is in tray C000101."
 */
export function missingCostWords(
  batch: Missing,
  today = new Date(),
): { name: string; detail: string } {
  const name = `${plantName(batch.commonName, batch.variety)}, batch ${batch.traceabilityCode}`;
  const where =
    batch.trays.length === 0
      ? "isn't in a tray now"
      : `is in ${batch.trays.length === 1 ? "tray" : "trays"} ${trayList(batch.trays)}`;

  if (!batch.startedAt) {
    return {
      name,
      detail:
        batch.trays.length === 0
          ? "It isn't in a tray now."
          : `It's in ${batch.trays.length === 1 ? "tray" : "trays"} ${trayList(batch.trays)}.`,
    };
  }

  // Cuttings taken here are never waiting for a cost, so only seed and bought-in plants are worded.
  const started = batch.source === "BoughtIn" ? "It arrived" : "It was sown";
  return {
    name,
    detail: `${started} on ${costDay(batch.startedAt, today)} and ${where}.`,
  };
}

type Pots = Schemas["PotsCost"];

/** What's said beneath "Pots and compost": each size potted into, and anything not counted. */
export function potsSentences(pots: Pots): string[] {
  const one = pots.lines.length === 1;
  const lines = pots.lines.map((line) => {
    const plants = Number(line.plants);
    const withVat = money(Number(line.each.withVat));
    const before = aboutMoney(Number(line.each.beforeVat));
    const each = plants === 1 ? "" : " each";

    // One line is the whole row; with several, each says only its own part.
    if (one) {
      return `That's ${plants === 1 ? "1 plant" : `${plants} plants`} potted into ${line.size} at ${withVat}${each} with VAT, which is ${before} before VAT.`;
    }

    return plants === 1
      ? `1 plant was potted into ${line.size} at ${withVat} with VAT, which is ${before} before VAT.`
      : `${plants} plants were potted into ${line.size} at ${withVat} each with VAT, which is ${before} before VAT.`;
  });
  const missing = pots.withoutAllowance;

  if (missing.length > 0) {
    lines.push(
      missing.length === 1
        ? `Pots and compost for ${missing[0]} aren't counted, because no cost of potting is set for that size.`
        : `Pots and compost for ${trayList(missing)} aren't counted, because no cost of potting is set for those sizes.`,
    );
  }

  const unsized = Number(pots.unsizedPlants);

  if (unsized > 0) {
    lines.push(
      unsized === 1
        ? "1 plant was potted with no pot size recorded, so its pot isn't counted."
        : `${unsized} plants were potted with no pot size recorded, so their pots aren't counted.`,
    );
  }

  return lines;
}

/**
 * Both figures in words, the one that counts first, always "about" as they're shares: "about £0.80 with VAT, or
 * about £0.67 before VAT", or "about £0.07, the same with VAT and before VAT" when they're equal.
 */
export function bothFigures(
  cost: MoneyPair,
  counts: Costing["counts"],
): string {
  const withVat = Number(cost.withVat);
  const before = Number(cost.beforeVat);

  if (money(withVat) === money(before)) {
    return `about ${money(withVat)}, the same with VAT and before VAT`;
  }

  const withWords = `about ${money(withVat)} with VAT`;
  const beforeWords = `about ${money(before)} before VAT`;
  return counts === "BeforeVat"
    ? `${beforeWords}, or ${withWords}`
    : `${withWords}, or ${beforeWords}`;
}

/** A whole cost in words, exactly, the one that counts first: "£14.39 with VAT, or £11.99 before VAT". */
export function wholeCost(cost: MoneyPair, counts: Costing["counts"]): string {
  const withVat = money(Number(cost.withVat));
  const before = money(Number(cost.beforeVat));

  if (withVat === before) {
    return `${withVat}, the same with VAT and before VAT`;
  }

  return counts === "BeforeVat"
    ? `${before} before VAT, or ${withVat} with VAT`
    : `${withVat} with VAT, or ${before} before VAT`;
}

/** What each plant has cost, what the lost ones had cost, and what stock counts found. */
export function perPlantSentences(costing: Costing): string[] {
  const plant = costing.perPlant;
  const alive = Number(plant.alive);
  const lost = Number(plant.lost);
  const adjusted = Number(plant.adjusted);
  const total = costing.total;

  if (plant.notCounted) {
    return [
      "These plants haven't been counted yet, so the cost can't be shared out between them.",
    ];
  }

  if (!total) {
    return [];
  }

  if (Number(total.withVat) === 0 && Number(total.beforeVat) === 0) {
    return [
      "Nothing has been spent on this batch, so its plants have cost nothing.",
    ];
  }

  const sentences: string[] = [];
  const already = " That's already counted in what each plant has cost.";

  if (plant.allLost) {
    sentences.push(
      `Every plant in this batch was lost, so the whole cost was lost with them: ${wholeCost(total, costing.counts)}.`,
    );
  } else {
    if (alive <= 0) {
      sentences.push(
        "None of this batch's plants are left, so there are none to share the cost between.",
      );
    } else if (plant.each) {
      sentences.push(
        alive === 1
          ? `1 plant is still growing or has been sold, so it has cost ${bothFigures(plant.each, costing.counts)}.`
          : `${alive} plants are still growing or have been sold, so each one has cost ${bothFigures(plant.each, costing.counts)}.`,
      );
    }

    if (lost > 0 && plant.lostValue) {
      // Said as part of the cost already shared out, unless there are no plants left to share it.
      const tail = alive > 0 ? already : "";
      sentences.push(
        lost === 1
          ? `1 was lost. It had cost ${bothFigures(plant.lostValue, costing.counts)}.${tail}`
          : `${lost} were lost. Between them they had cost ${bothFigures(plant.lostValue, costing.counts)}.${tail}`,
      );
    }
  }

  if (adjusted < 0) {
    sentences.push(
      adjusted === -1
        ? "1 fewer was found at a stock count. It isn't counted as lost."
        : `${-adjusted} fewer were found at a stock count. They aren't counted as lost.`,
    );
  } else if (adjusted > 0) {
    sentences.push(
      adjusted === 1
        ? "1 more was found at a stock count than had been recorded, so it shares the cost too."
        : `${adjusted} more were found at a stock count than had been recorded, so they share the cost too.`,
    );
  }

  return sentences;
}

/** The figure that counts, in words: "£21.00", or "£17.50 before VAT" once registered. */
function counted(cost: MoneyPair, counts: Costing["counts"]): string {
  return counts === "BeforeVat"
    ? `${money(Number(cost.beforeVat))} before VAT`
    : money(Number(cost.withVat));
}

/** What the batch has taken, on invoices and from trays. */
export function takingsSentences(costing: Costing): string[] {
  const takings = costing.takings;
  const invoiced = Number(takings.invoiced);
  const fromTrays = Number(takings.fromTrays);
  const noPrice = Number(takings.noPrice);
  const sentences: string[] = [];

  if (invoiced > 0) {
    sentences.push(
      invoiced === 1
        ? `1 was sold on an invoice for ${counted(takings.invoiceTakings, costing.counts)}.`
        : `${invoiced} were sold on invoices for ${counted(takings.invoiceTakings, costing.counts)}.`,
    );
  }

  if (fromTrays > 0) {
    sentences.push(
      `${fromTrays === 1 ? "1 was" : `${fromTrays} were`} sold from the tray for ${counted(takings.trayTakings, costing.counts)}.`,
    );
  }

  if (noPrice > 0) {
    const more = invoiced + fromTrays > 0 ? " more" : "";
    sentences.push(
      noPrice === 1
        ? `1${more} was sold from the tray with no price recorded, so it isn't in the takings.`
        : `${noPrice}${more} were sold from the tray with no price recorded, so they aren't in the takings.`,
    );
  }

  return sentences.length > 0
    ? sentences
    : ["Nothing from this batch has been sold yet."];
}

/**
 * What the batch has made so far, on the figures that count: takings less costs with VAT while the nursery isn't
 * registered, both before VAT once it is. Only when the cost is known. The difference is worked out from the two
 * figures as shown, so the sentence always adds up.
 */
export function soFarSentence(costing: Costing): string {
  if (!costing.total) {
    return "";
  }

  const before = costing.counts === "BeforeVat";
  const pennies = (pair: MoneyPair) =>
    Math.round(Number(before ? pair.beforeVat : pair.withVat) * 100);
  const taken = pennies(costing.takings.total);
  const cost = pennies(costing.total);
  const made = taken - cost;
  const pounds = (value: number) => money(value / 100);
  const plant = costing.perPlant;
  const left = plant.notCounted
    ? null
    : Math.max(Number(plant.alive) - Number(costing.takings.sold ?? 0), 0);
  const still =
    left === null || left === 0
      ? ""
      : left === 1
        ? ", with 1 plant still to sell"
        : `, with ${left} plants still to sell`;

  // Nothing spent and nothing taken has nothing to say.
  if (taken === 0 && cost === 0) {
    return "";
  }

  if (left === 0) {
    const how =
      made > 0
        ? `so it made ${pounds(made)}`
        : made < 0
          ? `so it lost ${pounds(-made)}`
          : "so it broke even";
    return cost === 0
      ? `This batch has finished. It took ${pounds(taken)} and cost nothing, ${how}.`
      : `This batch has finished. It took ${pounds(taken)} and cost ${pounds(cost)}${before ? ", both before VAT," : " with VAT,"} ${how}.`;
  }

  if (taken === 0) {
    return `It has cost ${pounds(cost)}${before ? " before VAT" : " with VAT"} so far${still}.`;
  }

  if (cost === 0) {
    return `It has taken ${pounds(taken)} and cost nothing, so it has made ${pounds(made)} so far${still}.`;
  }

  return made >= 0
    ? `It has taken ${pounds(taken)} and cost ${pounds(cost)}${before ? ", both before VAT," : " with VAT,"} so it has made ${pounds(made)} so far${still}.`
    : `So far it has cost ${pounds(cost)}${before ? " and taken " + pounds(taken) + ", both before VAT" : " with VAT and taken " + pounds(taken)}${still}.`;
}
