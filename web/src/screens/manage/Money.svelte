<script lang="ts">
  import FileDown from "@lucide/svelte/icons/file-down";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { isOn } from "../../lib/features.svelte";
  import { isoDay } from "../../lib/days";
  import { onMount } from "svelte";
  import DataTable from "../DataTable.svelte";
  import DayPicker from "../DayPicker.svelte";
  import FormPanel from "../FormPanel.svelte";
  import type { Column } from "../../lib/table";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { attempt, type Schemas } from "../../lib/types";
  import { money } from "../../lib/work";

  let { onBack }: { onBack: () => void } = $props();

  const today = () => isoDay(new Date());

  const monthName = (month: string) =>
    new Date(`${month}-01T12:00:00Z`).toLocaleDateString("en-GB", {
      month: "long",
      year: "numeric",
    });

  let takings = $state<Schemas["TakingResponse"][]>([]);
  let summary = $state<Schemas["MoneySummary"] | null>(null);
  let takenOn = $state(today());
  let amount = $state("");
  let notes = $state("");
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    takings = (await api.GET("/api/money/takings")).data ?? [];
    summary = (await api.GET("/api/money/summary")).data ?? null;
  }

  onMount(() => attempt(load, setProblem));

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/money/takings", {
        body: { takenOn, amount: Number(amount), notes: notes || null },
      });

      if (response.ok) {
        amount = "";
        notes = "";
        await load();
      } else {
        problem =
          response.status === 409
            ? "Takings are already entered for that day. Void them first if you need to enter them again."
            : "Please check the date and amount.";
      }
    }, setProblem);
  };

  type Taking = Schemas["TakingResponse"];
  type Month = Schemas["MoneySummary"]["months"][number];

  // On a desk, takings and months are tables beside the form, sorted by any column.
  const takingColumns: Column<Taking>[] = [
    {
      label: "Day",
      value: (taking) => taking.takenOn,
      text: (taking) => dayLabel(taking.takenOn),
    },
    {
      label: "Taken",
      value: (taking) => Number(taking.amount),
      text: (taking) => money(Number(taking.amount)),
      numeric: true,
    },
    { label: "Notes", value: (taking) => taking.notes ?? "" },
  ];

  const monthColumns: Column<Month>[] = [
    {
      label: "Month",
      value: (month) => month.month,
      text: (month) => monthName(month.month),
    },
    {
      label: "Income",
      value: (month) => Number(month.income),
      text: (month) => money(Number(month.income)),
      numeric: true,
    },
    {
      label: "Spending",
      value: (month) => Number(month.spending),
      text: (month) => money(Number(month.spending)),
      numeric: true,
    },
    {
      label: "Difference",
      value: (month) => Number(month.income) - Number(month.spending),
      text: (month) => money(Number(month.income) - Number(month.spending)),
      numeric: true,
    },
  ];

  const voidTaking = (taking: Schemas["TakingResponse"]) => {
    if (!confirm(`Void the takings for ${dayLabel(taking.takenOn)}?`)) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/money/takings/{id}/void", {
        params: { path: { id: taking.id } },
      });
      await load();
    }, setProblem);
  };
</script>

{#snippet takingActions(taking: Taking)}
  <button class="link-button danger-link" onclick={() => voidTaking(taking)}
    ><Trash2 size={16} aria-hidden="true" />Void</button
  >
{/snippet}

<main>
  <h1>Money</h1>

  <div class="split wide-list">
    <section>
      <FormPanel label="Add a day's takings">
        <form class="form-card stack" onsubmit={save}>
          <h2 class="form-heading">Add a day's takings</h2>
          <DayPicker bind:value={takenOn} label="Date" />
          <label
            >Amount taken (£) <input
              bind:value={amount}
              inputmode="decimal"
              required
            /></label
          >
          <label>Notes <input bind:value={notes} /></label>
          <button type="submit">Save takings</button>
        </form>
      </FormPanel>
    </section>

    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#if summary}
        <div class="stat-tiles money-totals">
          {#if isOn("Purchases")}
            <div class="stat-tile">
              <strong>{money(Number(summary.owed))}</strong><span
                >still owed to suppliers</span
              >
            </div>
          {/if}
          {#if isOn("Invoices")}
            <div class="stat-tile">
              <strong>{money(Number(summary.owedToUs))}</strong><span
                >still owed by customers</span
              >
            </div>
          {/if}
        </div>
        <!-- On a phone, one card with a row for each month. -->
        {#if summary.months.length > 0}
          <ul class="narrow-only card takings-list" role="list">
            {#each summary.months as month (month.month)}
              <li>
                <strong>{monthName(month.month)}</strong>
                <span class="stack-tight month-figures">
                  <span>Income: {money(Number(month.income))}</span>
                  <span>Spending: {money(Number(month.spending))}</span>
                </span>
              </li>
            {/each}
          </ul>
          <div class="wide-only">
            <DataTable
              rows={summary.months}
              columns={monthColumns}
              key={(month) => month.month}
              caption="Income and spending by month"
              fit
            />
          </div>
        {/if}
      {/if}

      <a class="button secondary" href="/api/money/export.csv" download
        ><FileDown size={18} aria-hidden="true" /> Download the takings and spending
        for the accountant (CSV)</a
      >

      <h2 class="table-heading">Recent takings</h2>
      {#if takings.length > 0}
        <!-- On a phone, one card with a row for each day's takings. -->
        <ul class="narrow-only card takings-list" role="list">
          {#each takings as taking (taking.id)}
            <li>
              <span class="stack-tight">
                <strong
                  >{dayLabel(taking.takenOn)}: {money(
                    Number(taking.amount),
                  )}</strong
                >
                {#if taking.notes}<span class="muted">{taking.notes}</span>{/if}
              </span>
              {@render takingActions(taking)}
            </li>
          {/each}
        </ul>
        <div class="wide-only">
          <DataTable
            rows={takings}
            columns={takingColumns}
            key={(taking) => taking.id}
            caption="Recent takings"
            actions={takingActions}
            fit
          />
        </div>
      {:else}
        <p class="note-empty muted">No takings have been entered yet.</p>
      {/if}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
