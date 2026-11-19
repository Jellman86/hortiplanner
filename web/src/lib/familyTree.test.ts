import { describe, expect, it } from "vitest";
import { listWords, rankLabel } from "./familyTree";

describe("family tree", () => {
  it("names each rank", () => {
    expect(rankLabel("family")).toBe("Family");
    expect(rankLabel("subfamily")).toBe("Subfamily");
  });

  it("lists names as a sentence would", () => {
    expect(listWords(["Rosemary"])).toBe("Rosemary");
    expect(listWords(["Rosemary", "Sage"])).toBe("Rosemary and Sage");
    expect(listWords(["Rosemary", "Sage", "Catmint"])).toBe(
      "Rosemary, Sage and Catmint",
    );
  });
});
