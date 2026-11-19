<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import ChevronDown from "@lucide/svelte/icons/chevron-down";
  import ChevronUp from "@lucide/svelte/icons/chevron-up";
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import RefreshCw from "@lucide/svelte/icons/refresh-cw";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { broaderRanks, rankLabel, relatives } from "../../lib/familyTree";
  import type { Schemas } from "../../lib/types";

  // Where a plant sits among other plants, from the wider groups down to the plant itself, and the nursery's other
  // plants it's related to, closest first, each a tap away.
  let {
    plantId,
    plantName,
    canManage = false,
    onOpenPlant,
  }: {
    plantId: string;
    /** The nursery's own name for the plant, shown at the foot of the tree. */
    plantName: string;
    canManage?: boolean;
    /** Opens another of the nursery's plants. */
    onOpenPlant?: (id: string) => void;
  } = $props();

  const mostNames = 6;

  type Group = "species" | "genus" | "family";

  let tree = $state<Schemas["FamilyTree"] | null>(null);
  let failed = $state(false);
  let broader = $state(false);
  let distant = $state(false);
  let showingAll = $state<Record<Group, boolean>>({
    species: false,
    genus: false,
    family: false,
  });

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

  const lineage = $derived(tree?.lineage ?? []);
  const folds = $derived(
    lineage.some((rung) => broaderRanks.has(rung.rank)) &&
      lineage.some((rung) => !broaderRanks.has(rung.rank)),
  );
  const shown = $derived(
    folds && !broader
      ? lineage.filter((rung) => !broaderRanks.has(rung.rank))
      : lineage,
  );
  const lastRank = $derived(lineage.at(-1)?.rank);
  const kin = $derived(tree ? relatives(tree) : null);
  const close = $derived(
    kin ? kin.species.length + kin.genus.length + kin.family.length : 0,
  );
  const italic = (rank: string) => rank === "genus" || rank === "species";
  const rankWord = (rank: string) => rankLabel(rank).toLowerCase();

  const groups: { key: Group; one: string; many: string; all: string }[] = [
    {
      key: "species",
      one: "You grow one other plant of the same species.",
      many: "You grow these other plants of the same species.",
      all: "of the same species",
    },
    {
      key: "genus",
      one: "You grow one other plant in the same genus.",
      many: "You grow these other plants in the same genus.",
      all: "in the same genus",
    },
    {
      key: "family",
      one: "You grow one other plant in the same family.",
      many: "You grow these other plants in the same family.",
      all: "in the same family",
    },
  ];
</script>

{#snippet plantRows(plants: readonly Schemas["PlantName"][])}
  <ul class="family-plants">
    {#each plants as plant (plant.plantId)}
      <li>
        <button
          class="link-button"
          onclick={() => onOpenPlant?.(plant.plantId)}
          disabled={!onOpenPlant}
          ><span>{plant.name}</span><ArrowRight
            size={16}
            aria-hidden="true"
          /></button
        >
      </li>
    {/each}
  </ul>
{/snippet}

{#snippet toggle(
  open: boolean,
  controls: string,
  label: string,
  onclick: () => void,
)}
  <button
    class="link-button"
    aria-expanded={open}
    aria-controls={controls}
    {onclick}
  >
    {#if open}<ChevronUp size={16} aria-hidden="true" />{:else}<ChevronDown
        size={16}
        aria-hidden="true"
      />{/if}
    {label}
  </button>
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
  {:else if tree && kin}
    <p class="muted">
      This is where it sits among other plants, from the wider groups down to
      the plant itself.
    </p>
    <div class="card family-card">
      {#if folds}
        {@render toggle(
          broader,
          "family-ladder",
          broader ? "Hide the broader groups" : "Show the broader groups",
          () => (broader = !broader),
        )}
      {/if}

      <ol
        id="family-ladder"
        class="family-ladder"
        aria-label={`Classification, from the ${rankWord(shown[0]?.rank ?? "kingdom")} down to this plant`}
      >
        {#each shown as rung (rung.taxonId)}
          <li>
            <span class="rung-rank"
              >{rankLabel(rung.rank)}<span class="visually-hidden"
                >:
              </span></span
            >
            {#if rung.commonName}
              <span class="rung-name"
                >{rung.commonName}<span class="visually-hidden">, </span></span
              >
            {/if}
            <span class={rung.commonName ? "rung-latin" : "rung-name"} lang="la"
              >{#if italic(rung.rank)}<em>{rung.scientificName}</em
                >{:else}{rung.scientificName}{/if}</span
            >
          </li>
        {/each}
        <li class="this-plant" aria-current="true">
          <span class="rung-rank"
            >This plant<span class="visually-hidden">: </span></span
          >
          <span class="rung-name">{plantName}</span>
        </li>
      </ol>

      {#if lastRank && lastRank !== "species"}
        <p class="muted">
          This plant is matched to its {rankWord(lastRank)} rather than to one species,
          so its family tree stops at the {rankWord(lastRank)}.
        </p>
      {/if}

      <div class="family-relatives stack">
        <h3>Its relatives at the nursery</h3>
        {#each groups as group (group.key)}
          {@const plants = kin[group.key]}
          {#if plants.length > 0}
            <p>{plants.length === 1 ? group.one : group.many}</p>
            {@render plantRows(
              showingAll[group.key] ? plants : plants.slice(0, mostNames),
            )}
            {#if plants.length > mostNames && !showingAll[group.key]}
              <button
                class="link-button"
                onclick={() => (showingAll[group.key] = true)}
                ><ChevronDown size={16} aria-hidden="true" /> Show all {plants.length}
                {group.all}</button
              >
            {/if}
          {/if}
        {/each}

        {#if close === 0 && kin.order.length === 0}
          <p class="muted">
            Nothing else at the nursery is related to this plant yet.
          </p>
        {:else if close === 0}
          <p class="muted">
            Nothing else at the nursery is in this family yet.
          </p>
        {/if}

        {#if kin.order.length > 0}
          {@render toggle(
            distant,
            "family-distant",
            distant
              ? "Hide the more distant relatives"
              : kin.order.length === 1
                ? "Show one more distant relative"
                : `Show ${kin.order.length} more distant relatives`,
            () => (distant = !distant),
          )}
          <div id="family-distant" class="stack" hidden={!distant}>
            <p>
              {kin.order.length === 1
                ? "This plant is in the same order."
                : "These plants are in the same order."}
            </p>
            {@render plantRows(kin.order)}
          </div>
        {/if}
      </div>

      <p class="muted credit family-credits">
        <span
          >The classification is from
          <a
            class="small-button"
            href="https://doi.org/10.15468/39omei"
            target="_blank"
            rel="noopener noreferrer"
            >GBIF Backbone Taxonomy, CC BY 4.0 <ExternalLink
              size={16}
              aria-hidden="true"
            /></a
          >.</span
        >
        <span
          >Everyday names are from
          <a
            class="small-button"
            href="https://www.inaturalist.org/"
            target="_blank"
            rel="noopener noreferrer"
            >iNaturalist <ExternalLink size={16} aria-hidden="true" /></a
          >.</span
        >
      </p>
    </div>
  {/if}
</section>
