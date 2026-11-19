import { describe, expect, it } from "vitest";
import {
  aboveSpecies,
  broaderRanks,
  grow,
  leafCount,
  listWords,
  rankLabel,
  relativeCount,
} from "./familyTree";

const rung = (rank: string, name = rank) => ({
  taxonId: name,
  rank,
  scientificName: name,
  commonName: null,
});
const taxon = (name: string) => ({
  taxonId: name,
  scientificName: name,
  commonName: null,
});
const relative = (
  name: string,
  family: string | null,
  genus: string | null,
  species: string | null,
) => ({
  plantId: name,
  name,
  hasPhoto: false,
  family: family ? taxon(family) : null,
  genus: genus ? taxon(genus) : null,
  species: species ? taxon(species) : null,
});
const self = { plantId: "self", name: "Lavender 'Hidcote'", hasPhoto: true };

describe("family tree", () => {
  it("names each rank", () => {
    expect(rankLabel("family")).toBe("Family");
    expect(rankLabel("subfamily")).toBe("Subfamily");
  });

  it("folds away only the broad groups", () => {
    expect(broaderRanks.has("class")).toBe(true);
    expect(broaderRanks.has("order")).toBe(false);
  });

  it("knows a variety is no further up than a species", () => {
    expect(aboveSpecies.has("genus")).toBe(true);
    expect(aboveSpecies.has("variety")).toBe(false);
  });

  it("grows from the order, with this plant's branch first and each relative on its own", () => {
    const { above, root } = grow(
      {
        linked: true,
        unavailable: false,
        lineage: [
          rung("kingdom"),
          rung("class"),
          rung("order", "Lamiales"),
          rung("family", "Lamiaceae"),
          rung("genus", "Lavandula"),
          rung("species", "Lavandula angustifolia"),
        ],
        relatives: [
          relative("Foxglove", "Plantaginaceae", "Digitalis", "D. purpurea"),
          relative(
            "Munstead",
            "Lamiaceae",
            "Lavandula",
            "Lavandula angustifolia",
          ),
          relative("Grosso", "Lamiaceae", "Lavandula", "L. x intermedia"),
          relative("Sage", "Lamiaceae", "Salvia", null),
          relative("Odd one", null, null, null),
        ],
      },
      self,
    );

    expect(above.map((each) => each.rank)).toEqual(["kingdom", "class"]);
    expect(root?.scientificName).toBe("Lamiales");
    expect(root?.leaves.map((leaf) => leaf.name)).toEqual(["Odd one"]);

    const [mint, figwort] = root!.branches;
    expect(mint.scientificName).toBe("Lamiaceae");
    expect(mint.onPath).toBe(true);
    expect(figwort.onPath).toBe(false);

    const [lavenders, sages] = mint.branches;
    expect(lavenders.branches.map((each) => each.scientificName)).toEqual([
      "Lavandula angustifolia",
      "L. x intermedia",
    ]);
    expect(lavenders.branches[0].leaves.map((leaf) => leaf.name)).toEqual([
      "Lavender 'Hidcote'",
      "Munstead",
    ]);
    expect(sages.leaves.map((leaf) => leaf.name)).toEqual(["Sage"]);

    expect(leafCount(root!)).toBe(6);
    expect(relativeCount(root!)).toBe(5);
    expect(relativeCount(figwort)).toBe(1);
  });

  it("grows from the family when there is no order, and stands alone without either", () => {
    const fromFamily = grow(
      {
        linked: true,
        unavailable: false,
        lineage: [rung("family", "Lamiaceae"), rung("genus", "Lavandula")],
        relatives: [relative("Sage", "Lamiaceae", "Salvia", null)],
      },
      self,
    );
    expect(
      fromFamily.root?.branches.map((each) => each.scientificName),
    ).toEqual(["Lavandula", "Salvia"]);
    expect(fromFamily.root?.branches[0].leaves[0].self).toBe(true);

    const alone = grow(
      {
        linked: true,
        unavailable: false,
        lineage: [rung("kingdom")],
        relatives: [],
      },
      self,
    );
    expect(alone.root).toBeNull();
    expect(alone.above).toHaveLength(1);
  });

  it("lists names as a sentence would", () => {
    expect(listWords(["Rosemary", "Sage", "Catmint"])).toBe(
      "Rosemary, Sage and Catmint",
    );
  });
});
