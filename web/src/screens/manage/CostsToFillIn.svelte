<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import CircleHelp from "@lucide/svelte/icons/circle-help";
  import Gift from "@lucide/svelte/icons/gift";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import {
    materialWord,
    missingCostWords,
    moneyProblem,
    readMoney,
  } from "../../lib/costs";
  import { loadNeeds } from "../../lib/needs.svelte";
  import type { Schemas } from "../../lib/types";
  import PlantThumb from "../work/PlantThumb.svelte";

  // The batches nobody has said the cost of, for the owner to answer one by one or all at once as not known.
  let { onBack }: { onBack: () => void } = $props();

  type Missing = Schemas["MissingCost"];

  let batches = $state<Missing[] | null>(null);
  let amounts = $state<Record<string, string>>({});
  // A problem with one batch is said on its card, where the person is.
  let problems = $state<Record<string, string>>({});
  let problem = $state("");
  let said = $state("");
  let busy = $state(false);
  let heading = $state<HTMLHeadingElement>();

  const unreachable =
    "This device can't reach Hortiplanner. Please check the connection and try again.";

  /** The list again. Without it, what's shown stays as it is rather than looking as though nothing is left. */
  async function load() {
    try {
      const { data } = await api.GET("/api/costs/missing");

      if (data) {
        batches = data;
        problem = "";
      } else {
        problem =
          "The list of batches couldn't be fetched just now. Please try again in a minute.";
      }
    } catch {
      problem = "The batches will show when there's a signal.";
    }
  }

  onMount(load);

  /**
   * A batch answered: it leaves the list at once, the bell is brought up to date, and the focus goes to the card
   * that took its place, or to the top when there's none left.
   */
  async function answered(batch: Missing, message: string) {
    const index =
      batches?.findIndex((row) => row.batchId === batch.batchId) ?? -1;
    batches = batches?.filter((row) => row.batchId !== batch.batchId) ?? null;
    said = message;
    void loadNeeds();
    await load();
    await tick();
    const next = batches?.[Math.min(index, (batches?.length ?? 1) - 1)];
    (next ? document.getElementById(`cost-${next.batchId}`) : heading)?.focus();
  }

  async function answer(
    batch: Missing,
    body: { answer: Schemas["CostAnswer"]; amount?: number | null },
    done: string,
  ) {
    busy = true;
    said = "";
    problems[batch.batchId] = "";

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
          batch,
          `Someone else has already answered for batch ${batch.traceabilityCode}, so what you put in wasn't saved and it's no longer on this list.`,
        );
        return;
      }

      if (!response.ok) {
        problems[batch.batchId] =
          `The answer for batch ${batch.traceabilityCode} couldn't be saved. Please try again in a minute.`;
        return;
      }

      await answered(batch, done);
    } catch {
      problems[batch.batchId] = unreachable;
    } finally {
      busy = false;
    }
  }

  async function saveAmount(event: SubmitEvent, batch: Missing) {
    event.preventDefault();
    const paid = readMoney(amounts[batch.batchId] ?? "");

    if (paid === null) {
      problems[batch.batchId] = moneyProblem;
      // The box says what's wrong with it, through its description.
      await tick();
      document.getElementById(`cost-amount-${batch.batchId}`)?.focus();
      return;
    }

    void answer(
      batch,
      paid === 0 ? { answer: "Free" } : { answer: "Amount", amount: paid },
      paid === 0
        ? `Batch ${batch.traceabilityCode} is saved as costing nothing.`
        : `The cost of batch ${batch.traceabilityCode} is saved.`,
    );
  }

  function notKnown(batch: Missing) {
    // A batch in no tray can't be opened later to add a cost, so this is asked first.
    if (
      batch.trays.length === 0 &&
      !confirm(
        "Say that nobody knows what this batch cost? It'll be left out of the cost and profit figures, and as it isn't in a tray now, this can't be changed afterwards.",
      )
    ) {
      return;
    }

    void answer(
      batch,
      { answer: "NotKnown" },
      `You've said nobody knows what batch ${batch.traceabilityCode} cost.`,
    );
  }

  async function noneKnown() {
    if (!batches || batches.length === 0) {
      return;
    }

    const count = batches.length;

    if (
      !confirm(
        count === 1
          ? "Say that nobody knows what this batch cost? It'll be left out of the cost and profit figures. If it's still in a tray, you can add its cost later from that tray."
          : `Say that nobody knows what any of these ${count} batches cost? They'll be left out of the cost and profit figures. You can add a cost later to any that's still in a tray, from that tray.`,
      )
    ) {
      return;
    }

    busy = true;
    said = "";
    problem = "";

    try {
      const { data, response } = await api.POST("/api/costs/not-known", {
        body: { batchIds: batches.map((batch) => batch.batchId) },
      });

      if (!response.ok || !data) {
        problem =
          "Those answers couldn't be saved. Please try again in a minute.";
        return;
      }

      said =
        data.marked === 0
          ? "Nothing was changed, because someone had already answered for every one of them."
          : data.marked === 1
            ? "You've said nobody knows what that batch cost."
            : `You've said nobody knows what those ${data.marked} batches cost.`;
      void loadNeeds();
      await load();
      await tick();
      heading?.focus();
    } catch {
      problem = unreachable;
    } finally {
      busy = false;
    }
  }
</script>

<main class="costs-to-fill-in">
  <h1 tabindex="-1" bind:this={heading}>Costs to fill in</h1>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if said}<p class="card done" role="status">{said}</p>{/if}

  {#if batches && batches.length === 0}
    <p class="note-empty muted">
      Every batch has a cost, or you've said it isn't known.
    </p>
  {:else if batches}
    <p class="muted page-intro">
      These batches have no cost yet. For each one, put in what the seed or
      plants cost with any VAT included, or say it cost nothing, or say you
      don't know. A batch you don't know the cost of is left out of the cost and
      profit figures.
    </p>
    <p>
      <strong
        >{batches.length === 1
          ? "1 batch has no cost yet."
          : `${batches.length} batches have no cost yet.`}</strong
      >
    </p>
    <!-- One row for each batch on a desk, lined up, and a card for each on a phone. -->
    <ul class="plain-list cost-rows">
      {#each batches as batch (batch.batchId)}
        {@const words = missingCostWords(batch)}
        <li class="card cost-row">
          <PlantThumb
            url={batch.hasPhoto ? `/api/plants/${batch.plantId}/photo` : null}
          />
          <div class="cost-who">
            <h2 class="form-heading" id="cost-{batch.batchId}" tabindex="-1">
              {words.name}
            </h2>
            <p class="muted">{words.detail}</p>
          </div>
          <form
            class="cost-amount"
            aria-labelledby="cost-{batch.batchId}"
            onsubmit={(event) => saveAmount(event, batch)}
          >
            <label for="cost-amount-{batch.batchId}"
              >What the {materialWord(batch.source)} cost, with VAT (£)</label
            >
            <div class="cost-entry">
              <input
                id="cost-amount-{batch.batchId}"
                bind:value={amounts[batch.batchId]}
                inputmode="decimal"
                autocomplete="off"
                aria-invalid={problems[batch.batchId] === moneyProblem
                  ? "true"
                  : undefined}
                aria-describedby={problems[batch.batchId]
                  ? `cost-problem-${batch.batchId}`
                  : undefined}
              />
              <button type="submit" disabled={busy}
                ><Check size={16} aria-hidden="true" /> Save the cost</button
              >
            </div>
            {#if problems[batch.batchId]}<p
                class="problem"
                role="alert"
                id="cost-problem-{batch.batchId}"
              >
                {problems[batch.batchId]}
              </p>{/if}
          </form>
          <div class="cost-other">
            <button
              type="button"
              class="link-button"
              disabled={busy}
              onclick={() =>
                answer(
                  batch,
                  { answer: "Free" },
                  `Batch ${batch.traceabilityCode} is saved as costing nothing.`,
                )}><Gift size={16} aria-hidden="true" /> It cost nothing</button
            >
            <button
              type="button"
              class="link-button"
              disabled={busy}
              onclick={() => notKnown(batch)}
              ><CircleHelp size={16} aria-hidden="true" /> I don't know</button
            >
          </div>
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

<style>
  .cost-rows {
    display: grid;
    gap: 0.75rem;
  }

  /* On a phone: the picture beside the name, then the box with Save on one line, then the other two answers. */
  .cost-row {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    gap: 0.6rem 0.75rem;
    align-items: start;
  }

  .cost-amount,
  .cost-other {
    grid-column: 1 / -1;
  }

  .cost-who {
    display: grid;
    gap: 0.2rem;
  }

  .cost-amount {
    display: grid;
    gap: 0.3rem;
  }

  .cost-amount label {
    font-weight: 600;
  }

  .cost-entry {
    display: flex;
    gap: 0.5rem;
  }

  .cost-entry input {
    flex: 1 1 6rem;
    min-width: 0;
  }

  .cost-entry button {
    flex: none;
  }

  /* Side by side even on a narrow phone, as two equal buttons. */
  .cost-other {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 0.5rem;
  }

  .cost-other button {
    align-self: stretch;
    gap: 0.3rem;
    padding-inline: 0.4rem;
    font-size: 0.95rem;
  }

  /* With room, every batch is one row and the rows line up: picture, name, the box and Save, the other answers. */
  @container page (min-width: 60rem) {
    .cost-rows {
      grid-template-columns: auto minmax(14rem, 1fr) minmax(18rem, 24rem) auto;
      gap: 0.6rem 0;
    }

    .cost-row {
      grid-column: 1 / -1;
      grid-template-columns: subgrid;
      gap: 0 1rem;
      align-items: center;
    }

    .cost-amount,
    .cost-other {
      grid-column: auto;
    }

    .cost-other {
      display: flex;
    }

    .cost-other button {
      gap: 0.4rem;
      padding-inline: 0.9rem;
      font-size: inherit;
    }
  }
</style>
