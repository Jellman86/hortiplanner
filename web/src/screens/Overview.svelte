<script lang="ts">
  import { isOn } from "../lib/features.svelte";
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import Clock from "@lucide/svelte/icons/clock";
  import PackageCheck from "@lucide/svelte/icons/package-check";
  import PoundSterling from "@lucide/svelte/icons/pound-sterling";
  import { onMount } from "svelte";
  import { api } from "../api/client";
  import {
    plantsByStage,
    stages,
    totalPlants,
    type ProductionRow,
    type Stage,
  } from "../lib/production";
  import { byContainer } from "../lib/places";
  import type { Schemas } from "../lib/types";
  import { describeRecent, money } from "../lib/work";
  import BarList from "./BarList.svelte";

  // The owner's view of the whole nursery, for a wide screen.
  let {
    onOpen,
    onProduction,
    onMoney,
  }: {
    onOpen: (label: string) => void;
    onProduction?: (stage?: Stage | null) => void;
    onMoney?: () => void;
  } = $props();

  /** Seed usually shows within four weeks; longer is worth a look. */
  const slowToGerminateDays = 28;

  let rows = $state<ProductionRow[]>([]);
  let owed = $state(0);
  let today = $state<Schemas["RecentAction"][]>([]);
  let loaded = $state(false);

  const now = new Date();
  const daysSince = (since: string | null | undefined) =>
    since ? (now.getTime() - new Date(since).getTime()) / 86_400_000 : 0;

  const trays = $derived(byContainer(rows));
  const byStage = $derived(plantsByStage(rows));
  const stageBars = $derived(
    stages
      .filter((item) => item.always || (byStage.get(item.stage) ?? 0) > 0)
      .map((item) => ({
        label: item.label,
        value: byStage.get(item.stage) ?? 0,
      })),
  );
  // The trays that have waited longest come first.
  const slow = $derived(
    trays
      .filter(
        (lines) =>
          lines[0].stage === "Sown" &&
          daysSince(lines[0].stageSince) >= slowToGerminateDays,
      )
      .sort((a, b) => daysSince(b[0].stageSince) - daysSince(a[0].stageSince)),
  );
  /** How many slow trays are offered to open, so the card stays short. */
  const slowShown = 6;
  const ready = $derived(trays.filter((lines) => lines[0].stage === "Ready"));

  onMount(async () => {
    try {
      const startOfDay = new Date(
        now.getFullYear(),
        now.getMonth(),
        now.getDate(),
      );
      const [productionRes, moneyRes, recentRes] = await Promise.all([
        api.GET("/api/production"),
        isOn("Purchases") ? api.GET("/api/money/summary") : null,
        api.GET("/api/events/recent", {
          params: { query: { limit: 50, everyone: true } },
        }),
      ]);
      rows = productionRes.data ?? [];
      owed = Number(moneyRes?.data?.owed ?? 0);
      today = (recentRes.data ?? []).filter(
        (action) => new Date(action.occurredAt) >= startOfDay,
      );
    } finally {
      loaded = true;
    }
  });
</script>

{#if !loaded}
  <section
    class="stack overview"
    aria-label="The nursery at a glance"
    aria-busy="true"
  >
    <div class="stat-tiles">
      {#each [0, 1, 2, 3] as tile (tile)}
        <div class="stat-tile skeleton" aria-hidden="true">
          <span class="skeleton-bar wide"></span>
          <span class="skeleton-bar"></span>
        </div>
      {/each}
    </div>
  </section>
{:else}
  <section class="stack overview" aria-label="The nursery at a glance">
    <div class="stat-tiles">
      <button
        type="button"
        class="stat-tile stat-tile-button"
        onclick={() => onProduction?.(null)}
      >
        <strong>{totalPlants(rows)}</strong><span
          >{totalPlants(rows) === 1 ? "plant growing" : "plants growing"}</span
        >
      </button>
      <button
        type="button"
        class="stat-tile stat-tile-button"
        onclick={() => onProduction?.(null)}
      >
        <strong>{trays.length}</strong><span
          >{trays.length === 1
            ? "tray or pot group"
            : "trays and pot groups"}</span
        >
      </button>
      <button
        type="button"
        class="stat-tile stat-tile-button"
        onclick={() => onProduction?.("Ready")}
      >
        <strong>{ready.length}</strong><span>ready</span>
      </button>
      {#if isOn("Purchases")}
        <button
          type="button"
          class="stat-tile stat-tile-button"
          onclick={() => onMoney?.()}
        >
          <strong>{money(owed)}</strong><span>owed to suppliers</span>
        </button>
      {/if}
    </div>

    {#if rows.length > 0}
      <div class="card">
        <h2>Plants at each stage</h2>
        <BarList rows={stageBars} caption="Plants at each stage" />
      </div>
    {/if}

    {#if slow.length > 0 || ready.length > 0 || owed > 0}
      <div class="card">
        <h2>Worth a look</h2>
        <ul class="attention">
          {#if slow.length > 0}
            <li>
              <Clock size={18} aria-hidden="true" />
              <div class="stack tight">
                <p>
                  {slow.length === 1
                    ? `1 sown tray hasn't germinated after ${slowToGerminateDays} days.`
                    : `${slow.length} sown trays haven't germinated after ${slowToGerminateDays} days.`}
                  {#if slow.length > slowShown}
                    Here are the {slowShown} that have waited longest.
                  {/if}
                </p>
                <div class="tray-codes">
                  {#each slow.slice(0, slowShown) as lines (lines[0].containerId)}
                    <button
                      class="link-button"
                      aria-label="Open tray {lines[0].labelCode}"
                      onclick={() => onOpen(lines[0].labelCode)}
                      >{lines[0].labelCode}</button
                    >
                  {/each}
                </div>
              </div>
            </li>
          {/if}
          {#if ready.length > 0}
            <li>
              <PackageCheck size={18} aria-hidden="true" />
              <div class="stack tight">
                <p>
                  {ready.length}
                  {ready.length === 1 ? "tray is" : "trays are"} ready to sell.
                </p>
                {#if onProduction}
                  <button
                    class="link-button"
                    onclick={() => onProduction("Ready")}
                    >See ready trays <ArrowRight
                      size={16}
                      aria-hidden="true"
                    /></button
                  >
                {/if}
              </div>
            </li>
          {/if}
          {#if owed > 0}
            <li>
              <PoundSterling size={18} aria-hidden="true" />
              <div class="stack tight">
                <p>{money(owed)} is still owed to suppliers.</p>
                {#if onMoney}
                  <button class="link-button" onclick={onMoney}
                    >Go to Money <ArrowRight
                      size={16}
                      aria-hidden="true"
                    /></button
                  >
                {/if}
              </div>
            </li>
          {/if}
        </ul>
      </div>
    {/if}

    <div class="card">
      <h2>Today across the nursery</h2>
      {#each today as action (action.id)}
        <p>
          {describeRecent(action)}<span class="muted"
            >{action.recordedBy ? ` · ${action.recordedBy}` : ""}</span
          >
        </p>
      {:else}
        <p class="muted">Nothing has been recorded yet today.</p>
      {/each}
    </div>
  </section>
{/if}

<style>
  /* A row of tray codes to open, beneath what's worth a look about them. */
  .tray-codes {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
  }

  .tray-codes button {
    min-height: 2.5rem;
    padding: 0.3rem 0.75rem;
    font-variant-numeric: tabular-nums;
  }

  .stack.tight p {
    margin: 0;
  }
</style>
