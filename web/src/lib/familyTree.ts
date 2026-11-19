import type { Schemas } from "./types";

type Tree = Schemas["FamilyTree"];
type Rung = Schemas["TaxonRung"];
type TaxonName = Schemas["TaxonName"];

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

/** The ranks above a species. Anything below one, such as a variety, is as close as a species. */
export const aboveSpecies: ReadonlySet<string> = new Set([
  "kingdom",
  "phylum",
  "class",
  "order",
  "family",
  "genus",
]);

/** The ranks the tree branches at, below its root. */
const branchRanks = ["family", "genus", "species"] as const;

/** A plant hanging from the tree: one of the nursery's, or the plant whose page this is. */
export type Leaf = {
  plantId: string;
  name: string;
  hasPhoto: boolean;
  self: boolean;
};

/** A group in the tree, such as a genus, with the groups and plants below it. */
export type Branch = {
  taxonId: string;
  rank: string;
  scientificName: string;
  commonName: string | null;
  /** On the way from the root down to this plant. */
  onPath: boolean;
  branches: Branch[];
  leaves: Leaf[];
};

/** The tree drawn on a plant's page: the rungs above its root, and the root with everything below it. */
export type Grown = {
  above: Rung[];
  root: Branch | null;
};

const sortName = (branch: Branch) =>
  (branch.commonName ?? branch.scientificName).toLocaleLowerCase("en-GB");

function branchOf(taxon: TaxonName | Rung, rank: string, onPath: boolean) {
  return {
    taxonId: taxon.taxonId,
    rank,
    scientificName: taxon.scientificName,
    commonName: taxon.commonName ?? null,
    onPath,
    branches: [],
    leaves: [],
  } satisfies Branch;
}

/** This plant's own groups below the root, then its relatives' groups, each relative hung from its closest one. */
export function grow(tree: Tree, self: Omit<Leaf, "self">): Grown {
  const rootAt = ["order", "family"]
    .map((rank) => tree.lineage.findIndex((rung) => rung.rank === rank))
    .find((at) => at >= 0);

  if (rootAt === undefined) {
    return { above: tree.lineage, root: null };
  }

  const root = branchOf(tree.lineage[rootAt], tree.lineage[rootAt].rank, true);
  const below = tree.lineage
    .slice(rootAt + 1)
    .filter((rung) => (branchRanks as readonly string[]).includes(rung.rank));

  let at: Branch = root;
  for (const rung of below) {
    const next = branchOf(rung, rung.rank, true);
    at.branches.push(next);
    at = next;
  }
  at.leaves.push({ ...self, self: true });

  for (const relative of tree.relatives) {
    let hang: Branch = root;
    for (const rank of branchRanks) {
      const taxon = relative[rank];
      if (!taxon || taxon.taxonId === root.taxonId) {
        continue;
      }
      let next = hang.branches.find((each) => each.taxonId === taxon.taxonId);
      if (!next) {
        next = branchOf(taxon, rank, false);
        hang.branches.push(next);
      }
      hang = next;
    }
    hang.leaves.push({
      plantId: relative.plantId,
      name: relative.name,
      hasPhoto: relative.hasPhoto,
      self: false,
    });
  }

  tidy(root);
  return { above: tree.lineage.slice(0, rootAt), root };
}

/** This plant's way down first, then the rest by everyday name; this plant first among its leaves. */
function tidy(branch: Branch) {
  branch.branches.sort(
    (one, other) =>
      Number(other.onPath) - Number(one.onPath) ||
      sortName(one).localeCompare(sortName(other), "en-GB"),
  );
  branch.leaves.sort((one, other) => Number(other.self) - Number(one.self));
  branch.branches.forEach(tidy);
}

/** How many plants hang anywhere below a branch, this plant included. */
export function leafCount(branch: Branch): number {
  return branch.branches.reduce(
    (total, each) => total + leafCount(each),
    branch.leaves.length,
  );
}

/** The other plants anywhere below a branch, this plant left out. */
export const relativeCount = (branch: Branch) =>
  leafCount(branch) - (hasSelf(branch) ? 1 : 0);

function hasSelf(branch: Branch): boolean {
  return (
    branch.leaves.some((leaf) => leaf.self) || branch.branches.some(hasSelf)
  );
}

/** Names as a sentence would list them: "Rosemary", "Rosemary and Sage", "Rosemary, Sage and Catmint". */
export function listWords(names: string[]): string {
  if (names.length <= 1) {
    return names[0] ?? "";
  }

  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)}`;
}
