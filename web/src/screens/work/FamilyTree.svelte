<script lang="ts">
  import ChevronDown from "@lucide/svelte/icons/chevron-down";
  import ChevronUp from "@lucide/svelte/icons/chevron-up";
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import RefreshCw from "@lucide/svelte/icons/refresh-cw";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { rankLabel } from "../../lib/familyTree";
  import type { Schemas } from "../../lib/types";

  // Where a plant sits in the plant kingdom, from the broadest group down to the plant itself, and the nursery's other
  // plants in the same genus and family, each a tap away.
  let {
    plantId,
    canManage = false,
    onOpenPlant,
  }: {
    plantId: string;
    canManage?: boolean;
    /** Opens another of the nursery's plants. */
    onOpenPlant?: (id: string) => void;
  } = $props();

  const mostNames = 12;

  // The genus and family are named in sentences below, so only the order and the species open to their plants.
  const openable = new Set(["order", "species"]);

  let tree = $state<Schemas["FamilyTree"] | null>(null);
  let failed = $state(false);
  let open = $state<string | null>(null);
  let showingAll = $state(false);

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

  const family = $derived(
    showingAll
      ? (tree?.sameFamily ?? [])
      : (tree?.sameFamily ?? []).slice(0, mostNames),
  );
  const italic = (rank: string) => rank === "genus" || rank === "species";
  const rankWord = (rank: string) => rankLabel(rank).toLowerCase();
</script>

{#snippet plantButtons(plants: readonly Schemas["PlantName"][])}
  <div class="row family-plants">
    {#each plants as plant (plant.plantId)}
      <button
        class="secondary"
        onclick={() => onOpenPlant?.(plant.plantId)}
        disabled={!onOpenPlant}>{plant.name}</button
      >
    {/each}
  </div>
{/snippet}

<section class="stack family-tree" aria-labelledby="family-tree-heading">
  <h2 id="family-tree-heading">Family tree</h2>

  {#if !tree && !failed}
    <p class="muted" aria-live="polite">Finding the family tree…</p>
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
  {:else if tree}
    <p class="muted">
      This is where it sits in the plant kingdom, from the broadest group down
      to the plant itself.
    </p>
    <ol class="family-ladder">
      {#each tree.lineage as rung, index (rung.taxonId)}
        <li style:--depth={index}>
          <span>
            {rankLabel(rung.rank)}:
            {#if italic(rung.rank)}<em>{rung.scientificName}</em
              >{:else}{rung.scientificName}{/if}{rung.commonName
              ? ` (${rung.commonName})`
              : ""}
          </span>
          {#if openable.has(rung.rank) && rung.otherPlants.length > 0}
            {@const shown = open === rung.taxonId}
            <button
              class="link-button"
              aria-expanded={shown}
              onclick={() => (open = shown ? null : rung.taxonId)}
            >
              {#if shown}<ChevronUp
                  size={16}
                  aria-hidden="true"
                />{:else}<ChevronDown size={16} aria-hidden="true" />{/if}
              {shown
                ? `Hide the other plants in this ${rankWord(rung.rank)}`
                : rung.otherPlants.length === 1
                  ? `Show the other plant in this ${rankWord(rung.rank)}`
                  : `Show the ${rung.otherPlants.length} other plants in this ${rankWord(rung.rank)}`}
            </button>
            {#if shown}
              {@render plantButtons(rung.otherPlants)}
            {/if}
          {/if}
        </li>
      {/each}
    </ol>

    {#if tree.sameGenus.length > 0}
      <p>
        {tree.sameGenus.length === 1
          ? "You grow one other plant in this genus."
          : "You grow these other plants in this genus."}
      </p>
      {@render plantButtons(tree.sameGenus)}
    {/if}
    {#if tree.sameFamily.length > 0}
      <p>
        {tree.sameFamily.length === 1
          ? "You grow one other plant in the same family."
          : "You grow these other plants in the same family."}
      </p>
      {@render plantButtons(family)}
      {#if tree.sameFamily.length > mostNames && !showingAll}
        <button class="secondary" onclick={() => (showingAll = true)}
          >Show all {tree.sameFamily.length} plants in this family</button
        >
      {/if}
    {:else if tree.sameGenus.length === 0}
      <p class="muted">Nothing else at the nursery is in this family yet.</p>
    {/if}

    <p class="muted credit">
      The classification is from
      <a
        class="small-button"
        href="https://doi.org/10.15468/39omei"
        target="_blank"
        rel="noopener noreferrer"
        >GBIF Backbone Taxonomy, CC BY 4.0 <ExternalLink
          size={16}
          aria-hidden="true"
        /></a
      >. Everyday names are from
      <a
        class="small-button"
        href="https://www.inaturalist.org/"
        target="_blank"
        rel="noopener noreferrer"
        >iNaturalist <ExternalLink size={16} aria-hidden="true" /></a
      >.
    </p>
  {/if}
</section>
