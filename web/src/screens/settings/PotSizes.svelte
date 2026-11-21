<script lang="ts">
  import Archive from "@lucide/svelte/icons/archive";
  import Hash from "@lucide/svelte/icons/hash";
  import Pencil from "@lucide/svelte/icons/pencil";
  import FormPanel from "../FormPanel.svelte";
  import EntryPhotos from "../manage/EntryPhotos.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { stopUsing } from "../../lib/archive";
  import ArchivedEntries from "../manage/ArchivedEntries.svelte";
  import PotAllowance from "./PotAllowance.svelte";
  import { isOn } from "../../lib/features.svelte";
  import { attempt, optional, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  let sizes = $state<Schemas["PotSizeResponse"][]>([]);
  let quantity = $state("");
  let counting = $state<string | null>(null);
  let newCount = $state("");
  let name = $state("");
  let problem = $state("");
  let renaming = $state<string | null>(null);
  let newName = $state("");
  // What potting into each size costs, while costs are kept.
  let allowances = $state<Record<string, Schemas["PotAllowance"]>>({});
  let standardRate = $state(20);

  const setProblem = (message: string) => (problem = message);

  function explain(status: number) {
    problem =
      status === 409
        ? "That pot size is already on the list."
        : "Please enter a name.";
  }

  async function load() {
    // All at once; the cost of potting is left unknown, not shown as none, if it can't be fetched.
    const [list, costs, rates] = await Promise.all([
      api.GET("/api/pot-sizes"),
      isOn("Costs") ? api.GET("/api/pot-sizes/allowances") : null,
      isOn("Costs") ? api.GET("/api/settings/vat-rates") : null,
    ]);
    sizes = list.data ?? [];
    allowances = Object.fromEntries(
      (costs?.data ?? []).map((row) => [row.potSizeId, row]),
    );
    standardRate = Number(rates?.data?.standard ?? standardRate);
  }

  onMount(() => void attempt(load, setProblem));

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/pot-sizes", {
        body: {
          name,
          quantityInStock:
            optional(quantity) === null ? null : Number(quantity),
        },
      });

      if (response.ok) {
        name = "";
        quantity = "";
        await load();
      } else {
        explain(response.status);
      }
    }, setProblem);
  };

  const rename = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/pot-sizes/{id}", {
        params: { path: { id } },
        body: { name: newName },
      });

      if (response.ok) {
        renaming = null;
        await load();
      } else {
        explain(response.status);
      }
    }, setProblem);
  };

  const saveCount = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/pot-sizes/{id}/quantity", {
        params: { path: { id } },
        body: {
          quantity: optional(newCount) === null ? null : Number(newCount),
        },
      });

      if (response.ok) {
        counting = null;
        problem = "";
        await load();
      } else {
        problem = "Please enter a whole number.";
      }
    }, setProblem);
  };

  const archive = (size: Schemas["PotSizeResponse"]) =>
    attempt(async () => {
      problem = (await stopUsing("pot-sizes", size.id, size.name)) ?? "";
      await load();
    }, setProblem);
</script>

<main>
  <h1>Pot sizes</h1>

  <div class="split">
    <section>
      <FormPanel label="Add a pot size">
        <form class="form-card stack" onsubmit={add}>
          <h2 class="form-heading">Add a pot size</h2>
          <label>New pot size <input bind:value={name} required /></label>
          <label>
            How many we've got (optional)
            <input bind:value={quantity} inputmode="numeric" />
          </label>
          <button type="submit">Add this pot size</button>
        </form>
      </FormPanel>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      <div class="card-grid record-grid">
        {#each sizes as size (size.id)}
          <div class="card">
            {#if renaming === size.id}
              <form class="stack" onsubmit={(event) => rename(event, size.id)}>
                <label>Name <input bind:value={newName} required /></label>
                <button type="submit">Save</button>
              </form>
            {:else}
              <strong>{size.name}</strong>
              <span class="muted">
                {size.quantityInStock === null ||
                size.quantityInStock === undefined
                  ? "These haven't been counted yet."
                  : `There are ${size.quantityInStock} in stock.`}
              </span>
              {#if isOn("Costs")}
                <PotAllowance
                  {size}
                  allowance={allowances[size.id]}
                  {standardRate}
                  onSaved={load}
                />
              {/if}
              <EntryPhotos
                entry="PotSize"
                id={size.id}
                photoIds={size.photoIds as string[]}
                onChanged={load}
                onProblem={setProblem}
              />
              {#if counting === size.id}
                <form
                  class="stack"
                  onsubmit={(event) => saveCount(event, size.id)}
                >
                  <label>
                    How many we've got
                    <input bind:value={newCount} inputmode="numeric" />
                  </label>
                  <button type="submit">Save count</button>
                </form>
              {/if}
              <div class="row card-actions">
                <button
                  class="link-button"
                  onclick={() => {
                    counting = size.id;
                    newCount = size.quantityInStock?.toString() ?? "";
                  }}><Hash size={16} aria-hidden="true" />Count</button
                >
                <button
                  class="link-button"
                  onclick={() => {
                    renaming = size.id;
                    newName = size.name;
                  }}><Pencil size={16} aria-hidden="true" />Rename</button
                >
                <button class="link-button" onclick={() => archive(size)}
                  ><Archive size={16} aria-hidden="true" />Stop using</button
                >
              </div>
            {/if}
          </div>
        {/each}
      </div>
      <ArchivedEntries list="pot-sizes" onRestored={load} />
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
