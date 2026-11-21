<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import CircleHelp from "@lucide/svelte/icons/circle-help";
  import Gift from "@lucide/svelte/icons/gift";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { materialWord, moneyProblem, readMoney } from "../../lib/costs";
  import { isoDay, onDay } from "../../lib/days";
  import type { Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";

  // The batches nobody has said the cost of, for the owner to answer one by one or all at once as not known.
  let { onBack }: { onBack: () => void } = $props();

  type Missing = Schemas["MissingCost"];

  let batches = $state<Missing[] | null>(null);
  let amounts = $state<Record<string, string>>({});
  let problems = $state<Record<string, string>>({});
  let problem = $state("");
  let said = $state("");
  let busy = $state(false);
  let heading = $state<HTMLHeadingElement>();

  async function load() {
    try {
      const { data } = await api.GET("/api/costs/missing");
      batches = data ?? [];
      problem = data ? "" : "The batches couldn't be fetched just now.";
    } catch {
      problem = "The batches will show when there's a signal.";
    }
  }

  onMount(load);

  /** "Lavender 'Hidcote', sown on 3 March 2026, batch B000042, in tray C000101." */
  function describe(batch: Missing): string {
    const started =
      batch.source === "SeedLot"
        ? "sown"
        : batch.source === "BoughtIn"
          ? "arrived"
          : "taken";
    const when = batch.startedAt
      ? `, ${started} ${onDay(isoDay(new Date(batch.startedAt)))}`
      : "";
    const trays =
      batch.trays.length === 0
        ? ""
        : batch.trays.length === 1
          ? `, in tray ${batch.trays[0]}`
          : `, in trays ${batch.trays.join(", ")}`;
    return `${plantName(batch.commonName, batch.variety)}${when}, batch ${batch.traceabilityCode}${trays}.`;
  }

  /** After an answer, the list again, with the focus back at the top so the keyboard isn't lost. */
  async function answered(message: string) {
    said = message;
    await load();
    await tick();
    heading?.focus();
  }

  async function answer(
    batch: Missing,
    body: { answer: Schemas["CostAnswer"]; amount?: number | null },
    done: string,
  ) {
    busy = true;
    problem = "";

    try {
      const { response } = await api.POST("/api/batches/{id}/costs", {
        params: { path: { id: batch.batchId } },
        body: {
          eventId: crypto.randomUUID(),
          occurredAt: new Date().toISOString(),
          kind: "Material",
          ...body,
        },
      });

      if (response.status === 409) {
        await answered(
          `Someone has already said what batch ${batch.traceabilityCode} cost, so it's no longer on the list.`,
        );
        return;
      }

      if (!response.ok) {
        problem =
          "That couldn't be saved. Please check it and try again in a minute.";
        return;
      }

      await answered(done);
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      busy = false;
    }
  }

  function saveAmount(event: SubmitEvent, batch: Missing) {
    event.preventDefault();
    const paid = readMoney(amounts[batch.batchId] ?? "");

    if (paid === null) {
      problems[batch.batchId] = moneyProblem;
      return;
    }

    problems[batch.batchId] = "";
    void answer(
      batch,
      paid === 0 ? { answer: "Free" } : { answer: "Amount", amount: paid },
      `The cost of batch ${batch.traceabilityCode} is saved.`,
    );
  }

  async function noneKnown() {
    if (!batches || batches.length === 0) {
      return;
    }

    const count = batches.length;

    if (
      !confirm(
        `This marks ${count === 1 ? "this batch" : `all ${count} batches`} as cost not known and leaves ${count === 1 ? "it" : "them"} out of the cost and profit figures. You can still add a cost to any of them later from its tray.`,
      )
    ) {
      return;
    }

    busy = true;
    problem = "";

    try {
      const { data } = await api.POST("/api/costs/not-known", {
        body: { batchIds: batches.map((batch) => batch.batchId) },
      });
      const marked = data?.marked ?? 0;
      await answered(
        marked === 1
          ? "1 batch is marked as cost not known."
          : `${marked} batches are marked as cost not known.`,
      );
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      busy = false;
    }
  }
</script>

<main class="costs-to-fill-in">
  <h1 tabindex="-1" bind:this={heading}>Costs to fill in</h1>
  <p class="muted page-intro">
    These batches have no cost yet. Put in what the seed or plants cost, with
    any VAT included, say it cost nothing, or say you don't know. A batch you
    don't know the cost of is left out of the cost and profit figures.
  </p>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if said}<p class="card done" role="status">{said}</p>{/if}

  {#if batches && batches.length === 0}
    <p class="note-empty muted">
      Every batch has a cost, or you've said it isn't known.
    </p>
  {:else if batches}
    <ul class="plain-list card-grid">
      {#each batches as batch (batch.batchId)}
        {@const what = materialWord(batch.source)}
        <li class="card stack tight">
          <h2 class="form-heading" id="cost-{batch.batchId}">
            {describe(batch)}
          </h2>
          <form
            class="stack tight"
            aria-labelledby="cost-{batch.batchId}"
            onsubmit={(event) => saveAmount(event, batch)}
          >
            <label
              >What the {what} cost, with VAT (£)
              <input
                bind:value={amounts[batch.batchId]}
                inputmode="decimal"
                autocomplete="off"
                aria-invalid={problems[batch.batchId] ? "true" : undefined}
                aria-describedby={problems[batch.batchId]
                  ? `cost-problem-${batch.batchId}`
                  : undefined}
              /></label
            >
            {#if problems[batch.batchId]}<p
                class="problem"
                id="cost-problem-{batch.batchId}"
              >
                {problems[batch.batchId]}
              </p>{/if}
            <div class="row">
              <button type="submit" disabled={busy}
                ><Check size={16} aria-hidden="true" /> Save the cost</button
              >
              <button
                type="button"
                class="secondary"
                disabled={busy}
                onclick={() =>
                  answer(
                    batch,
                    { answer: "Free" },
                    `Batch ${batch.traceabilityCode} is saved as costing nothing.`,
                  )}
                ><Gift size={16} aria-hidden="true" /> It cost nothing</button
              >
              <button
                type="button"
                class="secondary"
                disabled={busy}
                onclick={() =>
                  answer(
                    batch,
                    { answer: "NotKnown" },
                    `Batch ${batch.traceabilityCode} is marked as cost not known.`,
                  )}
                ><CircleHelp size={16} aria-hidden="true" /> I don't know</button
              >
            </div>
          </form>
        </li>
      {/each}
    </ul>
    <button type="button" class="secondary" disabled={busy} onclick={noneKnown}
      ><CircleHelp size={16} aria-hidden="true" /> I don't know what any of these
      cost</button
    >
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
