<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    materialWord,
    perPlantSentences,
    soFarSentence,
    takingsSentences,
    wholeCost,
    type Costing,
  } from "../../lib/costs";

  // "What it cost", on a batch's trace report: the whole cost and what each plant has cost, for the owner. Left off
  // the printed report, which goes to an inspector and needn't carry what the nursery paid.
  let { batchId }: { batchId: string } = $props();

  let costing = $state<Costing | null>(null);
  let problem = $state("");

  onMount(async () => {
    try {
      const { data } = await api.GET("/api/batches/{id}/costing", {
        params: { path: { id: batchId } },
      });
      costing = data ?? null;
      problem = data
        ? ""
        : "What this batch cost couldn't be fetched just now.";
    } catch {
      problem = "What this batch cost will show when there's a signal.";
    }
  });

  const nothingSpent = $derived(
    !!costing?.total &&
      Number(costing.total.withVat) === 0 &&
      Number(costing.total.beforeVat) === 0,
  );
</script>

<section class="stack no-print" aria-labelledby="trace-cost">
  <h2 id="trace-cost">What it cost and took</h2>
  {#if problem}<p class="muted">{problem}</p>{/if}
  {#if costing}
    {#if nothingSpent}
      <p>Nothing has been spent on this batch so far.</p>
    {:else if costing.total}
      <p>
        Altogether it has cost {wholeCost(costing.total, costing.counts)}.
      </p>
      {#each perPlantSentences(costing) as sentence, index (index)}
        <p>{sentence}</p>
      {/each}
    {:else if costing.material.state === "NotKnown"}
      <p class="muted">
        You said nobody knows what the {materialWord(costing.source)} cost, so this
        batch has no total.
      </p>
    {:else}
      <p class="muted">
        Nobody has said what the {materialWord(costing.source)} cost yet, so there's
        no total. You can add it in Costs to fill in, which you'll find under Figures.
      </p>
    {/if}
    <!-- What it took is said whether or not its cost is known. -->
    {#each takingsSentences(costing) as sentence, index (index)}
      <p>{sentence}</p>
    {/each}
    {#if soFarSentence(costing)}<p>{soFarSentence(costing)}</p>{/if}
  {/if}
</section>
