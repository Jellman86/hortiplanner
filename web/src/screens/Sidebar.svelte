<script lang="ts">
  import PanelLeftClose from "@lucide/svelte/icons/panel-left-close";
  import PanelLeftOpen from "@lucide/svelte/icons/panel-left-open";
  import ScanLine from "@lucide/svelte/icons/scan-line";
  import Settings from "@lucide/svelte/icons/settings";
  import type { Me } from "../lib/boot";
  import { visibleHubs, type Hub, type HubName } from "../lib/sections";

  // Only shown on wide screens: phones get the bottom bar instead.
  let {
    me,
    current,
    collapsed,
    onHome,
    onHub,
    onToggleCollapsed,
    onSettings,
  }: {
    me: Me;
    /** The place the person is in, marked in the list. */
    current: HubName | "settings" | null;
    collapsed: boolean;
    onHome: () => void;
    /** Opens a place on its first page. */
    onHub: (hub: Hub) => void;
    onToggleCollapsed: () => void;
    onSettings: () => void;
  } = $props();

  // The rule goes before the first of the business's places that's shown.
  const firstBusiness = $derived(
    visibleHubs(me.isOwner).find((hub) =>
      ["selling", "buying", "money"].includes(hub.hub),
    )?.hub,
  );

  const here = (item: typeof current) =>
    current === item ? ("page" as const) : undefined;

  const collapseLabel = $derived(
    collapsed ? "Expand sidebar" : "Collapse sidebar",
  );
</script>

<aside class="sidebar">
  <button
    class="brand"
    aria-label="Hortiplanner, go to Record work"
    title="Go to Record work"
    onclick={onHome}
  >
    <img src="/logo-96.png" alt="" width="40" height="40" />
    <span class="nav-text">Hortiplanner</span>
  </button>

  <nav aria-label="Main">
    <button
      aria-current={here("home")}
      aria-label="Record work"
      title="Record work"
      onclick={onHome}
    >
      <ScanLine size={20} />
      <span class="nav-text">Record work</span>
    </button>
    {#each visibleHubs(me.isOwner) as hub (hub.hub)}
      <!-- A thin rule between growing and the business. -->
      {#if hub.hub === firstBusiness}<hr class="nav-rule" />{/if}
      <button
        aria-current={here(hub.hub)}
        aria-label={hub.label}
        title={hub.label}
        onclick={() => onHub(hub)}
      >
        <hub.icon size={20} />
        <span class="nav-text">{hub.label}</span>
      </button>
    {/each}
  </nav>

  <div class="sidebar-foot">
    <button
      aria-current={here("settings")}
      aria-label="Settings"
      title="Settings"
      onclick={onSettings}
    >
      <Settings size={20} />
      <span class="nav-text">Settings</span>
    </button>
    <button
      aria-label={collapseLabel}
      title={collapseLabel}
      onclick={onToggleCollapsed}
    >
      {#if collapsed}
        <PanelLeftOpen size={20} />
      {:else}
        <PanelLeftClose size={20} />
      {/if}
      <span class="nav-text">Collapse</span>
    </button>
  </div>
</aside>
