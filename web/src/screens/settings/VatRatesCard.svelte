<script lang="ts">
  import Check from "@lucide/svelte/icons/check";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";
  import { rateHistory } from "../../lib/vatRates";

  // The VAT rates, under Settings, Nursery details. A change is used from today: invoices already issued keep the
  // rate they were worked out with.
  let { vatRegistered }: { vatRegistered: boolean } = $props();

  type Rates = Schemas["VatRatesSummary"];

  let rates = $state<Rates | null>(null);
  let standard = $state("");
  let reduced = $state("");
  let confirming = $state(false);
  let busy = $state(false);
  let problem = $state("");
  let saved = $state("");
  let saveButton = $state<HTMLButtonElement>();
  let question = $state<HTMLDivElement>();

  const notFetched =
    "The VAT rates couldn't be fetched just now. Please try again in a minute.";

  async function load() {
    const { data } = await api.GET("/api/settings/vat-rates");

    if (!data) {
      // Nothing is guessed: without the rates, the card says so rather than showing 20 and 5.
      problem = notFetched;
      return;
    }

    rates = data;
    standard = String(Number(data.standard));
    reduced = String(Number(data.reduced));
  }

  onMount(() => attempt(load, () => (problem = notFetched)));

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

  const asked = $derived(
    changes.length === 1
      ? `Change the ${changes[0].band} rate from ${changes[0].from}% to ${changes[0].to}%? It will be used from today, and nothing already recorded changes.`
      : `Change the standard rate from ${changes[0]?.from}% to ${changes[0]?.to}% and the reduced rate from ${changes[1]?.from}% to ${changes[1]?.to}%? They will be used from today, and nothing already recorded changes.`,
  );

  const history = $derived(rates ? rateHistory(rates.changes) : []);

  // Back to the Save button once the question has gone, so the keyboard isn't sent to the top of the page.
  async function backToSave() {
    confirming = false;
    await tick();
    saveButton?.focus();
  }

  async function ask(event: SubmitEvent) {
    event.preventDefault();
    problem = "";
    saved = "";

    if (!rates) {
      problem = notFetched;
      return;
    }

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
    await tick();
    question?.focus();
  }

  async function keep() {
    standard = String(Number(rates?.standard));
    reduced = String(Number(rates?.reduced));
    await backToSave();
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
              "The VAT rates couldn't be saved. Please check them and try again.";
            return;
          }

          saved =
            said.length === 1
              ? `The ${said[0].band} rate is ${said[0].to}% from today.`
              : "The new VAT rates are used from today.";
          await load();
          await backToSave();
        } finally {
          busy = false;
        }
      },
      (message) => (problem = message),
    );
</script>

<form class="form-card stack" onsubmit={ask}>
  <h2 class="form-heading">VAT rates</h2>
  <p class="muted">
    {vatRegistered
      ? "These are used to work out the VAT on invoices."
      : "The nursery isn't registered for VAT, so invoices don't charge it. These rates are kept ready for when it is."}
  </p>
  <label
    >Standard rate (%) <input
      bind:value={standard}
      inputmode="decimal"
      disabled={confirming || !rates}
      required
    /></label
  >
  <label
    >Reduced rate (%) <input
      bind:value={reduced}
      inputmode="decimal"
      disabled={confirming || !rates}
      required
    /></label
  >
  <p class="muted">
    The zero rate is always 0%, so there's nothing to set for it.
  </p>

  {#if confirming}
    <!-- A question with its two answers: the question is read when it appears, and Tab reaches the answers. -->
    <div
      class="card caution stack tight"
      role="group"
      aria-labelledby="vat-rates-question"
      tabindex="-1"
      bind:this={question}
    >
      <span id="vat-rates-question">{asked}</span>
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
    <button type="submit" bind:this={saveButton} disabled={!rates}
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
      {#each history as sentence, index (index)}
        <li class="muted">{sentence}</li>
      {/each}
    </ul>
  {/if}
</form>
