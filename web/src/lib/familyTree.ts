import type { Schemas } from "./types";

type Tree = Schemas["FamilyTree"];
type Plant = Schemas["PlantName"];

/** The words for each rank of the plant kingdom's family tree, as shown on a plant's page. */
const ranks: Record<string, string> = {
  kingdom: "Kingdom",
  phylum: "Phylum",
  class: "Class",
  order: "Order",
  family: "Family",
  genus: "Genus",
  species: "Species",
};

export const rankLabel = (rank: string) =>
  ranks[rank] ?? rank.charAt(0).toUpperCase() + rank.slice(1);

/** The broad groups nearly every plant shares, folded away until asked for. */
export const broaderRanks: ReadonlySet<string> = new Set([
  "kingdom",
  "phylum",
  "class",
]);

/** The nursery's other plants, closest first, each listed once: same species, then genus, then family, then order. */
export function relatives(tree: Tree): {
  species: Plant[];
  genus: Plant[];
  family: Plant[];
  order: Plant[];
} {
  const listed = new Set<string>();
  const fresh = (plants: readonly Plant[]) => {
    const found = plants.filter((plant) => !listed.has(plant.plantId));
    for (const plant of found) {
      listed.add(plant.plantId);
    }
    return found;
  };
  const at = (rank: string) =>
    tree.lineage.find((rung) => rung.rank === rank)?.otherPlants ?? [];

  const species = fresh(at("species"));
  const genus = fresh(tree.sameGenus);
  const family = fresh(tree.sameFamily);
  const order = fresh(at("order"));

  return { species, genus, family, order };
}

/** Names as a sentence would list them: "Rosemary", "Rosemary and Sage", "Rosemary, Sage and Catmint". */
export function listWords(names: string[]): string {
  if (names.length <= 1) {
    return names[0] ?? "";
  }

  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)}`;
}
