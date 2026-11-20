import { describe, expect, it } from "vitest";
import { rateHistory } from "./vatRates";

const start = [
  { band: "Standard", percent: 20, from: "2000-01-01", setBy: null },
  { band: "Reduced", percent: 5, from: "2000-01-01", setBy: null },
];

describe("earlier VAT rates", () => {
  it("says what a rate was until the day before it changed", () => {
    expect(
      rateHistory([
        ...start,
        { band: "Standard", percent: 17.5, from: "2027-04-01", setBy: "Sam" },
      ]),
    ).toEqual([
      "The standard rate was 20% until 31 March 2027. Sam changed it to 17.5% on 1 April 2027.",
    ]);
  });

  it("says a second change on one day was from the first, not that it was the rate the day before", () => {
    expect(
      rateHistory([
        ...start,
        { band: "Standard", percent: 71.5, from: "2027-04-01", setBy: "Sam" },
        { band: "Standard", percent: 17.5, from: "2027-04-01", setBy: "Sam" },
      ]),
    ).toEqual([
      "Sam changed the standard rate again on 1 April 2027, from 71.5% to 17.5%.",
      "The standard rate was 20% until 31 March 2027. Sam changed it to 71.5% on 1 April 2027.",
    ]);
  });

  it("keeps every sentence when a rate goes there and back on one day", () => {
    const history = rateHistory([
      ...start,
      { band: "Standard", percent: 17.5, from: "2027-04-01", setBy: "Sam" },
      { band: "Standard", percent: 20, from: "2027-04-01", setBy: "Sam" },
      { band: "Standard", percent: 17.5, from: "2027-04-01", setBy: "Sam" },
    ]);
    expect(history).toHaveLength(3);
  });

  it("leaves out the starting rates", () => {
    expect(rateHistory(start)).toEqual([]);
  });
});
