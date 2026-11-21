<script lang="ts">
  import { getHubNav } from "../lib/hubNav";
  import { hubOf, listedPages, type HubPage } from "../lib/sections";

  // The row of buttons under a page's heading for the other pages in its place, such as Invoices, Customers and
  // Consignments under Selling. Only when the place has more than one page this person is shown, and never at a bench.
  let { page }: { page: HubPage } = $props();

  const nav = getHubNav();
  const hub = $derived(hubOf(page));
  const pages = $derived(nav ? listedPages(hub, nav.isOwner) : []);
</script>

{#if nav && !nav.atBench && pages.length > 1}
  <nav class="hub-nav" aria-label={hub.label}>
    {#each pages as item (item.page)}
      <button
        aria-current={item.page === page ? "page" : undefined}
        onclick={() => nav.open(item.page)}
      >
        <item.icon size={16} aria-hidden="true" />
        {item.label}
      </button>
    {/each}
  </nav>
{/if}
