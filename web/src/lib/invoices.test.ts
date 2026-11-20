import { catalogueNet, changeRate, plantRate, vatChoices } from "./invoices";
import { describe, expect, it } from "vitest";
import { invoiceNumber, standing, totals, type DraftLine } from "./invoices";

const line = (quantity: number, unitPrice: number, vatRate = 0): DraftLine => ({
  description: "Lavender",
  quantity,
  unitPrice,
  vatRate,
  plantId: null,
  containerId: null,
  batchId: null,
});

describe("invoice totals", () => {
  it("adds VAT for each rate", () => {
    expect(totals([line(10, 3, 20), line(10, 2, 0)])).toEqual({
      net: 50,
      vat: [{ rate: 20, vat: 6 }],
      total: 56,
    });
  });

  it("rounds to the penny", () => {
    expect(totals([line(3, 0.333, 20)]).total).toBe(1.2);
  });
});

describe("invoice standing", () => {
  const base = {
    id: "i",
    number: 1,
    kind: "Invoice",
    customerName: "Hilltop",
    issuedOn: "2026-10-01",
    dueOn: "2026-10-31",
    paidOn: null,
    total: 10,
    credited: false,
    creditsInvoiceId: null,
  } as const;

  it("says when it's overdue", () => {
    expect(standing(base, "2026-11-10")).toBe("Overdue");
    expect(standing(base, "2026-10-15")).toBe("Owed");
    expect(standing({ ...base, paidOn: "2026-10-20" }, "2026-11-10")).toBe(
      "Paid",
    );
    expect(standing({ ...base, credited: true }, "2026-11-10")).toBe(
      "Cancelled",
    );
  });

  it("pads numbers", () => {
    expect(invoiceNumber(7)).toBe("0007");
  });
});

describe("catalogue prices", () => {
  it("removes VAT and rounds net prices to the penny", () => {
    expect(catalogueNet(4.5, 20, true)).toBe(3.75);
    expect(catalogueNet(4.5, 5, true)).toBe(4.29);
    expect(catalogueNet(4.5, 20, false)).toBe(4.5);
  });

  it("recomputes the prefill when the rate changes until someone edits it", () => {
    const line = {
      description: "Lavender",
      quantity: 1,
      unitPrice: 3.75,
      vatRate: 20,
      plantId: null,
      containerId: null,
      batchId: null,
      catalogueGross: 4.5,
    };
    expect(changeRate(line, 5, true).unitPrice).toBe(4.29);
    expect(
      changeRate({ ...line, unitPrice: 3, catalogueGross: null }, 5, true)
        .unitPrice,
    ).toBe(3);
  });
});

describe("VAT choices", () => {
  it("come from the rates set under Settings, with none always offered", () => {
    expect(vatChoices(17.5, 5)).toEqual([
      { rate: 17.5, label: "Standard, 17.5%" },
      { rate: 5, label: "Reduced, 5%" },
      { rate: 0, label: "None, 0%" },
    ]);
  });
});

describe("a plant's line", () => {
  it("starts at the plant's own rate, as set under Settings", () => {
    expect(plantRate("Standard", 17.5, 5, true)).toBe(17.5);
    expect(plantRate("Reduced", 17.5, 5, true)).toBe(5);
    expect(plantRate("Zero", 17.5, 5, true)).toBe(0);
  });

  it("charges nothing while the nursery isn't registered", () => {
    expect(plantRate("Standard", 20, 5, false)).toBe(0);
    expect(plantRate("Reduced", 20, 5, false)).toBe(0);
  });

  it("works a zero-rated plant's price back to the whole catalogue price", () => {
    expect(catalogueNet(6, plantRate("Zero", 20, 5, true), true)).toBe(6);
    expect(catalogueNet(6, plantRate("Standard", 20, 5, true), true)).toBe(5);
  });
});
