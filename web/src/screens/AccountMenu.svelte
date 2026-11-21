<script lang="ts">
  import LogOut from "@lucide/svelte/icons/log-out";
  import UserRound from "@lucide/svelte/icons/user-round";
  import type { Me } from "../lib/boot";
  import { themes, type Theme } from "../lib/theme";
  import Avatar from "./Avatar.svelte";

  // The person's picture at the right of the bar along the top, on a desk: their account, how Hortiplanner looks, and
  // signing out. A phone has these under Settings, in the bar along the bottom.
  let {
    me,
    theme,
    onAccount,
    onTheme,
    onFinished,
  }: {
    me: Me;
    theme: Theme;
    onAccount: () => void;
    onTheme: (theme: Theme) => void;
    onFinished: () => void;
  } = $props();

  let panel = $state<HTMLElement>();

  // Light, Dark, then Match device, as the choices are read.
  const choices = [
    ...themes.filter((item) => item.theme !== "system"),
    ...themes.filter((item) => item.theme === "system"),
  ];

  /** Closes the panel, then does what was chosen. */
  function then(action: () => void) {
    panel?.hidePopover();
    action();
  }
</script>

<button
  class="icon-button topbar-person"
  popovertarget="account-panel"
  aria-label="Your account, signed in as {me.name}"
  title="Your account"
>
  <Avatar id={me.id} name={me.name} pictureAt={me.pictureAt} size={30} />
</button>

<div
  id="account-panel"
  class="account-panel"
  popover
  bind:this={panel}
  aria-label="Your account"
>
  <p>You're signed in as {me.name}.</p>
  <button class="secondary" onclick={() => then(onAccount)}
    ><UserRound size={16} aria-hidden="true" /> Go to My account</button
  >
  <div class="stack tight">
    <p class="account-panel-heading" id="account-appearance">Appearance</p>
    <div class="segmented" role="group" aria-labelledby="account-appearance">
      {#each choices as choice (choice.theme)}
        <button
          class={theme === choice.theme ? "" : "secondary"}
          aria-pressed={theme === choice.theme}
          onclick={() => onTheme(choice.theme)}>{choice.label}</button
        >
      {/each}
    </div>
  </div>
  <button class="secondary" onclick={() => then(onFinished)}
    ><LogOut size={16} aria-hidden="true" />
    {me.isTeam ? "Sign in as yourself" : "Sign out"}</button
  >
</div>
