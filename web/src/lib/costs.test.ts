import { describe, expect, it } from "vitest";
import {
  costDay,
  entrySentence,
  firstOf,
  halfOf,
  missingCostWords,
  potsSentences,
  materialSentence,
  readMoney,
  replacedParts,
  replacedSentence,
  rowName,
  supersededSentence,
  vatSentence,
  type CostEntry,
  type Costing,
} from "./costs";

const entry = (overrides: Partial<CostEntry> = {}): CostEntry => ({
  id: "a",
  kind: "Material",
  answer: "Amount",
  amountPaid: 2.99,
  includesVat: true,
  vatBand: "Standard",
  vatRate: 20,
  vatSaid: false,
  cost: { withVat: 2.99, beforeVat: 2.49 },
  note: null,
  addedBy: "Alex Moss",
  occurredAt: "2026-03-03T10:00:00Z",
  addedAt: "2026-03-03T10:00:00Z",
  replaced: false,
  replacesId: null,
  ...overrides,
});

const costing = (overrides: Partial<Costing> = {}): Costing => ({
  batchId: "b",
  traceabilityCode: "B000001",
  source: "SeedLot",
  plantVatBand: "Standard",
  vatRegistered: false,
  counts: "WithVat",
  material: {
    state: "NoAnswer",
    cost: null,
    entry: null,
    purchase: null,
    superseded: null,
  },
  others: [],
  pots: null,
  total: null,
  history: [],
  standardRate: 20,
  reducedRate: 5,
  ...overrides,
});

const march = new Date("2026-06-01T12:00:00Z");

describe("reading a typed cost", () => {
  it("takes pounds and pence, with or without the pound sign", () => {
    expect(readMoney("2.99")).toBe(2.99);
    expect(readMoney(" £2.5 ")).toBe(2.5);
    expect(readMoney("£ 3")).toBe(3);
    expect(readMoney("0")).toBe(0);
  });

  it("refuses anything else", () => {
    expect(readMoney("")).toBeNull();
    expect(readMoney("2.999")).toBeNull();
    expect(readMoney("two pounds")).toBeNull();
    expect(readMoney("-1")).toBeNull();
    expect(readMoney("1,000")).toBeNull();
  });
});

describe("the words for a cost", () => {
  it("names the day, with the year only when it isn't this one", () => {
    expect(costDay("2026-03-03T10:00:00Z", march)).toBe("3 March");
    expect(costDay("2025-03-03T10:00:00Z", march)).toBe("3 March 2025");
  });

  it("says when the VAT was assumed, and when it was said", () => {
    expect(vatSentence(entry(), "seed")).toBe(
      "Nobody has said how much of it was VAT, so it's taken as including VAT at 20%.",
    );
    expect(vatSentence(entry({ vatRate: 0, vatBand: "Zero" }), "seed")).toBe(
      "This plant has no VAT on it, so the seed is taken as having none either.",
    );
    expect(vatSentence(entry({ vatSaid: true }), "seed")).toBe(
      "You said it included VAT at 20%.",
    );
    expect(
      vatSentence(
        entry({ vatSaid: true, includesVat: false, amountPaid: 2.49 }),
        "seed",
      ),
    ).toBe(
      "You said £2.49 was the price before VAT, and VAT at 20% was added.",
    );
    expect(vatSentence(entry({ vatSaid: true, vatRate: 0 }), "seed")).toBe(
      "You said there was no VAT on it, so both figures are the same.",
    );
  });

  it("names who first added a cost, however often its VAT was changed since", () => {
    const first = entry({ id: "1", replaced: true });
    const second = entry({
      id: "2",
      replacesId: "1",
      vatSaid: true,
      addedBy: "Sam Fielding",
      addedAt: "2026-03-05T10:00:00Z",
    });

    expect(firstOf(second, [first, second])).toBe(first);
    expect(entrySentence(second, [first, second], "seed")).toMatch(
      /^Alex Moss added the cost of the seed on 3 March\. You said it included VAT at 20%\.$/,
    );
  });

  it("names the row by what the batch started from", () => {
    expect(rowName(null, "SeedLot")).toBe("Seed");
    expect(rowName(null, "BoughtIn")).toBe("Plants");
    expect(
      rowName(entry({ kind: "Other", note: "slug pellets" }), "SeedLot"),
    ).toBe("Something else: slug pellets");
  });

  it("says where the seed or plants' cost comes from when nobody typed one", () => {
    const shared = (equally: boolean) =>
      materialSentence(
        costing({
          material: {
            ...costing().material,
            state: "Purchase",
            purchase: {
              purchaseId: "p",
              supplier: "Example Seeds",
              purchasedOn: "2026-03-03",
              shared: true,
              equally,
              share: { withVat: 1, beforeVat: 1 },
            },
          },
        }),
      );
    expect(shared(false)).toMatch(
      /^The seed is from the purchase from Example Seeds on 3 March( 2026)?, which gives the cost and its VAT\. Other batches were started from the same purchase, so this is this batch's share of the cost, by how many plants each one started with\.$/,
    );
    expect(shared(true)).toMatch(
      /so the cost is shared equally between them, because one was sown without a count\.$/,
    );
    expect(materialSentence(costing())).toBe(
      "Nobody has said what the seed for this batch cost, so we can't work out what the batch has cost.",
    );
    expect(
      materialSentence(
        costing({
          source: "Cuttings",
          material: { ...costing().material, state: "Cuttings" },
        }),
      ),
    ).toBe("These were cuttings taken here, so they cost nothing to buy.");
    expect(
      materialSentence(
        costing({
          source: "BoughtIn",
          material: {
            ...costing().material,
            state: "Purchase",
            purchase: {
              purchaseId: "p",
              supplier: "Example Plugs",
              purchasedOn: "2026-03-03",
              shared: false,
              equally: false,
              share: { withVat: 18, beforeVat: 15 },
            },
          },
        }),
      ),
    ).toMatch(
      /^The plants are from the purchase from Example Plugs on 3 March( 2026)?, which gives the cost and its VAT\.$/,
    );
  });

  it("keeps earlier answers in words, saying what VAT was in them", () => {
    expect(replacedSentence(entry({ amountPaid: 2.5 }))).toMatch(
      /^Replaced: £2\.50 including VAT at 20%, added by Alex Moss on 3 March( 2026)?\.$/,
    );
    expect(
      replacedSentence(entry({ includesVat: false, vatSaid: true })),
    ).toMatch(/^Replaced: £2\.99 before VAT at 20%, added by/);
    expect(replacedSentence(entry({ vatRate: 0 }))).toMatch(
      /^Replaced: £2\.99 with no VAT, added by/,
    );
    const slugs = entry({
      id: "s",
      kind: "Other",
      amountPaid: 3,
      note: "slug pellets",
    });
    expect(
      replacedParts(slugs, [
        slugs,
        entry({ id: "r", answer: "Removed", replacesId: "s" }),
      ]),
    ).toEqual({
      lead: "Removed: ",
      struck: "£3.00 including VAT at 20%",
      rest: expect.stringMatching(
        /^ for slug pellets, added by Alex Moss on 3 March( 2026)?\.$/,
      ),
    });
    expect(replacedSentence(entry({ answer: "Free" }))).toMatch(
      /^Replaced: Alex Moss said on 3 March( 2026)? that it cost nothing\.$/,
    );
    expect(replacedSentence(entry({ answer: "NotKnown" }))).toMatch(
      /^Replaced: Alex Moss said on 3 March( 2026)? that nobody knows what it cost\.$/,
    );
    expect(supersededSentence(entry())).toMatch(
      /^The purchase now gives this batch's cost, so the £2\.99 typed in on 3 March( 2026)? is no longer counted\.$/,
    );
  });
});

describe("half a packet's price", () => {
  it("is worked out in pence, rounding a half penny up", () => {
    expect(halfOf(2.99)).toBe(1.5);
    expect(halfOf(1.15)).toBe(0.58);
    expect(halfOf(2.01)).toBe(1.01);
    expect(halfOf(2.55)).toBe(1.28);
    expect(halfOf(0.01)).toBe(0.01);
    expect(halfOf(4)).toBe(2);
  });
});

describe("a batch waiting for its cost", () => {
  const batch = (
    trays: string[],
    startedAt: string | null = "2026-03-03T10:00:00Z",
    source = "SeedLot",
  ) => ({
    batchId: "b",
    traceabilityCode: "B000042",
    commonName: "Lavender",
    variety: "Hidcote",
    source: source as "SeedLot",
    startedAt,
    trays,
  });

  it("has a short name, and says when it started and where it is", () => {
    expect(missingCostWords(batch(["C000101"]), march)).toEqual({
      name: "Lavender 'Hidcote', batch B000042",
      detail: "It was sown on 3 March and is in tray C000101.",
    });
    expect(
      missingCostWords(batch(["C000101", "C000102", "C000103"]), march).detail,
    ).toBe(
      "It was sown on 3 March and is in trays C000101, C000102 and C000103.",
    );
    expect(
      missingCostWords(batch([], "2026-03-03T10:00:00Z", "BoughtIn"), march)
        .detail,
    ).toBe("It arrived on 3 March and isn't in a tray now.");
  });

  it("leaves out the day when there isn't one", () => {
    expect(missingCostWords(batch(["C000101"], null), march).detail).toBe(
      "It's in tray C000101.",
    );
    expect(missingCostWords(batch([], null), march).detail).toBe(
      "It isn't in a tray now.",
    );
  });
});

describe("what potting cost", () => {
  it("says how many plants went into each size, and what isn't counted", () => {
    expect(
      potsSentences({
        cost: { withVat: 8.4, beforeVat: 7 },
        lines: [
          {
            size: "9cm",
            plants: 20,
            each: { withVat: 0.42, beforeVat: 0.35 },
            cost: { withVat: 8.4, beforeVat: 7 },
          },
          {
            size: "1L",
            plants: 1,
            each: { withVat: 0.85, beforeVat: 0.7083 },
            cost: { withVat: 0.85, beforeVat: 0.7083 },
          },
        ],
        withoutAllowance: ["2L", "3L"],
        unsizedPlants: 4,
      }),
    ).toEqual([
      "That's 20 plants potted into 9cm at £0.42 each with VAT, which is £0.35 before VAT.",
      "That's 1 plant potted into 1L at £0.85 with VAT, which is £0.71 before VAT.",
      "Pots and compost for 2L and 3L aren't counted, because those pot sizes have no cost of potting yet.",
      "4 plants were potted with no pot size recorded, so their pots aren't counted.",
    ]);
  });
});
