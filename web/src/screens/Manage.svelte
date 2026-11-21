<script lang="ts">
  import {
    firstPage,
    hubOf,
    isManageSectionOn,
    listedPages,
    visibleHubs,
    type ManageSection,
  } from "../lib/sections";
  import { getHubNav } from "../lib/hubNav";
  import type { BatchPrefill } from "../lib/work";
  import type { AskAbout } from "../lib/assistant.svelte";
  import Climate from "./manage/Climate.svelte";
  import Consignments from "./manage/Consignments.svelte";
  import Customers from "./manage/Customers.svelte";
  import Figures from "./manage/Figures.svelte";
  import Invoices from "./manage/Invoices.svelte";
  import Locations from "./manage/Locations.svelte";
  import Money from "./manage/Money.svelte";
  import Plants from "./manage/Plants.svelte";
  import CostsToFillIn from "./manage/CostsToFillIn.svelte";
  import Purchases from "./manage/Purchases.svelte";
  import Suppliers from "./manage/Suppliers.svelte";
  import Trace from "./manage/Trace.svelte";
  import Jobs from "./manage/Jobs.svelte";
  import MotherPlants from "./manage/MotherPlants.svelte";

  let {
    section,
    batchId = null,
    onSection,
    onTrace,
    onBack,
    onStartBatch,
    onOpenTray,
    onMoveTraysHere,
    onAsk,
  }: {
    section: ManageSection | null;
    batchId?: string | null;
    onSection: (section: ManageSection | null) => void;
    onTrace: (batchId: string | null) => void;
    onBack: () => void;
    onStartBatch: (prefill: BatchPrefill) => void;
    onOpenTray: (label: string) => void;
    onMoveTraysHere: (id: string) => void;
    /** Asks Ask Hortiplanner about something, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
  } = $props();

  const nav = getHubNav();

  /**
   * Back from a page goes where it was reached from: Trace a batch to Production, Jobs to Record work, and the rest to
   * More, which lists them.
   */
  function back() {
    const hub = section ? hubOf(section) : null;

    if (hub?.hub === "home") {
      onBack();
    } else if (hub?.inPhoneBar && nav) {
      nav.open(firstPage(hub, true));
    } else {
      onSection(null);
    }
  }
  // A section whose part of Hortiplanner is turned off isn't opened, even from a remembered screen.
  const open = $derived(section && isManageSectionOn(section) ? section : null);
  // More lists the places that aren't in the phone's bar along the bottom, each with its pages.
  const groups = $derived(
    visibleHubs(true)
      .filter((hub) => !hub.inPhoneBar)
      .map((hub) => ({ hub, pages: listedPages(hub, true) })),
  );
</script>

{#if open === "figures"}
  <Figures onBack={back} onCosts={() => onSection("costs")} />
{:else if open === "costs"}
  <CostsToFillIn onBack={() => onSection("figures")} />
{:else if open === "money"}
  <Money onBack={back} onFigures={() => onSection("figures")} />
{:else if open === "purchases"}
  <Purchases onBack={back} {onStartBatch} />
{:else if open === "plants"}
  <Plants onBack={back} {onAsk} />
{:else if open === "suppliers"}
  <Suppliers onBack={back} />
{:else if open === "places"}
  <Locations onBack={back} {onOpenTray} {onMoveTraysHere} {onAsk} />
{:else if open === "mothers"}
  <MotherPlants onBack={back} {onTrace} />
{:else if open === "climate"}
  <Climate onBack={back} {onAsk} />
{:else if open === "jobs"}
  <Jobs onBack={back} />
{:else if open === "trace"}
  <Trace {batchId} onChoose={onTrace} onBack={back} {onOpenTray} {onAsk} />
{:else if open === "invoices"}
  <Invoices onBack={back} />
{:else if open === "consignments"}
  <Consignments onBack={back} />
{:else if open === "customers"}
  <Customers onBack={back} />
{:else}
  <main>
    <h1>More</h1>
    <p class="muted page-intro">
      These are the parts of Hortiplanner that don't fit in the bar below.
    </p>
    {#each groups as { hub, pages } (hub.hub)}
      <section class="stack menu-group">
        <h2 class="settings-heading">{hub.label}</h2>
        <div class="menu-grid">
          {#each pages as item (item.page)}
            <button
              class="secondary menu-item"
              onclick={() => onSection(item.page as ManageSection)}
            >
              <item.icon size={20} />
              {item.label}
            </button>
          {/each}
        </div>
      </section>
    {/each}
    <p class="muted">
      Trace a batch is at the top of Production, and the jobs are on Record
      work. The nursery's details, labels, pot sizes, staff and signing in are
      under Settings.
    </p>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
