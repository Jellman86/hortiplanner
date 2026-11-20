<script lang="ts">
  import ArrowRight from "@lucide/svelte/icons/arrow-right";
  import Undo2 from "@lucide/svelte/icons/undo-2";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { isOn } from "../../lib/features.svelte";
  import { forRecordWork, levelClass, placeNames } from "../../lib/needs";
  import { loadNeeds, needs } from "../../lib/needs.svelte";
  import type { Schemas } from "../../lib/types";

  // Jobs due today or late, to tick off; and anything in the records that wants this person's look, with why. Each
  // person gets their own list from the server: staff what is theirs to do, the owner the whole nursery's.
  let {
    onGo,
  }: {
    onGo: (where: string) => void;
  } = $props();

  let jobs = $state<Schemas["JobSummary"][]>([]);
  // The same list as the bell, so ticking a job off here lowers its number straight away.
  const alerts = $derived(forRecordWork(needs.alerts));
  let busyId = $state<string | null>(null);
  let problem = $state("");
  // The job last ticked off here, so a tap by mistake can be taken back.
  let lastDone = $state<{ id: string; title: string } | null>(null);

  const due = $derived(isOn("Jobs") ? jobs.filter((job) => job.due) : []);

  async function load() {
    try {
      // With jobs turned off only the reminders show here.
      jobs = isOn("Jobs") ? ((await api.GET("/api/jobs")).data ?? []) : [];
    } catch {
      // No signal: the jobs show again when there is one.
    }
  }

  onMount(load);

  async function done(job: Schemas["JobSummary"]) {
    busyId = job.id;
    problem = "";

    try {
      const { response } = await api.POST("/api/jobs/{id}/done", {
        params: { path: { id: job.id } },
        body: { doneOn: null },
      });
      problem = response.ok ? "" : "That couldn't be marked done.";
      lastDone = response.ok ? { id: job.id, title: job.title } : null;
      await Promise.all([load(), loadNeeds()]);
    } catch {
      problem = "Marking a job done needs a signal.";
    } finally {
      busyId = null;
    }
  }

  async function undo() {
    if (!lastDone) {
      return;
    }

    const job = lastDone;
    busyId = job.id;

    try {
      const { response } = await api.POST("/api/jobs/{id}/undo-done", {
        params: { path: { id: job.id } },
        body: { doneOn: null },
      });
      problem = response.ok
        ? ""
        : "That can't be undone any more, because a job can only be undone for a quarter of an hour.";
      lastDone = null;
      await Promise.all([load(), loadNeeds()]);
    } catch {
      problem = "Undoing needs a signal.";
    } finally {
      busyId = null;
    }
  }
</script>

{#if alerts.length > 0}
  <h2>Needs a look</h2>
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
{/if}

{#if lastDone}
  <p class="card done row" role="status">
    <span class="grow">{lastDone.title} is done.</span>
    <button class="link-button" disabled={busyId === lastDone.id} onclick={undo}
      ><Undo2 size={16} aria-hidden="true" />Undo</button
    >
  </p>
{/if}
{#if problem && due.length === 0}<p class="problem" role="alert">
    {problem}
  </p>{/if}

{#if due.length > 0}
  <h2>Jobs due</h2>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#each due as job (job.id)}
    <div class="card recent">
      <span class="plant-names">
        <strong>{job.title}</strong>
        <em
          >{job.locationName ? `${job.locationName} · ` : ""}{Number(
            job.daysLate,
          ) > 0
            ? `${job.daysLate} day${Number(job.daysLate) === 1 ? "" : "s"} late`
            : "Due today"}{job.lastDoneOn
            ? ` · last done ${dayLabel(job.lastDoneOn)}${job.lastDoneBy ? ` by ${job.lastDoneBy}` : ""}`
            : ""}</em
        >
        {#if job.notes}<span class="job-notes">{job.notes}</span>{/if}
      </span>
      <div class="row">
        <button
          class="secondary"
          disabled={busyId === job.id}
          onclick={() => done(job)}>Done</button
        >
      </div>
    </div>
  {/each}
{/if}
