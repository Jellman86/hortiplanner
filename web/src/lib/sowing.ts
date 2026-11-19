import type { Schemas } from "./types";

export type SownMeasure = Schemas["SownMeasure"];
export type PacketShare = Schemas["PacketShare"];

/** The ways seed can be measured when sown, as staff choose them. */
export const sowingChoices: { measure: SownMeasure; label: string }[] = [
  { measure: "Counted", label: "I counted them" },
  { measure: "About", label: "About…" },
  { measure: "Grams", label: "By weight" },
  { measure: "Packet", label: "A packet" },
  { measure: "Unknown", label: "Don't know" },
];

export const packetShares: { share: PacketShare; label: string }[] = [
  { share: "Whole", label: "Whole packet" },
  { share: "Half", label: "Half" },
  { share: "Part", label: "Part" },
];

/** Whether seed measured this way starts the tray with a number of plants. */
export const startsCounted = (measure: SownMeasure | null | undefined) =>
  !measure || measure === "Counted";

/** How seed was sown, in words: "about 200 seeds", "0.5 g", "a half packet (approx. 250 seeds)". */
export function sowingWords(sowing: {
  sownAs?: SownMeasure | null;
  sownAmount?: number | string | null;
  packetShare?: PacketShare | null;
  packetSays?: string | null;
}): string {
  const amount = Number(sowing.sownAmount ?? 0);

  switch (sowing.sownAs) {
    case "About":
      return `about ${amount} seeds`;
    case "Grams":
      return `${amount} g`;
    case "Packet": {
      const share =
        sowing.packetShare === "Whole"
          ? "a whole packet"
          : sowing.packetShare === "Half"
            ? "half a packet"
            : "part of a packet";
      return sowing.packetSays ? `${share} (${sowing.packetSays})` : share;
    }
    case "Unknown":
      return "not known";
    default:
      return "";
  }
}

type Sowing = Parameters<typeof sowingWords>[0];

const capital = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** The seed that went into a tray nobody has counted since, as it reads before a plant's name: "About 200 seeds of". */
export function seedAmount(sown: Sowing | null | undefined): string {
  switch (sown?.sownAs) {
    case "About":
      return `About ${Number(sown.sownAmount ?? 0)} seeds of`;
    case "Grams":
      return `${Number(sown.sownAmount ?? 0)} g of`;
    case "Packet":
      return sown.packetShare === "Whole"
        ? "A packet of"
        : sown.packetShare === "Half"
          ? "Half a packet of"
          : "Part of a packet of";
    default:
      return "Some";
  }
}

/** Whether a tray's seed was measured at all, so there's an amount to tell people. "Don't know" isn't one. */
export const measured = (sown: Sowing | null | undefined): sown is Sowing =>
  !!sown?.sownAs && sown.sownAs !== "Counted" && sown.sownAs !== "Unknown";

/** The seed that went into a tray nobody has counted since, on its own: "About 200 seeds", or "Not known". */
export const seedOnly = (sown: Sowing | null | undefined) =>
  measured(sown) ? capital(sowingWords(sown)) : "Not known";

/** The same, to go in brackets after a plant's name: "about 200 seeds", or "amount not known". */
export const seedNote = (sown: Sowing | null | undefined) =>
  measured(sown)
    ? sowingWords(sown).replace(/^./, (first) => first.toLowerCase())
    : "amount not known";

/** How many of a batch are in a tray, roughly if only guessed, or the seed that went in if nobody has counted them. */
export const lineAmount = (line: {
  quantity: number | string;
  notCounted?: boolean;
  rough?: boolean;
  sown?: Sowing | null;
}) =>
  line.notCounted
    ? seedAmount(line.sown)
    : line.rough
      ? `About ${line.quantity}`
      : String(line.quantity);
