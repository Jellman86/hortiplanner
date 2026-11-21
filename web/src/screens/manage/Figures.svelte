<script lang="ts">
  import HubNav from "../HubNav.svelte";
  import { lossWords } from "../../lib/losses";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Column } from "../../lib/table";
  import { attempt, type Schemas } from "../../lib/types";
  import { lossReasons, percent, plantName } from "../../lib/work";
  import BarList from "../BarList.svelte";
  import DataTable from "../DataTable.svelte";
  import { isOn } from "../../lib/features.svelte";
  import MoneyFigures from "./MoneyFigures.svelte";

  let { onBack, onCosts }: { onBack: () => void; onCosts: () => void } =
    $props();

  // Beneath "How the plants are growing" while the money figures sit above it, so headings stay in order.
  const sub = $derived(isOn("Costs") ? "h3" : "h2");

  type PlantFigures = Schemas["PlantFigures"];
  type GroupFigures = Schemas["GroupFigures"];

  let figures = $state<Schemas["Figures"] | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  const reasonLabel = (reason: string) =>
    lossReasons.find((item) => item.reason === reason)?.label ?? reason;

  const rate = (value: number | string | null | undefined) =>
    value === null || value === undefined ? null : Number(value);

  /** "2026-11" as "Nov 2026". */
  const monthLabel = (name: string) => {
    const [year, month] = name.split("-").map(Number);
    return year && month
      ? new Date(year, month - 1, 1).toLocaleDateString("en-GB", {
          month: "short",
          year: "numeric",
        })
      : name;
  };

  // Seed sown "about so many" gives a rate that is only approximate, and says so.
  const germination = (
    value: number | string | null | undefined,
    approximate: boolean | undefined,
  ) =>
    approximate && value != null ? `about ${percent(value)}` : percent(value);

  const plantColumns: Column<PlantFigures>[] = [
    { label: "Plant", value: (row) => plantName(row.commonName, row.variety) },
    { label: "Started", value: (row) => Number(row.started), numeric: true },
    {
      label: "Germination",
      value: (row) => rate(row.germinationRate),
      text: (row) =>
        germination(row.germinationRate, row.germinationApproximate),
      numeric: true,
    },
    {
      label: "Survival",
      value: (row) => rate(row.survivalRate),
      text: (row) => percent(row.survivalRate),
      numeric: true,
    },
    { label: "Lost", value: (row) => Number(row.lost), numeric: true },
    {
      label: "Counted +/−",
      value: (row) => Number(row.adjusted),
      numeric: true,
    },
    { label: "Alive now", value: (row) => Number(row.alive), numeric: true },
  ];

  const groupColumns = (
    label: string,
    name = (row: GroupFigures) => row.name,
  ) =>
    [
      { label, value: name },
      { label: "Started", value: (row) => Number(row.started), numeric: true },
      {
        label: "Germination",
        value: (row) => rate(row.germinationRate),
        text: (row) =>
          germination(row.germinationRate, row.germinationApproximate),
        numeric: true,
      },
      {
        label: "Survival",
        value: (row) => rate(row.survivalRate),
        text: (row) => percent(row.survivalRate),
        numeric: true,
      },
      { label: "Lost", value: (row) => Number(row.lost), numeric: true },
    ] as Column<GroupFigures>[];

  const survivalBars = (
    groups: GroupFigures[],
    name = (row: GroupFigures) => row.name,
  ) =>
    groups
      .filter((group) => group.survivalRate !== null)
      .map((group) => ({
        label: name(group),
        value: Number(group.survivalRate) * 100,
        text: percent(group.survivalRate),
      }));

  const breakdowns = $derived(
    figures
      ? [
          {
            title: "Survival by supplier",
            heading: "Supplier",
            groups: figures.bySupplier,
            name: (row: GroupFigures) => row.name,
          },
          {
            title: "Survival by place",
            heading: "Place",
            groups: figures.byPlace,
            name: (row: GroupFigures) => row.name,
          },
          {
            title: "Survival by month started",
            heading: "Month",
            groups: figures.bySeason,
            name: (row: GroupFigures) => monthLabel(row.name),
          },
        ].filter((item) => item.groups.length > 0)
      : [],
  );

  onMount(() =>
    attempt(async () => {
      figures = (await api.GET("/api/figures")).data ?? null;
    }, setProblem),
  );
</script>

<main>
  <h1>Figures</h1>
  <HubNav page="figures" />

  {#if isOn("Costs")}
    <MoneyFigures {onCosts} />
    <h2>How the plants are growing</h2>
  {/if}

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if figures}
    {#if figures.plants.length === 0}
      <p class="note-empty muted">Nothing has been recorded yet.</p>
    {:else}
      <div class="wide-only">
        <DataTable
          rows={figures.plants}
          columns={plantColumns}
          key={(row) => row.plantId}
          caption="Germination and survival for each plant"
        />
      </div>
      <div class="figure-grid">
        {#if figures.losses.length > 0}
          <section class="card">
            <svelte:element this={sub}>Losses by reason</svelte:element>
            <BarList
              caption="Plants lost, by reason"
              series={3}
              rows={figures.losses.map((loss) => ({
                label: reasonLabel(String(loss.reason)),
                value: Number(loss.quantity),
                text: lossWords(
                  Number(loss.quantity),
                  Number(loss.uncountedTrays),
                ),
              }))}
            />
          </section>
        {/if}

        {#each breakdowns as item (item.title)}
          <section class="card">
            <svelte:element this={sub}>{item.title}</svelte:element>
            <BarList
              caption={item.title}
              max={100}
              series={2}
              rows={survivalBars(item.groups, item.name)}
            />
            <details>
              <summary>See the numbers</summary>
              <DataTable
                rows={item.groups}
                columns={groupColumns(item.heading, item.name)}
                key={(row) => row.name}
                caption={item.title}
                fit
              />
            </details>
          </section>
        {/each}
      </div>

      <!-- On a phone the charts come first, then each plant's figures. -->
      <div class="narrow-only stack">
        <svelte:element this={sub}>Each plant</svelte:element>
        <div class="card-grid">
          {#each figures.plants as plant (plant.plantId)}
            <section class="card stack tight">
              <h3>{plantName(plant.commonName, plant.variety)}</h3>
              <dl class="figure-facts">
                <dt>Started</dt>
                <dd>{plant.started}</dd>
                <dt>Germination</dt>
                <dd>
                  {germination(
                    plant.germinationRate,
                    plant.germinationApproximate,
                  )}
                </dd>
                <dt>Survival</dt>
                <dd>{percent(plant.survivalRate)}</dd>
                <dt>Lost</dt>
                <dd>{plant.lost}</dd>
                {#if Number(plant.adjusted) !== 0}
                  <dt>Stock count adjustments</dt>
                  <dd>{plant.adjusted}</dd>
                {/if}
                <dt>Alive now</dt>
                <dd>{plant.alive}</dd>
              </dl>
            </section>
          {/each}
        </div>
      </div>
    {/if}
  {/if}

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
