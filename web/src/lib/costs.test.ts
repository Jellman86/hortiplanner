import { describe, expect, it } from "vitest";
import {
  costDay,
  entrySentence,
  firstOf,
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
