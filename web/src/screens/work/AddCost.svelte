<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import {
    costWork,
    materialWord,
    moneyProblem,
    readMoney,
  } from "../../lib/costs";
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";

  // What a batch's seed or plants cost, asked of anyone at the tray. Kept on the phone without a signal, and the
  // amount isn't shown again afterwards to anyone but the owner.
  let {
    line,
    onSaved,
    onCancel,
  }: {
    line: Schemas["ContainerLine"];
    onSaved: (message: string) => void;
    onCancel: () => void;
  } = $props();

  let amount = $state("");
  let nothing = $state(false);
  let problem = $state("");
  let busy = $state(false);

  const what = $derived(materialWord(line.source));
  const plants = $derived(line.source === "BoughtIn");

  async function save(event: SubmitEvent) {
    event.preventDefault();
    problem = "";
    const paid = nothing ? 0 : readMoney(amount);

    if (paid === null) {
      problem = moneyProblem;
      return;
    }

    busy = true;
    // Nought is taken as it costing nothing.
    const outcome = await recordWork(
      costWork(line.batchId, {
        kind: "Material",
        answer: paid === 0 ? "Free" : "Amount",
        amount: paid === 0 ? null : paid,
      }),
    );
    busy = false;

    if (isRecorded(outcome)) {
      onSaved(
        outcome.kind === "queued"
          ? "The cost is kept on this device, and it will be sent when there's a signal."
          : "The cost is saved.",
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="What did it cost?" {problem} {onCancel}>
  <p class="action-for">
    <strong>Batch {line.traceabilityCode}</strong>
    {plantName(line.commonName, line.variety)}
  </p>
  <form class="stack" onsubmit={save}>
    <label
      >{plants
        ? "What did these plants cost altogether? (£)"
        : `What did this ${what} cost? (£)`}
      <input
        bind:value={amount}
        inputmode="decimal"
        autocomplete="off"
        disabled={nothing}
        aria-describedby="cost-help"
      /></label
    >
    <p class="muted" id="cost-help">
      {plants
        ? "Put in what was paid for all of them, not for one of them, with any VAT included."
        : "Put in what was paid for the seed that went into this tray, with any VAT included, as it's printed on the packet or the receipt. If only part of a packet went in, put in roughly that part of its price."}
    </p>
    <label class="tick"
      ><input type="checkbox" bind:checked={nothing} />
      {plants
        ? "They cost nothing, because they were a gift."
        : "It cost nothing, because it was our own seed or a gift."}</label
    >
    <button type="submit" disabled={busy}
      ><Check size={18} aria-hidden="true" /> Save the cost</button
    >
  </form>
</ActionFrame>
