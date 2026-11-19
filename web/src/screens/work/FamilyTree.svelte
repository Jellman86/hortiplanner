<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import ChevronDown from "@lucide/svelte/icons/chevron-down";
  import ChevronUp from "@lucide/svelte/icons/chevron-up";
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import RefreshCw from "@lucide/svelte/icons/refresh-cw";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    aboveSpecies,
    grow,
    rankLabel,
    relativeCount,
    type Branch,
    type Leaf,
  } from "../../lib/familyTree";
  import type { Schemas } from "../../lib/types";
  import PlantThumb from "./PlantThumb.svelte";

  // Where a plant sits among other plants, drawn as a tree from its order down to the plant itself, with the
  // nursery's other plants hanging from the branches they share, each a tap away.
  let {
    plantId,
    plantName,
    photoUrl = null,
    canManage = false,
    onOpenPlant,
  }: {
    plantId: string;
    /** The nursery's own name for the plant, on its leaf. */
    plantName: string;
    /** The plant's own picture, for its leaf. */
    photoUrl?: string | null;
    canManage?: boolean;
    /** Opens another of the nursery's plants. */
    onOpenPlant?: (id: string) => void;
  } = $props();

  /** Leaves shown under one group before "Show all". */
  const mostLeaves = 6;
  /** Plants in this plant's family before its other genera start folded. */
  const mostOpen = 10;

  let tree = $state<Schemas["FamilyTree"] | null>(null);
  let failed = $state(false);
  let broader = $state(false);
  let distant = $state(false);
  /** Groups opened or folded by hand, by taxon. */
  let opened = $state<Record<string, boolean>>({});
  /** Groups showing all their leaves, by taxon. */
  let showingAll = $state<Record<string, boolean>>({});

  async function load() {
    failed = false;

    try {
      const { data } = await api.GET("/api/plants/{id}/family-tree", {
        params: { path: { id: plantId } },
      });
      tree = data ?? null;
      failed = !data;
    } catch {
      failed = true;
    }
  }

  onMount(load);

  const grown = $derived(
    tree
      ? grow(tree, { plantId, name: plantName, hasPhoto: !!photoUrl })
      : null,
  );
  const root = $derived(grown?.root ?? null);
  /** This plant's family: the root itself when the tree grows from the family. */
  const family = $derived(
    root?.rank === "order"
      ? (root.branches.find((each) => each.onPath) ?? null)
      : root,
  );
  /** Plants in other families of the same order, or matched only to the order: the more distant relatives. */
  const distantCount = $derived(
    root?.rank === "order"
      ? root.branches
          .filter((each) => !each.onPath)
          .reduce((total, each) => total + relativeCount(each), 0) +
          root.leaves.length
      : 0,
  );
  const closeCount = $derived(family ? relativeCount(family) : 0);
  const crowded = $derived(closeCount + 1 > mostOpen);
  const lastRank = $derived(tree?.lineage.at(-1)?.rank);

  const italic = (rank: string) => rank === "genus" || !aboveSpecies.has(rank);
  const rankWord = (rank: string) => rankLabel(rank).toLowerCase();
  const nodeId = (branch: Branch) => `family-node-${branch.taxonId}`;
  const listId = (branch: Branch) => `family-children-${branch.taxonId}`;
  const plants = (count: number) =>
    count === 1 ? "the one plant" : `the ${count} plants`;

  /** Another genus in a crowded family starts folded; everything else starts open. */
  const isOpen = (branch: Branch, parent: Branch) =>
    opened[branch.taxonId] ??
    !(
      crowded &&
      parent === family &&
      branch.rank === "genus" &&
      !branch.onPath
    );
  /** Shown at the root only once the more distant relatives are opened. */
  const shown = (branch: Branch, parent: Branch) =>
    parent !== root || parent.rank !== "order" || branch.onPath || distant;
</script>

{#snippet toggle(
  open: boolean,
  controls: string,
  label: string,
  onclick: () => void,
  describedBy?: string,
)}
  <button
    class="link-button"
    aria-expanded={open}
    aria-controls={controls}
    aria-describedby={describedBy}
    {onclick}
  >
    {#if open}<ChevronUp size={16} aria-hidden="true" />{:else}<ChevronDown
        size={16}
        aria-hidden="true"
      />{/if}
    {label}
  </button>
{/snippet}

{#snippet names(
  rank: string,
  scientificName: string,
  commonName: string | null,
)}
  <span class="rung-rank"
    >{rankLabel(rank)}<span class="visually-hidden">: </span></span
  >
  <span class="ftree-names">
    {#if commonName}
      <span class="rung-name"
        >{commonName}<span class="visually-hidden">, </span></span
      >
    {/if}
    <span class={commonName ? "rung-latin" : "rung-name"} lang="la"
      >{#if italic(rank)}<em>{scientificName}</em
        >{:else}{scientificName}{/if}</span
    >
  </span>
{/snippet}

{#snippet leaf(item: Leaf)}
  <li class="ftree-item" class:on-path={item.self}>
    {#if item.self}
      <div class="ftree-leaf this-plant" aria-current="true">
        <PlantThumb url={photoUrl} />
        <span class="ftree-leaf-name">
          <span class="rung-rank"
            >This plant<span class="visually-hidden">: </span></span
          >
          <strong>{item.name}</strong>
        </span>
      </div>
    {:else if onOpenPlant}
      <button
        class="link-button ftree-leaf"
        onclick={() => onOpenPlant?.(item.plantId)}
      >
        <PlantThumb
          url={item.hasPhoto ? `/api/plants/${item.plantId}/photo` : null}
        />
        <span class="ftree-leaf-name">{item.name}</span>
        <ArrowRight size={16} aria-hidden="true" />
      </button>
    {:else}
      <div class="ftree-leaf">
        <PlantThumb
          url={item.hasPhoto ? `/api/plants/${item.plantId}/photo` : null}
        />
        <span class="ftree-leaf-name">{item.name}</span>
      </div>
    {/if}
  </li>
{/snippet}

{#snippet node(branch: Branch, parent: Branch | null)}
  {@const open = parent ? isOpen(branch, parent) : true}
  {@const foldable =
    !!parent &&
    !branch.onPath &&
    (opened[branch.taxonId] !== undefined || !open)}
  {@const hidden = branch === root && branch.rank === "order" && !distant}
  {@const all = showingAll[branch.taxonId] ?? false}
  {@const leaves = (all ? branch.leaves : branch.leaves.slice(0, mostLeaves))
    .filter((item) => !item.self)
    .filter(() => !hidden)}
  {@const own = branch.leaves.find((item) => item.self)}
  {@const word =
    branch.rank === "species"
      ? "of this species"
      : `in this ${rankWord(branch.rank)}`}
  <li class="ftree-item" class:on-path={branch.onPath}>
    <div class="ftree-node" id={nodeId(branch)}>
      {@render names(branch.rank, branch.scientificName, branch.commonName)}
    </div>
    <ul
      class="ftree-children"
      class:on-path={branch.onPath}
      role="list"
      id={listId(branch)}
    >
      <!-- One toggle that stays put, so focus stays on it as the group opens and folds. -->
      {#if foldable}
        <li class="ftree-item">
          {@render toggle(
            open,
            listId(branch),
            `${open ? "Hide" : "Show"} ${plants(relativeCount(branch))} ${word}`,
            () => (opened[branch.taxonId] = !open),
            nodeId(branch),
          )}
        </li>
      {/if}
      {#if open}
        <!-- This plant first, so the way down to it runs straight. -->
        {#if own}
          {@render leaf(own)}
        {/if}
        {#each branch.branches as child (child.taxonId)}
          {#if shown(child, branch)}
            {@render node(child, branch)}
          {/if}
        {/each}
        {#each leaves as item (item.plantId)}
          {@render leaf(item)}
        {/each}
        {#if branch.leaves.length > mostLeaves && !hidden}
          <li class="ftree-item">
            {@render toggle(
              all,
              listId(branch),
              all
                ? `Show only the first ${mostLeaves} plants ${word}`
                : `Show all ${branch.leaves.length} plants ${word}`,
              () => (showingAll[branch.taxonId] = !all),
              nodeId(branch),
            )}
          </li>
        {/if}
      {/if}
    </ul>
  </li>
{/snippet}

<section
  class="stack family-tree"
  aria-labelledby="family-tree-heading"
  aria-busy={!tree && !failed}
>
  <h2 id="family-tree-heading">Family tree</h2>

  {#if !tree && !failed}
    <p class="muted">Finding the family tree…</p>
  {:else if failed}
    <p class="muted">
      We couldn't get the family tree just now. Everything else on this page
      still works.
    </p>
    <button class="secondary" onclick={load}
      ><RefreshCw size={18} aria-hidden="true" /> Try again</button
    >
  {:else if tree && !tree.linked}
    <p class="note-empty muted">
      {canManage
        ? "This plant isn't matched to a species yet, so we can't show its family tree. Search for it under \"Find this plant's details\" below."
        : "This plant isn't matched to a species yet, so we can't show its family tree. The owner can match it from this page."}
    </p>
  {:else if tree && tree.unavailable}
    <p class="muted">
      We can't reach the online plant details just now, so the family tree isn't
      here yet. Please look again later.
    </p>
  {:else if tree && tree.lineage.length === 0}
    <p class="note-empty muted">
      We couldn't find this plant in the online classification, so there's no
      family tree to show.
    </p>
  {:else if tree && grown}
    <p class="muted">
      {tree.relatives.length > 0
        ? "This is where this plant sits among other plants. Its relatives at the nursery are on the branches."
        : "This is where this plant sits among other plants, from the wider groups down to the plant itself."}
    </p>
    <div class="card family-card">
      {#if grown.above.length > 0 && root}
        {@render toggle(
          broader,
          "family-ladder",
          broader ? "Hide the broader groups" : "Show the broader groups",
          () => (broader = !broader),
        )}
      {/if}

      {#if grown.above.length > 0 && (broader || !root)}
        <ol
          id="family-ladder"
          class="family-ladder"
          role="list"
          aria-label="The broader groups, from the {rankWord(
            grown.above[0].rank,
          )} down"
        >
          {#each grown.above as rung (rung.taxonId)}
            <li>
              {@render names(rung.rank, rung.scientificName, rung.commonName)}
            </li>
          {/each}
        </ol>
      {/if}

      <ul
        class="ftree"
        role="list"
        aria-label={root
          ? `Family tree, from the ${rankWord(root.rank)} down to this plant and its relatives at the nursery`
          : "This plant"}
      >
        {#if root}
          {@render node(root, null)}
        {:else}
          {@render leaf({
            plantId,
            name: plantName,
            hasPhoto: !!photoUrl,
            self: true,
          })}
        {/if}
      </ul>

      {#if lastRank && aboveSpecies.has(lastRank)}
        <p class="muted">
          This plant is matched to its {rankWord(lastRank)} rather than to one species,
          so its family tree stops at the {rankWord(lastRank)}.
        </p>
      {/if}

      {#if tree.relatives.length === 0}
        <p class="muted">
          Nothing else at the nursery is related to this plant yet.
        </p>
      {:else if family && closeCount === 0}
        <p class="muted">Nothing else at the nursery is in this family yet.</p>
      {/if}

      {#if distantCount > 0 && root}
        {@render toggle(
          distant,
          listId(root),
          distant
            ? "Hide the more distant relatives"
            : distantCount === 1
              ? "Show one more distant relative"
              : `Show ${distantCount} more distant relatives`,
          () => (distant = !distant),
        )}
      {/if}

      <div class="family-credits stack">
        <p class="muted credit">
          The classification is from the GBIF Backbone Taxonomy, which is shared
          under CC BY 4.0. The everyday names are from iNaturalist.
        </p>
        <div class="row">
          <a
            class="small-button"
            href="https://doi.org/10.15468/39omei"
            target="_blank"
            rel="noopener noreferrer"
            >See the GBIF Backbone Taxonomy <ExternalLink
              size={16}
              aria-hidden="true"
            /></a
          >
          <a
            class="small-button"
            href="https://www.inaturalist.org/"
            target="_blank"
            rel="noopener noreferrer"
            >Go to iNaturalist <ExternalLink size={16} aria-hidden="true" /></a
          >
        </div>
      </div>
    </div>
  {/if}
</section>
