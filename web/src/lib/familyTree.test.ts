import { describe, expect, it } from "vitest";
import {
  aboveSpecies,
  broaderRanks,
  listWords,
  rankLabel,
  relatives,
} from "./familyTree";

const plant = (id: string) => ({ plantId: id, name: id });
const rung = (rank: string, otherPlants: string[] = []) => ({
  taxonId: rank,
  rank,
  scientificName: rank,
  commonName: null,
  otherPlants: otherPlants.map(plant),
});

describe("family tree", () => {
  it("names each rank", () => {
    expect(rankLabel("family")).toBe("Family");
    expect(rankLabel("subfamily")).toBe("Subfamily");
  });

  it("folds away only the broad groups", () => {
    expect(broaderRanks.has("class")).toBe(true);
    expect(broaderRanks.has("order")).toBe(false);
    expect(broaderRanks.has("subfamily")).toBe(false);
  });

  it("knows a variety is no further up than a species", () => {
    expect(aboveSpecies.has("genus")).toBe(true);
    expect(aboveSpecies.has("species")).toBe(false);
    expect(aboveSpecies.has("variety")).toBe(false);
  });

  it("lists each relative once, in the closest group it shares", () => {
    const found = relatives({
      linked: true,
      unavailable: false,
      lineage: [
        rung("kingdom"),
        rung("order", ["twin", "cousin", "aunt", "distant"]),
        rung("family", ["twin", "cousin", "aunt"]),
        rung("genus", ["twin", "cousin"]),
        rung("species", ["twin"]),
      ],
      sameGenus: [plant("twin"), plant("cousin")],
      sameFamily: [plant("aunt")],
    });

    expect(found.species.map((each) => each.plantId)).toEqual(["twin"]);
    expect(found.genus.map((each) => each.plantId)).toEqual(["cousin"]);
    expect(found.family.map((each) => each.plantId)).toEqual(["aunt"]);
    expect(found.order.map((each) => each.plantId)).toEqual(["distant"]);
  });

  it("lists names as a sentence would", () => {
    expect(listWords(["Rosemary"])).toBe("Rosemary");
    expect(listWords(["Rosemary", "Sage"])).toBe("Rosemary and Sage");
    expect(listWords(["Rosemary", "Sage", "Catmint"])).toBe(
      "Rosemary, Sage and Catmint",
    );
  });
});
