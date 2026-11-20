<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import Check from "@lucide/svelte/icons/check";
  import CircleHelp from "@lucide/svelte/icons/circle-help";
  import Pencil from "@lucide/svelte/icons/pencil";
  import Percent from "@lucide/svelte/icons/percent";
  import Plus from "@lucide/svelte/icons/plus";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import {
    countsSentence,
    entrySentence,
    madeSentence,
    materialSentence,
    materialWord,
    moneyProblem,
    otherSentence,
    readMoney,
    replacedSentence,
    rowName,
    supersededSentence,
    type CostEntry,
    type Costing,
    type MoneyPair,
  } from "../../lib/costs";
  import type { Schemas } from "../../lib/types";
  import { money, plantName } from "../../lib/work";

  // The owner's view of what a batch cost, from its tray. Every figure is shown twice, with VAT and before VAT,
  // the one that counts first.
  let {
    line,
    onPurchases,
    onBack,
  }: {
    line: Schemas["ContainerLine"];
    /** Opens Purchases, where a purchase's cost comes from. */
    onPurchases?: () => void;
    onBack: () => void;
  } = $props();

  type Form =
    | { kind: "material" }
    | { kind: "vat"; entry: CostEntry }
    | { kind: "other"; entry: CostEntry | null };

  let costing = $state<Costing | null>(null);
  let problem = $state("");
  let said = $state("");
  let busy = $state(false);
  let form = $state<Form | null>(null);
  let amount = $state("");
  let nothing = $state(false);
  let note = $state("");
  let band = $state<Schemas["VatBand"]>("Standard");
  let includesVat = $state(true);
  let formHeading = $state<HTMLHeadingElement>();

  const what = $derived(costing ? materialWord(costing.source) : "seed");
  const withFirst = $derived(costing?.counts !== "BeforeVat");
  const replaced = $derived(
    costing?.history.filter((entry) => entry.replaced) ?? [],
  );

  async function load() {
    try {
      const { data } = await api.GET("/api/batches/{id}/costing", {
        params: { path: { id: line.batchId } },
      });
      costing = data ?? null;
      problem = data ? "" : "The costs couldn't be fetched just now.";
    } catch {
      problem = "Costs and takings will show when there's a signal.";
    }
  }

  onMount(load);

  /** The two figures in the order they're shown: the one that counts first. */
  const pair = (cost: MoneyPair) =>
    withFirst
      ? [Number(cost.withVat), Number(cost.beforeVat)]
      : [Number(cost.beforeVat), Number(cost.withVat)];

  async function open(next: Form) {
    said = "";
    problem = "";
    form = next;
    nothing = false;
    note = next.kind === "other" ? (next.entry?.note ?? "") : "";
    amount =
      next.kind === "other" && next.entry
        ? String(Number(next.entry.amountPaid))
        : "";

    if (next.kind === "vat") {
      band = next.entry.vatBand ?? "Standard";
      includesVat = next.entry.includesVat ?? true;
    }

    await tick();
    formHeading?.focus();
  }

  async function send(
    body: Omit<Schemas["CostRequest"], "eventId" | "occurredAt"> & {
      occurredAt?: string;
    },
    done: string | ((after: Costing) => string),
  ) {
    busy = true;

    try {
      const { response } = await api.POST("/api/batches/{id}/costs", {
        params: { path: { id: line.batchId } },
        body: {
          ...body,
          eventId: crypto.randomUUID(),
          occurredAt: body.occurredAt ?? new Date().toISOString(),
        },
      });

      if (!response.ok) {
        problem =
          response.status === 409
            ? "Someone changed that cost in the meantime. Here is how it stands now."
            : "That cost couldn't be saved. Please check it and try again.";
        await load();
        return;
      }

      form = null;
      await load();
      said = costing
        ? typeof done === "string"
          ? done
          : done(costing)
        : "Saved.";
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      busy = false;
    }
  }

  function saveAmount(event: SubmitEvent) {
    event.preventDefault();
    const paid = nothing ? 0 : readMoney(amount);

    if (paid === null || (form?.kind === "other" && paid === 0)) {
      problem = moneyProblem;
      return;
    }

    if (form?.kind === "other") {
      const entry = form.entry;
      void send(
        {
          kind: "Other",
          answer: "Amount",
          amount: paid,
          note,
          replacesId: entry?.id ?? null,
        },
        entry ? "The cost is changed." : "The cost is added.",
      );
      return;
    }

    void send(
      {
        kind: "Material",
        answer: paid === 0 ? "Free" : "Amount",
        amount: paid === 0 ? null : paid,
      },
      "The cost is saved.",
    );
  }

  function saveVat(event: SubmitEvent) {
    event.preventDefault();

    if (form?.kind !== "vat") {
      return;
    }

    const entry = form.entry;
    // Kept on the day it was for, so it keeps that day's rate.
    void send(
      {
        kind: entry.kind,
        answer: "Amount",
        amount: Number(entry.amountPaid),
        note: entry.note,
        occurredAt: entry.occurredAt,
        vatBand: band,
        includesVat,
        replacesId: entry.kind === "Other" ? entry.id : null,
      },
      (after) => {
        const now =
          entry.kind === "Other"
            ? after.others.find((other) => other.replacesId === entry.id)
            : after.material.entry;
        return now?.cost ? madeSentence(now.cost) : "The VAT is saved.";
      },
    );
  }

  function notKnown() {
    void send(
      { kind: "Material", answer: "NotKnown" },
      "It's saved that nobody knows what it cost.",
    );
  }

  function remove(entry: CostEntry) {
    if (
      confirm(
        `Remove ${money(Number(entry.amountPaid))} for ${entry.note}? It stays in the list of earlier answers.`,
      )
    ) {
      void send(
        { kind: "Other", answer: "Removed", replacesId: entry.id },
        "The cost is removed.",
      );
    }
  }
</script>

{#snippet amounts(cost: MoneyPair, strong = false)}
  {#each pair(cost) as figure, index (index)}
    <td class="cost-amount" class:counts={index === 0}
      >{#if strong || index === 0}<strong>{money(figure)}</strong>{:else}{money(
          figure,
        )}{/if}</td
    >
  {/each}
{/snippet}

{#snippet entryButtons(entry: CostEntry)}
  <div class="row">
    {#if entry.kind === "Other"}
      <button
        type="button"
        class="secondary"
        onclick={() => open({ kind: "other", entry })}
        ><Pencil size={16} aria-hidden="true" /> Change the cost</button
      >
    {:else}
      <button
        type="button"
        class="secondary"
        onclick={() => open({ kind: "material" })}
        ><Pencil size={16} aria-hidden="true" /> Change the cost</button
      >
    {/if}
    <button
      type="button"
      class="secondary"
      onclick={() => open({ kind: "vat", entry })}
      ><Percent size={16} aria-hidden="true" /> Change the VAT</button
    >
    {#if entry.kind === "Other"}
      <button
        type="button"
        class="secondary destructive"
        onclick={() => remove(entry)}
        ><Trash2 size={16} aria-hidden="true" /> Remove</button
      >
    {/if}
  </div>
{/snippet}

<main class="costing">
  <h1>Costs and takings</h1>
  <p class="action-for">
    <strong>Batch {line.traceabilityCode}</strong>
    {plantName(line.commonName, line.variety)}
  </p>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if said}<p class="card done" role="status">{said}</p>{/if}

  {#if costing}
    {@const material = costing.material}
    <section class="stack" aria-labelledby="cost-heading">
      <h2 id="cost-heading" class="form-heading">What it cost</h2>
      <p class="muted">{countsSentence(costing.vatRegistered)}</p>

      {#if material.state === "NoAnswer"}
        <div class="card caution stack tight">
          <p>{materialSentence(costing)}</p>
          <div class="row">
            <button type="button" onclick={() => open({ kind: "material" })}
              ><Plus size={16} aria-hidden="true" /> Add what it cost</button
            >
            <button
              type="button"
              class="secondary"
              disabled={busy}
              onclick={notKnown}
              ><CircleHelp size={16} aria-hidden="true" /> I don't know what it cost</button
            >
          </div>
        </div>
      {/if}

      <table class="cost-table">
        <caption class="visually-hidden"
          >What batch {costing.traceabilityCode} cost</caption
        >
        <thead>
          <tr>
            <th scope="col">What</th>
            {#if withFirst}
              <!-- Which column counts is said in words, not left to bold type. -->
              <th
                scope="col"
                class="counts"
                aria-label="With VAT, the figures that count">With VAT</th
              >
              <th scope="col">Before VAT</th>
            {:else}
              <!-- Which column counts is said in words, not left to bold type. -->
              <th
                scope="col"
                class="counts"
                aria-label="Before VAT, the figures that count">Before VAT</th
              >
              <th scope="col">With VAT</th>
            {/if}
          </tr>
        </thead>
        <tbody>
          {#if material.cost}
            <tr>
              <th scope="row">{rowName(null, costing.source)}</th>
              {@render amounts(material.cost)}
            </tr>
          {/if}
          {#if material.state !== "NoAnswer"}
            <tr class="cost-said">
              <td colspan="3">
                {#if material.state === "Amount" && material.entry}
                  <p>
                    {entrySentence(material.entry, costing.history, what)}
                  </p>
                  {@render entryButtons(material.entry)}
                {:else}
                  <p>{materialSentence(costing)}</p>
                  {#if material.superseded}<p class="muted">
                      {supersededSentence(material.superseded)}
                    </p>{/if}
                  {#if material.state === "Purchase" && onPurchases}
                    <button
                      type="button"
                      class="secondary"
                      onclick={onPurchases}
                      ><ArrowRight size={16} aria-hidden="true" /> Go to Purchases</button
                    >
                  {:else if material.state === "Free" || material.state === "NotKnown"}
                    <button
                      type="button"
                      class="secondary"
                      onclick={() => open({ kind: "material" })}
                      ><Pencil size={16} aria-hidden="true" /> Change the cost</button
                    >
                  {/if}
                {/if}
              </td>
            </tr>
          {/if}
          {#each costing.others as entry (entry.id)}
            <tr>
              <th scope="row">{rowName(entry, costing.source)}</th>
              {#if entry.cost}{@render amounts(entry.cost)}{/if}
            </tr>
            <tr class="cost-said">
              <td colspan="3">
                <p>{otherSentence(entry, costing.history)}</p>
                {@render entryButtons(entry)}
              </td>
            </tr>
          {/each}
          {#if costing.total}
            <tr class="cost-total">
              <th scope="row">Altogether</th>
              {@render amounts(costing.total, true)}
            </tr>
          {/if}
        </tbody>
      </table>

      {#if !form}
        <button
          type="button"
          class="secondary"
          onclick={() => open({ kind: "other", entry: null })}
          ><Plus size={16} aria-hidden="true" /> Add another cost</button
        >
      {/if}
    </section>

    {#if form?.kind === "vat"}
      {@const entry = form.entry}
      <form class="form-card stack" onsubmit={saveVat}>
        <h2 class="form-heading" tabindex="-1" bind:this={formHeading}>
          Change the VAT
        </h2>
        <fieldset class="stack tight">
          <legend>How much VAT was in this cost?</legend>
          <label class="tick"
            ><input
              type="radio"
              name="vat-band"
              value="Standard"
              bind:group={band}
            />
            The standard rate, {Number(costing.standardRate)}%</label
          >
          <label class="tick"
            ><input
              type="radio"
              name="vat-band"
              value="Reduced"
              bind:group={band}
            />
            The reduced rate, {Number(costing.reducedRate)}%</label
          >
          <label class="tick"
            ><input
              type="radio"
              name="vat-band"
              value="Zero"
              bind:group={band}
            /> None, as when the seller isn't registered for VAT</label
          >
        </fieldset>
        {#if band !== "Zero"}
          <fieldset class="stack tight">
            <legend
              >Is {money(Number(entry.amountPaid))} the price with the VAT in it?</legend
            >
            <label class="tick"
              ><input
                type="radio"
                name="vat-included"
                value={true}
                bind:group={includesVat}
              /> Yes, it's what was paid.</label
            >
            <label class="tick"
              ><input
                type="radio"
                name="vat-included"
                value={false}
                bind:group={includesVat}
              /> No, it's the price before VAT.</label
            >
          </fieldset>
        {/if}
        <div class="form-actions">
          <button type="submit" disabled={busy}
            ><Check size={18} aria-hidden="true" /> Save the VAT</button
          >
          <button type="button" class="secondary" onclick={() => (form = null)}
            >Cancel</button
          >
        </div>
      </form>
    {:else if form}
      <form class="form-card stack" onsubmit={saveAmount}>
        <h2 class="form-heading" tabindex="-1" bind:this={formHeading}>
          {form.kind === "material"
            ? `What the ${what} cost`
            : form.entry
              ? "Change this cost"
              : "Add another cost"}
        </h2>
        {#if form.kind === "other"}
          <label
            >What was it? <input
              bind:value={note}
              maxlength="200"
              required
            /></label
          >
        {/if}
        <label
          >What did it cost, with VAT? (£) <input
            bind:value={amount}
            inputmode="decimal"
            autocomplete="off"
            disabled={nothing}
          /></label
        >
        {#if form.kind === "material"}
          <label class="tick"
            ><input type="checkbox" bind:checked={nothing} /> It cost nothing, because
            it was our own or a gift.</label
          >
        {/if}
        <div class="form-actions">
          <button type="submit" disabled={busy}
            ><Check size={18} aria-hidden="true" />
            {form.kind === "other" && !form.entry
              ? "Add this cost"
              : "Save the cost"}</button
          >
          <button type="button" class="secondary" onclick={() => (form = null)}
            >Cancel</button
          >
        </div>
      </form>
    {/if}

    {#if replaced.length > 0}
      <section class="stack tight" aria-labelledby="earlier-heading">
        <h2 id="earlier-heading" class="form-heading">Earlier answers</h2>
        <ul class="plain-list stack tight">
          {#each replaced as entry (entry.id)}
            <li class="muted replaced-cost">{replacedSentence(entry)}</li>
          {/each}
        </ul>
      </section>
    {/if}

    <p class="muted">
      These figures count what went into the plants: the {what} and anything else
      added here. They leave out wages, heating and rent.
    </p>
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
