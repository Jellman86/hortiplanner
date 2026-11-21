import { beforeEach, describe, expect, it, vi } from "vitest";

// Every part of Hortiplanner on, unless a test turns one off.
const off = new Set<string>();
vi.mock("./features.svelte", () => ({
  anyOn: (...features: string[]) => features.some((item) => !off.has(item)),
}));

const { firstPage, hubOf, hubs, listedPages, visibleHubs } =
  await import("./sections");

const names = (isOwner: boolean) =>
  visibleHubs(isOwner).map((hub) => hub.label);

describe("places in the sidebar", () => {
  beforeEach(() => off.clear());

  it("gives the owner six places after Record work, and staff two", () => {
    expect(names(true)).toEqual([
      "Production",
      "Plants",
      "Places",
      "Selling",
      "Buying",
      "Money and figures",
    ]);
    expect(names(false)).toEqual(["Production", "Plants"]);
  });

  it("gives staff no owner-only pages, so no rows", () => {
    for (const hub of hubs) {
      expect(listedPages(hub, false).every((item) => !item.ownerOnly)).toBe(
        true,
      );
    }
    expect(
      listedPages(hubOf("production"), false).map((item) => item.page),
    ).toEqual(["production"]);
  });

  it("hides a place with no page left on, and opens one on its first page that's on", () => {
    off.add("Invoices");
    expect(firstPage(hubOf("invoices"), true)).toBe("customers");
    off.add("Passports");
    expect(names(true)).not.toContain("Selling");
    off.add("Purchases");
    expect(
      listedPages(hubOf("suppliers"), true).map((item) => item.page),
    ).toEqual(["suppliers"]);
  });

  it("lists no page that's reached from elsewhere", () => {
    const listed = hubs.flatMap((hub) =>
      listedPages(hub, true).map((item) => item.page),
    );
    expect(listed).not.toContain("costs");
    expect(listed).not.toContain("jobs");
    expect(hubOf("costs").hub).toBe("money");
    expect(hubOf("jobs").hub).toBe("home");
  });
});
