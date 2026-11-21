<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    bothFigures,
    materialWord,
    perPlantSentences,
    type Costing,
  } from "../../lib/costs";

  // "What it cost", on a batch's trace report: the whole cost and what each plant has cost, for the owner.
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
</script>

<section class="stack" aria-labelledby="trace-cost">
  <h2 id="trace-cost">What it cost</h2>
  {#if problem}<p class="muted">{problem}</p>{/if}
  {#if costing}
    {#if costing.total}
      <p>
        Altogether it has cost {bothFigures(costing.total, costing.counts)}.
      </p>
    {:else if costing.material.state === "NotKnown"}
      <p class="muted">
        You said nobody knows what the {materialWord(costing.source)} cost, so this
        batch has no total.
      </p>
    {:else}
      <p class="muted">
        Nobody has said what the {materialWord(costing.source)} cost yet, so there's
        no total. You can add it from the batch's tray.
      </p>
    {/if}
    {#each perPlantSentences(costing) as sentence, index (index)}
      <p>{sentence}</p>
    {/each}
  {/if}
</section>
