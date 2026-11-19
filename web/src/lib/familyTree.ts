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

/** Names as a sentence would list them: "Rosemary", "Rosemary and Sage", "Rosemary, Sage and Catmint". */
export function listWords(names: string[]): string {
  if (names.length <= 1) {
    return names[0] ?? "";
  }

  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)}`;
}
