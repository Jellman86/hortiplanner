<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import PoundSterling from "@lucide/svelte/icons/pound-sterling";
  import { tick } from "svelte";
  import { api } from "../../api/client";
  import { moneyProblem, readMoney } from "../../lib/costs";
  import type { Schemas } from "../../lib/types";
  import { money } from "../../lib/work";

  // What it costs to pot one plant into a size, under Settings, Pot sizes: the owner's alone.
  let {
    size,
    allowance,
    standardRate,
    onSaved,
  }: {
    size: { id: string; name: string };
    allowance: Schemas["PotAllowance"] | undefined;
    /** Today's standard rate, which a new amount is worked out at. */
    standardRate: number;
    onSaved: () => Promise<void>;
  } = $props();

  let editing = $state(false);
  let amount = $state("");
  let beforeVat = $state(false);
  let problem = $state("");
  let said = $state("");
  let busy = $state(false);
  let box = $state<HTMLInputElement>();

  const each = $derived(
    allowance?.each
      ? {
          withVat: Number(allowance.each.withVat),
          beforeVat: Number(allowance.each.beforeVat),
        }
      : null,
  );

  /** "That's £0.35 before VAT at 20%." for what's typed, as it's typed. */
  const preview = $derived.by(() => {
    const paid = amount.trim() ? readMoney(amount) : null;

    if (!paid) {
      return "";
    }

    const factor = 1 + standardRate / 100;
    return beforeVat
      ? `That's ${money(paid * factor)} with VAT at ${standardRate}%.`
      : `That's ${money(paid / factor)} before VAT at ${standardRate}%.`;
  });

  async function open() {
    said = "";
    problem = "";
    amount = allowance?.costEach ? String(Number(allowance.costEach)) : "";
    beforeVat = allowance ? !allowance.includesVat : false;
    editing = true;
    await tick();
    box?.focus();
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    const paid = amount.trim() ? readMoney(amount) : null;

    if (amount.trim() && !paid) {
      problem = moneyProblem;
      box?.focus();
      return;
    }

    busy = true;
    problem = "";

    try {
      const { data, response } = await api.PUT(
        "/api/pot-sizes/{id}/allowance",
        {
          params: { path: { id: size.id } },
          body: { costEach: paid, includesVat: !beforeVat },
        },
      );

      if (!response.ok || !data) {
        problem =
          "The cost of potting couldn't be saved. Please try again in a minute.";
        return;
      }

      said = !data.changed
        ? "That's already the amount in use, so nothing has changed."
        : data.first
          ? `As this is the first amount for ${size.name}, it's used for potting already recorded as well.`
          : "The new amount is used for potting from today. Potting already recorded keeps the amount it had.";
      editing = false;
      await onSaved();
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      busy = false;
    }
  }
</script>

<div class="stack tight pot-allowance">
  {#if editing}
    <form class="stack tight" onsubmit={save}>
      <label
        >What it costs to pot one plant into this size, with VAT (£, optional)
        <input
          bind:this={box}
          bind:value={amount}
          inputmode="decimal"
          autocomplete="off"
          aria-invalid={problem === moneyProblem ? "true" : undefined}
          aria-describedby={`allowance-help-${size.id}${problem ? ` allowance-problem-${size.id}` : ""}${preview ? ` allowance-preview-${size.id}` : ""}`}
        /></label
      >
      {#if problem}<p
          class="problem"
          role="alert"
          id="allowance-problem-{size.id}"
        >
          {problem}
        </p>{/if}
      {#if preview}<p class="muted" id="allowance-preview-{size.id}">
          {preview}
        </p>{/if}
      <p class="muted" id="allowance-help-{size.id}">
        Count the pot, the compost that fills it and the label. Each time plants
        are pricked out or potted on into this size, their batch is charged this
        much for each plant. Leave it empty for seed trays.
      </p>
      <label class="tick"
        ><input type="checkbox" bind:checked={beforeVat} /> This amount is before
        VAT.</label
      >
      <div class="row">
        <button type="submit" disabled={busy}
          ><Check size={16} aria-hidden="true" /> Save the cost of potting</button
        >
        <button
          type="button"
          class="secondary"
          onclick={() => (editing = false)}>Cancel</button
        >
      </div>
    </form>
  {:else}
    <span class="muted">
      {each
        ? `Potting one plant into it costs ${money(each.withVat)} with VAT, which is ${money(each.beforeVat)} before VAT.`
        : "There's no cost of potting into it yet."}
    </span>
    <button type="button" class="link-button" onclick={open}
      ><PoundSterling size={16} aria-hidden="true" />{each
        ? "Change the cost of potting"
        : "Set the cost of potting"}</button
    >
  {/if}
  {#if said}<p class="card done" role="status">{said}</p>{/if}
</div>
