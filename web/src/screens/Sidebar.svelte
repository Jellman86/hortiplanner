<script lang="ts">
  import Avatar from "./Avatar.svelte";
  import LogOut from "@lucide/svelte/icons/log-out";
  import Monitor from "@lucide/svelte/icons/monitor";
  import Moon from "@lucide/svelte/icons/moon";
  import PanelLeftClose from "@lucide/svelte/icons/panel-left-close";
  import PanelLeftOpen from "@lucide/svelte/icons/panel-left-open";
  import ScanLine from "@lucide/svelte/icons/scan-line";
  import Settings from "@lucide/svelte/icons/settings";
  import Sun from "@lucide/svelte/icons/sun";
  import type { Me } from "../lib/boot";
  import { visibleHubs, type Hub, type HubName } from "../lib/sections";
  import { themes, type Theme } from "../lib/theme";

  // Only shown on wide screens: phones get the bottom bar instead.
  let {
    me,
    current,
    collapsed,
    theme,
    onHome,
    onHub,
    onToggleCollapsed,
    onCycleTheme,
    onSettings,
    onFinished,
  }: {
    me: Me;
    /** The place the person is in, marked in the list. */
    current: HubName | "settings" | null;
    collapsed: boolean;
    theme: Theme;
    onHome: () => void;
    /** Opens a place on its first page. */
    onHub: (hub: Hub) => void;
    onToggleCollapsed: () => void;
    onCycleTheme: () => void;
    onSettings: () => void;
    onFinished: () => void;
  } = $props();

  // The rule goes before the first of the business's places that's shown.
  const firstBusiness = $derived(
    visibleHubs(me.isOwner).find((hub) =>
      ["selling", "buying", "money"].includes(hub.hub),
    )?.hub,
  );

  const here = (item: typeof current) =>
    current === item ? ("page" as const) : undefined;

  const themeLabel = $derived(
    themes.find((item) => item.theme === theme)?.label ?? "",
  );
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
    <p class="signed-in nav-text">
      <Avatar id={me.id} name={me.name} pictureAt={me.pictureAt} size={28} />
      <span>Signed in as {me.name}</span>
    </p>
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
      aria-label="Theme: {themeLabel}"
      title="Theme: {themeLabel}"
      onclick={onCycleTheme}
    >
      {#if theme === "light"}
        <Sun size={20} />
      {:else if theme === "dark"}
        <Moon size={20} />
      {:else}
        <Monitor size={20} />
      {/if}
      <span class="nav-text">{themeLabel}</span>
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
    <button
      aria-label={me.isTeam ? "Sign in as yourself" : "Sign out"}
      title={me.isTeam ? "Sign in as yourself" : "Sign out"}
      onclick={onFinished}
    >
      <LogOut size={20} />
      <span class="nav-text"
        >{me.isTeam ? "Sign in as yourself" : "Sign out"}</span
      >
    </button>
  </div>
</aside>
