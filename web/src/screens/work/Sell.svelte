<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import { api } from "../../api/client";
  import { moneyProblem, readMoney } from "../../lib/costs";
  import { describeWaiting, recordWork } from "../../lib/queue";
  import { quantityPresets } from "../../lib/tray";
  import type { Schemas } from "../../lib/types";
  import {
    countedSinceNote,
    isRecorded,
    outcomeProblem,
    plantName,
    work,
  } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import BatchChoice from "./BatchChoice.svelte";
  import Stepper from "./Stepper.svelte";

  // Plants sold from a tray, recorded by hand, with what each went for. Work like any other, so it waits on a
  // phone without a signal and can be undone.
  let {
    container,
    when,
    owner = false,
    onDone,
    onCancel,
  }: {
    container: Schemas["ContainerResponse"];
    /** When the sale was, if not just now. */
    when?: string;
    /** The owner is told the day's till total still goes in under Money. */
    owner?: boolean;
    onDone: (message: string, eventId: string) => void;
    onCancel: () => void;
  } = $props();

  let chosen = $state<string | null>(null);
  let quantity = $state(1);
  let price = $state("");
  let listed = $state<number | null>(null);
  let problem = $state("");
  let busy = $state(false);
  let priceBox = $state<HTMLInputElement>();

  const line = $derived(
    container.contents.find((item) => item.batchId === chosen) ??
      container.contents[0],
  );

  /** The plant list's price for this plant in this tray's pot size, if there is one. */
  async function lookUpPrice(plantId: string) {
    listed = null;

    try {
      const plants = (await api.GET("/api/plants")).data ?? [];
      const found = plants
        .find((plant) => plant.id === plantId)
        ?.prices.find((item) => item.potSizeId === container.potSizeId);
      listed = found ? Number(found.price) : null;
    } catch {
      // Without a signal there's no list to read, so the price is typed.
    }

    price = listed === null ? "" : listed.toFixed(2);
  }

  // Looked up again when a mixed tray's other batch is chosen.
  $effect(() => {
    void lookUpPrice(line.plantId);
  });

  async function record(event: SubmitEvent) {
    event.preventDefault();
    const each = price.trim() ? readMoney(price) : null;

    if (price.trim() && each === null) {
      problem = moneyProblem;
      priceBox?.focus();
      return;
    }

    busy = true;
    problem = "";
    const item = work.sell(
      container.id,
      { batchId: line.batchId, quantity, priceEach: each },
      when,
    );
    const outcome = await recordWork(item);
    busy = false;
    const what = `${quantity} ${plantName(line.commonName, line.variety)}`;

    if (outcome.kind === "queued") {
      describeWaiting(
        outcome.eventId,
        `Sold ${what} from ${container.labelCode}`,
      );
    }

    if (isRecorded(outcome)) {
      onDone(
        `Recorded ${what} as sold.${countedSinceNote(outcome)}`,
        outcome.eventId,
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="Sold" {problem} {onCancel} {container}>
  {#if line.notCounted}
    <p>
      This tray hasn't been counted yet, so please count it before recording a
      sale.
    </p>
  {:else}
    <form class="stack" onsubmit={record}>
      <BatchChoice lines={container.contents} bind:selected={chosen} />
      <p>How many were sold from this tray?</p>
      <Stepper
        bind:value={quantity}
        max={Number(line.quantity)}
        presets={quantityPresets(Number(line.quantity))}
      />
      <label
        >Price for each one (£)
        <input
          bind:this={priceBox}
          bind:value={price}
          oninput={() => (problem = "")}
          inputmode="decimal"
          autocomplete="off"
          aria-describedby="sale-price-help"
          aria-invalid={problem === moneyProblem ? "true" : undefined}
        /></label
      >
      <p class="muted" id="sale-price-help">
        {listed === null
          ? "There's no price on the plant list for this pot size. Put in what each one sold for, or leave it empty if you don't know."
          : `This is the price on the plant list${container.potSizeName ? ` for ${container.potSizeName} pots` : ""}. Change it if they went for something different.`}
      </p>
      {#if owner}
        <p class="muted">
          This takes the plants off the tray. The day's till total still goes in
          under Money.
        </p>
      {/if}
      <button type="submit" disabled={busy}
        ><Check size={18} aria-hidden="true" /> Record the sale</button
      >
    </form>
  {/if}
</ActionFrame>
