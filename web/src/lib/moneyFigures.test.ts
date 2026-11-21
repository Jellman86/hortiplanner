import { describe, expect, it } from "vitest";
import {
  comparedWords,
  costCell,
  costOrder,
  figureCell,
  notKnownSentence,
  tillSentence,
  tiles,
  unansweredSentence,
  unbatchedNote,
  unbatchedSentence,
  type MoneyFigures,
  type PlantMoney,
} from "./moneyFigures";

const pair = (withVat: number, beforeVat: number) => ({ withVat, beforeVat });

const plant = (overrides: Partial<PlantMoney> = {}): PlantMoney => ({
  plantId: "p",
  commonName: "Lavender",
  variety: "Hidcote",
  batches: 1,
  sold: 6,
  takings: pair(28, 28),
  unbatched: null,
  cost: pair(3, 2.5),
  complete: true,
  profit: pair(25, 25.5),
  each: pair(0.15, 0.125),
  lostValue: null,
  ...overrides,
});

const figures = (overrides: Partial<MoneyFigures> = {}): MoneyFigures => ({
  counts: "WithVat",
  vatRegistered: false,
  totals: {
    taken: pair(1024.5, 1024.5),
    spent: pair(312.4, 260.33),
    made: pair(712.1, 764.17),
    lost: pair(38.2, 31.83),
    complete: true,
  },
  unanswered: 0,
  notKnown: 0,
  plants: [plant()],
  byYear: [],
  bySupplier: [],
  till: { taken: 2410, fromTrays: pair(1630, 1630) },
  ...overrides,
});

describe("money figures", () => {
  it("puts the figure that counts first and compares the other", () => {
    expect(costOrder("WithVat")).toEqual(["withVat", "beforeVat"]);
    expect(costOrder("BeforeVat")).toEqual(["beforeVat", "withVat"]);
    expect(comparedWords(pair(312.4, 260.33), "WithVat")).toBe(
      "£260.33 before VAT",
    );
    expect(comparedWords(pair(312.4, 260.33), "BeforeVat")).toBe(
      "£312.40 with VAT",
    );
    expect(comparedWords(pair(5, 5), "WithVat")).toBe("");
  });

  it("says a missing figure in words, and so far while a batch has no answer", () => {
    expect(costCell(null, true, "withVat")).toBe("Not known yet");
    expect(costCell(pair(40, 33.33), false, "withVat")).toBe("£40.00 so far");
    expect(costCell(pair(40, 33.33), true, "beforeVat")).toBe("£33.33");
    expect(figureCell(null, "WithVat")).toBe("Not known yet");
    expect(figureCell(pair(25, 25.5), "BeforeVat")).toBe("£25.50");
  });

  it("gives the tiles, with both figures for what was spent and lost", () => {
    expect(tiles(figures())).toEqual([
      {
        key: "taken",
        amount: "£1,024.50",
        words: "taken from plants",
        compare: "",
      },
      {
        key: "spent",
        amount: "£312.40",
        words: "spent on what went into them",
        compare: "£260.33 before VAT",
      },
      { key: "made", amount: "£712.10", words: "made", compare: "" },
      {
        key: "lost",
        amount: "£38.20",
        words: "went on plants that were lost",
        compare: "£31.83 before VAT",
      },
    ]);

    const incomplete = figures({
      totals: {
        ...figures().totals,
        made: pair(-5.39, -5.39),
        complete: false,
      },
    });
    expect(tiles(incomplete)[1].words).toBe(
      "spent so far on what went into them",
    );
    expect(tiles(incomplete)[2]).toMatchObject({
      amount: "£5.39",
      words: "lost so far overall",
    });
  });

  it("counts batches with no answer and batches not known", () => {
    expect(unansweredSentence(12)).toBe(
      "12 batches have no cost yet, so the figures for their plants are incomplete.",
    );
    expect(unansweredSentence(1)).toBe(
      "1 batch has no cost yet, so the figures for its plant are incomplete.",
    );
    expect(notKnownSentence(3)).toBe(
      "3 batches are left out because nobody knows what they cost.",
    );
    expect(notKnownSentence(1)).toBe(
      "1 batch is left out because nobody knows what it cost.",
    );
  });

  it("sets the till beside sales from trays without saying one is inside the other", () => {
    const till = figures().till;
    expect(tillSentence(till, "ThisYear", "WithVat")).toBe(
      "The till took £2,410.00 this year. Sales recorded from trays came to £1,630.00 in the same time.",
    );
    expect(tillSentence(till, "LastYear", "WithVat")).toBe(
      "The till took £2,410.00 last year. Sales recorded from trays came to £1,630.00 in the same time.",
    );
    expect(tillSentence(till, "All", "WithVat")).toBe(
      "The till has taken £2,410.00 altogether. Sales recorded from trays came to £1,630.00 in the same time.",
    );
  });

  it("names takings sold without saying which batch", () => {
    const sold = plant({ unbatched: pair(84, 84) });
    expect(unbatchedSentence(sold, "WithVat")).toBe(
      "£84.00 of this was sold without saying which batch, so it's in the plant's takings but not in any batch's.",
    );
    expect(unbatchedNote(sold, "WithVat")).toBe(
      "Lavender 'Hidcote': £84.00 of this was sold without saying which batch, so it's in the plant's takings but not in any batch's.",
    );
    expect(unbatchedSentence(plant(), "WithVat")).toBe("");
  });
});
