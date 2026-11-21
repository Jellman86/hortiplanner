<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import PoundSterling from "@lucide/svelte/icons/pound-sterling";
  import { tick } from "svelte";
  import { api } from "../../api/client";
  import { aboutMoney, moneyProblem, readMoney } from "../../lib/costs";
  import type { Schemas } from "../../lib/types";

  // What it costs to pot one plant into a size, under Settings, Pot sizes: the owner's alone.
  let {
    size,
    allowance,
    standardRate,
    onSaved,
  }: {
    size: { id: string; name: string };
    /** Undefined when the amounts couldn't be fetched. */
    allowance: Schemas["PotAllowance"] | undefined;
    /** Today's standard rate, which a new amount is worked out at. */
    standardRate: number;
    onSaved: () => Promise<void>;
  } = $props();

  const mostEach = 999.99;
  const tooMuch =
    "That's more than Hortiplanner can take for one plant. Please put in £999.99 or less.";

  let editing = $state(false);
  let amount = $state("");
  let beforeVat = $state(false);
  let problem = $state("");
  let said = $state("");
  let busy = $state(false);
  let box = $state<HTMLInputElement>();
  let opener = $state<HTMLButtonElement>();

  const each = $derived(
    allowance?.each
      ? {
          withVat: Number(allowance.each.withVat),
          beforeVat: Number(allowance.each.beforeVat),
        }
      : null,
  );

  /** The amount typed, or null for none: nought is taken as none, as for seed trays. */
  const typed = $derived(amount.trim() ? readMoney(amount) : 0);

  /** "That's £0.35 before VAT at 20%." for what's typed, as it's typed. */
  const preview = $derived.by(() => {
    if (!typed || typed > mostEach) {
      return "";
    }

    const factor = 1 + standardRate / 100;
    return beforeVat
      ? `That's ${aboutMoney(typed * factor)} with VAT at ${standardRate}%.`
      : `That's ${aboutMoney(typed / factor)} before VAT at ${standardRate}%.`;
  });

  const describedBy = $derived(
    [
      problem ? `allowance-problem-${size.id}` : "",
      preview ? `allowance-preview-${size.id}` : "",
      `allowance-help-${size.id}`,
    ]
      .filter(Boolean)
      .join(" "),
  );

  async function open() {
    said = "";
    problem = "";
    amount = allowance?.costEach ? Number(allowance.costEach).toFixed(2) : "";
    beforeVat = allowance?.costEach ? !allowance.includesVat : false;
    editing = true;
    await tick();
    box?.focus();
  }

  /** The form closes and the focus goes back to the button that opened it. */
  async function close() {
    editing = false;
    await tick();
    opener?.focus();
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();

    if (typed === null) {
      problem = moneyProblem;
      box?.focus();
      return;
    }

    if (typed > mostEach) {
      problem = tooMuch;
      box?.focus();
      return;
    }

    busy = true;
    problem = "";

    try {
      const costEach = typed || null;
      const { data, response } = await api.PUT(
        "/api/pot-sizes/{id}/allowance",
        {
          params: { path: { id: size.id } },
          body: { costEach, includesVat: !beforeVat },
        },
      );

      if (!response.ok || !data) {
        problem =
          "The cost of potting couldn't be saved. Please try again in a minute.";
        return;
      }

      said = !data.changed
        ? "That's already the amount in use, so nothing has changed."
        : costEach === null
          ? data.reachesBack
            ? `Nothing is charged for potting into ${size.name}, including potting already recorded.`
            : `Nothing is charged for potting into ${size.name} from today. Potting recorded before today keeps the amount it had.`
          : data.first
            ? `As this is the first amount for ${size.name}, it's used for potting already recorded as well.`
            : data.reachesBack
              ? `As ${size.name} had no amount before today, the new amount is used for potting already recorded as well.`
              : "The new amount is used for potting from today, including any recorded earlier today. Potting before today keeps the amount it had.";
      await onSaved();
      await close();
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
          oninput={() => (problem = "")}
          inputmode="decimal"
          autocomplete="off"
          aria-invalid={problem === moneyProblem || problem === tooMuch
            ? "true"
            : undefined}
          aria-describedby={describedBy}
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
        ><input
          type="checkbox"
          bind:checked={beforeVat}
          aria-describedby={preview
            ? `allowance-preview-${size.id}`
            : undefined}
        /> This amount is before VAT.</label
      >
      <div class="row">
        <button type="submit" disabled={busy}
          ><Check size={16} aria-hidden="true" /> Save the cost of potting</button
        >
        <button type="button" class="secondary" onclick={close}>Cancel</button>
      </div>
    </form>
  {:else if allowance === undefined}
    <span class="muted">The cost of potting couldn't be fetched just now.</span>
  {:else}
    <span class="muted">
      {each
        ? `It costs ${aboutMoney(each.withVat)} with VAT to pot one plant into this size, which is ${aboutMoney(each.beforeVat)} before VAT.`
        : allowance.set
          ? "Nothing is charged for potting into this size at the moment."
          : "Nobody has said what it costs to pot a plant into this size yet."}
    </span>
    <button
      type="button"
      class="link-button"
      bind:this={opener}
      aria-label={each
        ? `Change the cost of potting into ${size.name}`
        : `Set the cost of potting into ${size.name}`}
      onclick={open}
      ><PoundSterling size={16} aria-hidden="true" />{each
        ? "Change the cost of potting"
        : "Set the cost of potting"}</button
    >
  {/if}
  {#if said}<p class="card done" role="status">{said}</p>{/if}
</div>
