import { describe, expect, it } from "vitest";
import {
  lineAmount,
  seedNote,
  seedOnly,
  sowingWords,
  startsCounted,
} from "./sowing";

describe("sowing", () => {
  it("puts each measure in words", () => {
    expect(sowingWords({ sownAs: "About", sownAmount: 200 })).toBe(
      "about 200 seeds",
    );
    expect(sowingWords({ sownAs: "Grams", sownAmount: "0.5" })).toBe("0.5 g");
    expect(
      sowingWords({
        sownAs: "Packet",
        packetShare: "Half",
        packetSays: "approx. 250 seeds",
      }),
    ).toBe("half a packet (approx. 250 seeds)");
    expect(sowingWords({ sownAs: "Counted" })).toBe("");
  });

  it("only a count starts a tray with plants", () => {
    expect(startsCounted("Counted")).toBe(true);
    expect(startsCounted(null)).toBe(true);
    expect(startsCounted("Grams")).toBe(false);
  });

  it("shows the seed that went into a tray nobody has counted", () => {
    expect(
      lineAmount({
        quantity: 0,
        notCounted: true,
        sown: { sownAs: "About", sownAmount: 200 },
      }),
    ).toBe("About 200 seeds of");
    expect(
      lineAmount({
        quantity: 0,
        notCounted: true,
        sown: { sownAs: "Grams", sownAmount: "0.5" },
      }),
    ).toBe("0.5 g of");
    expect(
      lineAmount({
        quantity: 0,
        notCounted: true,
        sown: { sownAs: "Packet", packetShare: "Half" },
      }),
    ).toBe("Half a packet of");
    expect(lineAmount({ quantity: 0, notCounted: true })).toBe("Some");
    expect(seedOnly({ sownAs: "About", sownAmount: 200 })).toBe(
      "About 200 seeds",
    );
    expect(seedOnly(null)).toBe("Not known");
    expect(seedOnly({ sownAs: "Unknown" })).toBe("Not known");
    expect(
      seedNote({
        sownAs: "Packet",
        packetShare: "Whole",
        packetSays: "RHS 250 Seeds",
      }),
    ).toBe("a whole packet (RHS 250 Seeds)");
    expect(seedNote({ sownAs: "Unknown" })).toBe("amount not known");
    expect(lineAmount({ quantity: 40 })).toBe("40");
    expect(lineAmount({ quantity: 200, rough: true })).toBe("About 200");
  });
});
