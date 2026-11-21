<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import FileDown from "@lucide/svelte/icons/file-down";
  import { api } from "../../api/client";
  import { countsSentence } from "../../lib/costs";
  import { isOn } from "../../lib/features.svelte";
  import {
    costCell,
    costOrder,
    counting,
    figureCell,
    notKnownSentence,
    startedChoices,
    tillSentence,
    tiles,
    unansweredSentence,
    unbatchedNote,
    unbatchedSentence,
    type GroupMoney,
    type MoneyFigures,
    type PlantMoney,
    type StartedIn,
  } from "../../lib/moneyFigures";
  import type { Column } from "../../lib/table";
  import { attempt } from "../../lib/types";
  import { money, plantName } from "../../lib/work";
  import BarList from "../BarList.svelte";
  import DataTable from "../DataTable.svelte";

  // What the plants cost and made, for the owner, above the growing figures.
  let { onCosts }: { onCosts: () => void } = $props();

  let started = $state<StartedIn>("ThisYear");
  let figures = $state<MoneyFigures | null>(null);
  let problem = $state("");

  async function load(chosen: StartedIn) {
    problem = "";
    await attempt(
      async () => {
        const data = (
          await api.GET("/api/figures/money", {
            params: { query: { started: chosen } },
          })
        ).data;

        // A reply for a choice no longer made is left alone.
        if (chosen === started) {
          figures = data ?? null;
        }
      },
      (message) => (problem = message),
    );
  }

  $effect(() => {
    void load(started);
  });

  const costLabel = (side: "withVat" | "beforeVat") =>
    side === "withVat" ? "Cost with VAT" : "Cost before VAT";

  const plantColumns = $derived.by((): Column<PlantMoney>[] => {
    const counts = figures?.counts ?? "WithVat";
    return [
      {
        label: "Plant",
        value: (row) => plantName(row.commonName, row.variety),
      },
      { label: "Batches", value: (row) => Number(row.batches), numeric: true },
      { label: "Sold", value: (row) => Number(row.sold), numeric: true },
      {
        label: "Takings",
        value: (row) => counting(row.takings, counts),
        text: (row) => money(counting(row.takings, counts)),
        numeric: true,
      },
      ...costOrder(counts).map((side, index): Column<PlantMoney> => ({
        label: costLabel(side),
        value: (row) => (row.cost ? Number(row.cost[side]) : null),
        text: (row) => costCell(row.cost, row.complete, side),
        numeric: true,
        strong: index === 0,
      })),
      {
        label: "Profit",
        value: (row) => (row.profit ? counting(row.profit, counts) : null),
        text: (row) => figureCell(row.profit, counts),
        numeric: true,
      },
      {
        label: "Cost for each plant",
        value: (row) => (row.each ? counting(row.each, counts) : null),
        text: (row) => figureCell(row.each, counts),
        numeric: true,
      },
      {
        label: "Lost plants had cost",
        value: (row) =>
          row.lostValue ? counting(row.lostValue, counts) : null,
        text: (row) => figureCell(row.lostValue, counts),
        numeric: true,
      },
    ];
  });

  const groupColumns = (label: string): Column<GroupMoney>[] => {
    const counts = figures?.counts ?? "WithVat";
    return [
      { label, value: (row) => row.name },
      {
        label: "Takings",
        value: (row) => counting(row.takings, counts),
        text: (row) => money(counting(row.takings, counts)),
        numeric: true,
      },
      {
        label: "Cost",
        value: (row) => (row.cost ? counting(row.cost, counts) : null),
        text: (row) =>
          costCell(
            row.cost,
            row.complete,
            counts === "BeforeVat" ? "beforeVat" : "withVat",
          ),
        numeric: true,
      },
      {
        label: "Profit",
        value: (row) => (row.profit ? counting(row.profit, counts) : null),
        text: (row) => figureCell(row.profit, counts),
        numeric: true,
      },
    ];
  };

  const groups = $derived(
    figures
      ? [
          {
            title: "By year started",
            heading: "Year started",
            rows: figures.byYear,
          },
          {
            title: "By supplier",
            heading: "Supplier",
            rows: figures.bySupplier,
          },
        ].filter((group) => group.rows.length > 0)
      : [],
  );

  const unbatched = $derived(
    figures ? figures.plants.filter((plant) => plant.unbatched) : [],
  );
</script>

<section class="stack money-figures" aria-labelledby="money-figures-heading">
  <h2 id="money-figures-heading">What the plants cost and made</h2>

  <p id="started-in">Batches started in</p>
  <div class="segmented" role="group" aria-labelledby="started-in">
    {#each startedChoices as choice (choice.value)}
      <button
        class={started === choice.value ? "" : "secondary"}
        aria-pressed={started === choice.value}
        onclick={() => (started = choice.value)}>{choice.label}</button
      >
    {/each}
  </div>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if figures}
    {@const counts = figures.counts}
    <p class="muted">{countsSentence(figures.vatRegistered)}</p>

    {#if figures.plants.length === 0}
      <p class="note-empty muted">Nothing has a cost or a sale recorded yet.</p>
    {:else}
      <div class="stat-tiles money-tiles">
        {#each tiles(figures) as tile (tile.key)}
          <div class="stat-tile">
            <strong>{tile.amount}</strong><span>{tile.words}</span>
            {#if tile.compare}<span class="compare">{tile.compare}</span>{/if}
          </div>
        {/each}
      </div>

      {#if Number(figures.unanswered) > 0}
        <div class="card caution stack tight">
          <p>{unansweredSentence(Number(figures.unanswered))}</p>
          <div>
            <button class="secondary" onclick={onCosts}
              >Go to Costs to fill in <ArrowRight
                size={16}
                aria-hidden="true"
              /></button
            >
          </div>
        </div>
      {/if}

      {#if Number(figures.notKnown) > 0}
        <p class="muted">{notKnownSentence(Number(figures.notKnown))}</p>
      {/if}

      {#if isOn("TraySales") && Number(figures.till.taken) > 0}
        <p>{tillSentence(figures.till, started, counts)}</p>
      {/if}

      <div class="wide-only stack tight">
        <DataTable
          rows={figures.plants}
          columns={plantColumns}
          key={(row) => row.plantId}
          caption="What each plant cost and made"
        />
        {#each unbatched as plant (plant.plantId)}
          <p class="muted">{unbatchedNote(plant, counts)}</p>
        {/each}
      </div>

      <div class="figure-grid">
        {#each groups as group (group.title)}
          <section class="card">
            <h3>{group.title}</h3>
            <BarList
              caption="Takings, {group.title.toLowerCase()}"
              rows={group.rows.map((row) => ({
                label: row.name,
                value: counting(row.takings, counts),
                text: money(counting(row.takings, counts)),
              }))}
            />
            <details>
              <summary>See the numbers</summary>
              <DataTable
                rows={group.rows}
                columns={groupColumns(group.heading)}
                key={(row) => row.name}
                caption={group.title}
                fit
              />
            </details>
          </section>
        {/each}
      </div>

      <!-- On a phone, a card for each plant. -->
      <div class="narrow-only stack">
        <h3>Each plant</h3>
        <div class="card-grid">
          {#each figures.plants as plant (plant.plantId)}
            <section class="card stack tight">
              <h4>{plantName(plant.commonName, plant.variety)}</h4>
              <dl class="figure-facts">
                <dt>Batches</dt>
                <dd>{plant.batches}</dd>
                <dt>Sold</dt>
                <dd>{plant.sold}</dd>
                <dt>Takings</dt>
                <dd>{money(counting(plant.takings, counts))}</dd>
                {#each costOrder(counts) as side, index (side)}
                  <dt>{costLabel(side)}</dt>
                  <dd>
                    {#if index === 0}<strong
                        >{costCell(plant.cost, plant.complete, side)}</strong
                      >{:else}{costCell(plant.cost, plant.complete, side)}{/if}
                  </dd>
                {/each}
                <dt>Profit</dt>
                <dd>{figureCell(plant.profit, counts)}</dd>
                <dt>Cost for each plant</dt>
                <dd>{figureCell(plant.each, counts)}</dd>
                <dt>Lost plants had cost</dt>
                <dd>{figureCell(plant.lostValue, counts)}</dd>
              </dl>
              {#if plant.unbatched}
                <p class="muted">{unbatchedSentence(plant, counts)}</p>
              {/if}
            </section>
          {/each}
        </div>
      </div>
    {/if}

    <div>
      <a
        class="button secondary"
        href="/api/figures/money.csv?started={started}"
        download
        ><FileDown size={16} aria-hidden="true" /> Download these figures (CSV)</a
      >
    </div>
  {/if}
</section>

<style>
  .money-tiles .compare {
    font-size: 0.8rem;
  }
</style>
