<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import { onMount } from "svelte";
  import { levelClass, placeNames, type Alert } from "../../lib/needs";
  import { dayLabel } from "../../lib/dates";
  import { isoDay } from "../../lib/days";
  import { loadNeeds, needs } from "../../lib/needs.svelte";

  // Everything that needs this person's look, from any screen: the same list as Record work, worked out afresh on
  // the server, with anything recorded on this device that couldn't be saved first.
  let {
    alerts,
    onGo,
  }: {
    /** The list to show, including any work this device couldn't save. */
    alerts: Alert[];
    onGo: (where: string) => void;
  } = $props();

  // Opening the list always checks it again.
  onMount(() => void loadNeeds());

  // The time it was last checked, with the day too when that wasn't today.
  const checked = $derived.by(() => {
    if (needs.checkedAt === null) {
      return null;
    }

    const when = new Date(needs.checkedAt);
    const time = when.toLocaleTimeString("en-GB", {
      hour: "2-digit",
      minute: "2-digit",
    });
    return isoDay(when) === isoDay(new Date())
      ? time
      : `${dayLabel(isoDay(when))} at ${time}`;
  });
</script>

<main>
  <h1>Needs a look</h1>

  {#if needs.failed && !needs.offline && alerts.length === 0}
    <p class="problem" role="alert">
      This list couldn't be checked just now. Please try again in a minute.
    </p>
  {/if}

  {#if needs.offline}
    <p class="card notice">
      {checked
        ? `This list was last checked at ${checked}, and there's no signal to check it again.`
        : "This list needs a signal."}
    </p>
  {/if}

  {#if alerts.length > 0}
    <div class="needs-list">
      {#each alerts as alert (alert.kind)}
        <div class="card alert-card {levelClass(alert)}">
          <strong>{alert.title}</strong>
          <span class="muted">{alert.why}</span>
          <button class="secondary" onclick={() => onGo(alert.where)}
            >Go to {placeNames[alert.where] ?? alert.where}
            <ArrowRight size={16} aria-hidden="true" /></button
          >
        </div>
      {/each}
    </div>
  {:else if needs.loaded}
    <p class="note-empty muted">Nothing needs a look just now.</p>
  {/if}
</main>
