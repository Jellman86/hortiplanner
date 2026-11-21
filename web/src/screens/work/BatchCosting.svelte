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
    perPlantSentences,
    potsSentences,
    readMoney,
    replacedParts,
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
    onPotSizes,
    onBack,
  }: {
    line: Schemas["ContainerLine"];
    /** Opens Settings, Pot sizes, where the cost of potting is set. */
    onPotSizes?: () => void;
    /** Opens Purchases, where a purchase's cost comes from, while Purchases is on. */
    onPurchases?: () => void;
    onBack: () => void;
  } = $props();

  // Each form names the entry it replaces, if any, so nobody's answer is overwritten unseen.
  type Form =
    | { kind: "material"; entry: CostEntry | null }
    | { kind: "vat"; entry: CostEntry }
    | { kind: "other"; entry: CostEntry | null };

  let costing = $state<Costing | null>(null);
  let problem = $state("");
  let formProblem = $state("");
  let said = $state("");
  let busy = $state(false);
  let form = $state<Form | null>(null);
  // Made when a form opens, so pressing save again after a lost reply doesn't store the cost twice.
  let formId = "";
  let opener: HTMLElement | null = null;
  let amount = $state("");
  let nothing = $state(false);
  let note = $state("");
  let band = $state<Schemas["VatBand"]>("Standard");
  let includesVat = $state(true);
  let formHeading = $state<HTMLHeadingElement>();
  let costHeading = $state<HTMLHeadingElement>();

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
    formProblem = "";
    opener = document.activeElement as HTMLElement | null;
    formId = crypto.randomUUID();
    form = next;
    nothing = false;
    note = next.kind === "other" ? (next.entry?.note ?? "") : "";
    amount =
      next.kind !== "vat" && next.entry?.answer === "Amount"
        ? String(Number(next.entry.amountPaid))
        : "";

    if (next.kind === "vat") {
      band = next.entry.vatBand ?? "Standard";
      includesVat = next.entry.includesVat ?? true;
    }

    await tick();
    formHeading?.focus();
  }

  async function close() {
    form = null;
    await tick();
    (opener?.isConnected ? opener : costHeading)?.focus();
  }

  async function send(
    body: Omit<Schemas["CostRequest"], "eventId" | "occurredAt">,
    done: string | ((after: Costing) => string),
    eventId = formId,
  ) {
    busy = true;

    try {
      const { response } = await api.POST("/api/batches/{id}/costs", {
        params: { path: { id: line.batchId } },
        body: { ...body, eventId, occurredAt: new Date().toISOString() },
      });

      if (!response.ok) {
        const stale = response.status === 409;
        problem = stale
          ? "Someone changed that cost in the meantime. Here is how it stands now."
          : "That cost couldn't be saved. Please check it and try again.";

        if (stale) {
          form = null;
          await load();
          await tick();
          costHeading?.focus();
        }

        return;
      }

      form = null;
      await load();
      said = costing
        ? typeof done === "string"
          ? done
          : done(costing)
        : "Saved.";
      await tick();
      costHeading?.focus();
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      busy = false;
    }
  }

  /** What VAT the owner said, carried over when only the amount changes. */
  const saidVat = (entry: CostEntry | null) =>
    entry?.vatSaid && entry.answer === "Amount"
      ? { vatBand: entry.vatBand, includesVat: entry.includesVat }
      : {};

  function saveAmount(event: SubmitEvent) {
    event.preventDefault();
    const paid = nothing ? 0 : readMoney(amount);

    if (paid === null || (form?.kind === "other" && paid === 0)) {
      formProblem = moneyProblem;
      return;
    }

    formProblem = "";
    const entry = form?.kind === "vat" ? null : (form?.entry ?? null);

    if (form?.kind === "other") {
      void send(
        {
          kind: "Other",
          answer: "Amount",
          amount: paid,
          note,
          replacesId: entry?.id ?? null,
          ...saidVat(entry),
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
        replacesId: entry?.id ?? null,
        ...(paid === 0 ? {} : saidVat(entry)),
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
    // The server keeps the day the cost was for, and so that day's rate.
    void send(
      {
        kind: entry.kind,
        answer: "Amount",
        amount: Number(entry.amountPaid),
        note: entry.note,
        vatBand: band,
        includesVat: band === "Zero" ? true : includesVat,
        replacesId: entry.id,
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
      `You've said nobody knows what the ${what} cost.`,
      crypto.randomUUID(),
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
        crypto.randomUUID(),
      );
    }
  }
</script>

{#snippet amounts(cost: MoneyPair)}
  {#each pair(cost) as figure, index (index)}
    <td class="cost-amount" class:counts={index === 0}
      >{#if index === 0}<strong>{money(figure)}</strong>{:else}{money(
          figure,
        )}{/if}</td
    >
  {/each}
{/snippet}

{#snippet entryButtons(entry: CostEntry)}
  <div class="row">
    <button
      type="button"
      class="secondary"
      onclick={() =>
        open(
          entry.kind === "Other"
            ? { kind: "other", entry }
            : { kind: "material", entry },
        )}><Pencil size={16} aria-hidden="true" /> Change the cost</button
    >
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

{#snippet amountProblem()}
  {#if formProblem}<p class="problem" role="alert" id="cost-amount-problem">
      {formProblem}
    </p>{/if}
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
      <h2
        id="cost-heading"
        class="form-heading"
        tabindex="-1"
        bind:this={costHeading}
      >
        What it cost
      </h2>
      <p class="muted">{countsSentence(costing.vatRegistered)}</p>

      {#if material.state === "NoAnswer"}
        <div class="card caution stack tight">
          <p>{materialSentence(costing)}</p>
          <div class="row">
            <button
              type="button"
              onclick={() => open({ kind: "material", entry: null })}
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
            <!-- Which column counts is said in words, not left to bold type. -->
            {#if withFirst}
              <th scope="col" class="counts"
                >With VAT<span class="visually-hidden"
                  >, the figures that count</span
                ></th
              >
              <th scope="col">Before VAT</th>
            {:else}
              <th scope="col" class="counts"
                >Before VAT<span class="visually-hidden"
                  >, the figures that count</span
                ></th
              >
              <th scope="col">With VAT</th>
            {/if}
          </tr>
        </thead>
        <tbody>
          <tr>
            <th scope="row" id="cost-material"
              >{rowName(null, costing.source)}</th
            >
            {#if material.cost}
              {@render amounts(material.cost)}
            {:else}
              <td colspan="2"
                >{material.state === "NotKnown"
                  ? "Not known"
                  : "No answer yet"}</td
              >
            {/if}
          </tr>
          {#if material.state !== "NoAnswer"}
            <tr class="cost-said">
              <td colspan="3" headers="cost-material">
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
                      onclick={() =>
                        open({ kind: "material", entry: material.entry })}
                      ><Pencil size={16} aria-hidden="true" /> Change the cost</button
                    >
                  {/if}
                {/if}
              </td>
            </tr>
          {/if}
          {#if costing.pots}
            <tr>
              <th scope="row" id="cost-pots">Pots and compost</th>
              {#if costing.pots.lines.length > 0}
                {@render amounts(costing.pots.cost)}
              {:else}
                <!-- Nothing could be charged, which isn't the same as nothing. -->
                <td colspan="2">Not counted</td>
              {/if}
            </tr>
            <tr class="cost-said">
              <td colspan="3" headers="cost-pots">
                {#each potsSentences(costing.pots) as sentence, index (index)}
                  <p>{sentence}</p>
                {/each}
                {#if costing.pots.withoutAllowance.length > 0 && onPotSizes}
                  <button type="button" class="secondary" onclick={onPotSizes}
                    ><ArrowRight size={16} aria-hidden="true" /> Go to Pot sizes</button
                  >
                {/if}
              </td>
            </tr>
          {/if}
          {#each costing.others as entry (entry.id)}
            <tr>
              <th scope="row" id="cost-{entry.id}"
                >{rowName(entry, costing.source)}</th
              >
              {#if entry.cost}{@render amounts(entry.cost)}{/if}
            </tr>
            <tr class="cost-said">
              <td colspan="3" headers="cost-{entry.id}">
                <p>{otherSentence(entry, costing.history)}</p>
                {@render entryButtons(entry)}
              </td>
            </tr>
          {/each}
          {#if costing.total}
            <tr class="cost-total">
              <th scope="row">Altogether</th>
              {@render amounts(costing.total)}
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
            /> None. A seller who isn't registered for VAT doesn't charge any.</label
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
          <button type="button" class="secondary" onclick={close}>Cancel</button
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
            aria-invalid={formProblem ? "true" : undefined}
            aria-describedby={formProblem ? "cost-amount-problem" : undefined}
          /></label
        >
        {@render amountProblem()}
        {#if form.kind === "material"}
          <label class="tick"
            ><input type="checkbox" bind:checked={nothing} />
            {costing.source === "BoughtIn"
              ? "They cost nothing, because they were a gift."
              : "It cost nothing, because it was our own seed or a gift."}</label
          >
        {/if}
        <div class="form-actions">
          <button type="submit" disabled={busy}
            ><Check size={18} aria-hidden="true" />
            {form.kind === "other" && !form.entry
              ? "Add this cost"
              : "Save the cost"}</button
          >
          <button type="button" class="secondary" onclick={close}>Cancel</button
          >
        </div>
      </form>
    {/if}

    {@const perPlant = perPlantSentences(costing)}
    {#if perPlant.length > 0}
      <section class="stack tight" aria-labelledby="per-plant-heading">
        <h2 id="per-plant-heading" class="form-heading">
          What each plant has cost
        </h2>
        {#each perPlant as sentence, index (index)}
          <p>{sentence}</p>
        {/each}
      </section>
    {/if}

    {#if replaced.length > 0}
      <section class="stack tight" aria-labelledby="earlier-heading">
        <h2 id="earlier-heading" class="form-heading">Earlier answers</h2>
        <ul class="plain-list stack tight">
          {#each replaced as entry (entry.id)}
            {@const parts = replacedParts(entry, costing.history)}
            <li class="muted">
              {parts.lead}{#if parts.struck}<s>{parts.struck}</s
                >{/if}{parts.rest}
            </li>
          {/each}
        </ul>
      </section>
    {/if}

    <p class="muted">
      These figures count what went into the plants: the {what}, pots and
      compost, and anything else added here. They leave out wages, heating and
      rent.
    </p>
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
