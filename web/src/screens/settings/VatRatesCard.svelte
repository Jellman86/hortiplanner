<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { addDays, longDay } from "../../lib/days";
  import { attempt, type Schemas } from "../../lib/types";

  // The VAT rates, under Settings, Nursery details. A change is used from today: invoices already issued keep the
  // rate they were worked out with.
  let { vatRegistered }: { vatRegistered: boolean } = $props();

  type Rates = Schemas["VatRatesSummary"];
  type Change = Schemas["VatRateHistory"];

  let rates = $state<Rates | null>(null);
  let standard = $state("");
  let reduced = $state("");
  let confirming = $state(false);
  let busy = $state(false);
  let problem = $state("");
  let saved = $state("");

  const bandName = (band: string) =>
    band === "Standard" ? "standard" : "reduced";
  async function load() {
    rates = (await api.GET("/api/settings/vat-rates")).data ?? null;
    standard = String(Number(rates?.standard ?? 20));
    reduced = String(Number(rates?.reduced ?? 5));
  }

  onMount(() => void attempt(load, (message) => (problem = message)));

  const isRate = (text: string) =>
    /^\d{1,3}(\.\d{1,2})?$/.test(text.trim()) && Number(text) <= 100;

  // What would change, in words, for the question before saving.
  const changes = $derived.by(() => {
    if (!rates || !isRate(standard) || !isRate(reduced)) {
      return [];
    }

    return [
      { band: "standard", from: Number(rates.standard), to: Number(standard) },
      { band: "reduced", from: Number(rates.reduced), to: Number(reduced) },
    ].filter((change) => change.from !== change.to);
  });

  const question = $derived(
    changes.length === 1
      ? `Change the ${changes[0].band} rate from ${changes[0].from}% to ${changes[0].to}%? It will be used from today, and nothing already recorded changes.`
      : `Change the standard rate from ${changes[0]?.from}% to ${changes[0]?.to}% and the reduced rate from ${changes[1]?.from}% to ${changes[1]?.to}%? They will be used from today, and nothing already recorded changes.`,
  );

  // Each change after the starting rates, as a sentence: what it was until when, and who changed it to what.
  const history = $derived.by(() => {
    if (!rates) {
      return [];
    }

    const all = rates.changes as Change[];
    return all
      .map((change, index) => ({ change, index }))
      .filter(({ change }) => change.setBy)
      .map(({ change, index }) => {
        const before = all
          .slice(0, index)
          .filter((earlier) => earlier.band === change.band)
          .at(-1);
        const name = bandName(change.band);
        return before
          ? `The ${name} rate was ${Number(before.percent)}% until ${longDay(addDays(change.from, -1))}. ${change.setBy} changed it to ${Number(change.percent)}% on ${longDay(change.from)}.`
          : `${change.setBy} set the ${name} rate to ${Number(change.percent)}% on ${longDay(change.from)}.`;
      })
      .reverse();
  });

  function ask(event: SubmitEvent) {
    event.preventDefault();
    problem = "";
    saved = "";

    if (!isRate(standard) || !isRate(reduced)) {
      problem =
        "Please put the rate in as a percentage between 0 and 100, such as 20 or 17.5.";
      return;
    }

    if (Number(reduced) > Number(standard)) {
      problem = "The reduced rate can't be higher than the standard rate.";
      return;
    }

    if (changes.length === 0) {
      saved = "Those are already the rates in use, so nothing has changed.";
      return;
    }

    confirming = true;
  }

  function keep() {
    confirming = false;
    standard = String(Number(rates?.standard ?? 20));
    reduced = String(Number(rates?.reduced ?? 5));
  }

  const save = () =>
    attempt(
      async () => {
        busy = true;

        try {
          const said = changes;
          const { response } = await api.PUT("/api/settings/vat-rates", {
            body: { standard: Number(standard), reduced: Number(reduced) },
          });

          if (!response.ok) {
            problem =
              "Please put the rate in as a percentage between 0 and 100, such as 20 or 17.5.";
            return;
          }

          confirming = false;
          saved =
            said.length === 1
              ? `The ${said[0].band} rate is ${said[0].to}% from today.`
              : "The new VAT rates are used from today.";
          await load();
        } finally {
          busy = false;
        }
      },
      (message) => (problem = message),
    );
</script>

<form class="form-card stack" onsubmit={ask}>
  <h2 class="form-heading">VAT rates</h2>
  <p class="muted">These are used to work out the VAT on invoices.</p>
  {#if !vatRegistered}
    <p class="muted">
      The nursery isn't registered for VAT, so invoices don't charge it.
    </p>
  {/if}
  <label
    >Standard rate (%) <input
      bind:value={standard}
      inputmode="decimal"
      disabled={confirming}
      required
    /></label
  >
  <label
    >Reduced rate (%) <input
      bind:value={reduced}
      inputmode="decimal"
      disabled={confirming}
      required
    /></label
  >
  <p class="muted">
    The zero rate is always 0%, so there's nothing to set for it.
  </p>

  {#if confirming}
    <div class="card caution stack tight" role="alert">
      <span>{question}</span>
      <div class="row">
        <button type="button" disabled={busy} onclick={save}
          ><Check size={18} aria-hidden="true" />
          {changes.length === 1
            ? "Change the rate"
            : "Change the rates"}</button
        >
        <button type="button" class="secondary" onclick={keep}
          >{changes.length === 1
            ? `Keep it at ${changes[0].from}%`
            : "Keep the rates as they are"}</button
        >
      </div>
    </div>
  {:else}
    <button type="submit"
      ><Check size={18} aria-hidden="true" /> Save the VAT rates</button
    >
  {/if}
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}
  <p class="muted">
    A new rate is used from today. Invoices already issued keep the rate they
    were worked out with.
  </p>

  {#if history.length > 0}
    <h3 class="form-heading">Earlier rates</h3>
    <ul class="plain-list stack tight">
      {#each history as sentence (sentence)}
        <li class="muted">{sentence}</li>
      {/each}
    </ul>
  {/if}
</form>
