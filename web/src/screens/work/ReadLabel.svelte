<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import Check from "@lucide/svelte/icons/check";
  import ScanText from "@lucide/svelte/icons/scan-text";
  import X from "@lucide/svelte/icons/x";
  import { onDestroy } from "svelte";
  import { api } from "../../api/client";
  import { askRefusals, assistant } from "../../lib/assistant.svelte";
  import { isOn } from "../../lib/features.svelte";
  import { shrinkPhoto } from "../../lib/photo";
  import { sowByWords } from "../../lib/sowBy";
  import type { Schemas } from "../../lib/types";

  type LabelRead = Schemas["LabelRead"];

  // Reads a photo of a seed packet with Ask Hortiplanner, shows what it read beside the photo, and hands it over only
  // when the person says so. Nothing is filled in or saved until then.
  let {
    onUse,
  }: {
    /** The details to fill in, the photo, and whether to keep the photo with the batch. */
    onUse: (read: LabelRead, photo: Blob, keep: boolean) => void;
  } = $props();

  let photo = $state<{ blob: Blob; url: string } | null>(null);
  let reading = $state(false);
  let read = $state<LabelRead | null>(null);
  let problem = $state("");
  let keep = $state(true);
  let online = $state(typeof navigator === "undefined" || navigator.onLine);
  let inFlight: AbortController | undefined;
  let input = $state<HTMLInputElement>();

  const shown = $derived(assistant.canAsk && assistant.readsImages);

  const rows = $derived.by(() => {
    if (!read) {
      return [];
    }

    const plant = [read.commonName, read.botanicalName]
      .filter(Boolean)
      .join(", ");
    return [
      { field: "commonName", name: "Plant", value: plant },
      { field: "variety", name: "Variety", value: read.variety },
      { field: "supplierName", name: "Supplier", value: read.supplierName },
      {
        field: "supplierPassportNumber",
        name: "Supplier's passport number",
        value: read.supplierPassportNumber,
      },
      {
        field: "lotOrReference",
        name: "Lot or reference",
        value: read.lotOrReference,
      },
      {
        field: "sowBy",
        name: "Sow by",
        value: read.sowBy ? sowByWords(read.sowBy) : null,
      },
      {
        field: "contentsAsPrinted",
        name: "What it holds",
        value: read.contentsAsPrinted,
      },
    ].map((row) => ({
      ...row,
      unsure:
        read!.unsure.includes(row.field) ||
        (row.field === "commonName" && read!.unsure.includes("botanicalName")),
    }));
  });

  const cost = $derived(Number(read?.cost ?? 0));

  function clear() {
    inFlight?.abort();
    reading = false;
    read = null;
    problem = "";

    if (photo) {
      URL.revokeObjectURL(photo.url);
      photo = null;
    }
  }

  onDestroy(clear);

  function toDataUrl(blob: Blob) {
    return new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(blob);
    });
  }

  async function chosen(event: Event) {
    const file = (event.currentTarget as HTMLInputElement).files?.[0];
    (event.currentTarget as HTMLInputElement).value = "";

    if (!file) {
      return;
    }

    clear();
    const blob = await shrinkPhoto(file, 1600);
    photo = { blob, url: URL.createObjectURL(blob) };
    reading = true;
    inFlight = new AbortController();

    try {
      const { data, error, response } = await api.POST(
        "/api/assistant/read-label",
        {
          body: { kind: "SeedPacket", image: await toDataUrl(blob) },
          signal: inFlight.signal,
        },
      );

      if (data) {
        read = data;
        assistant.spentThisMonth = Number(data.spentThisMonth ?? 0);
        assistant.spentInAll = Number(data.spentInAll ?? 0);
        problem = data.readable
          ? ""
          : data.what === "other"
            ? "That doesn't look like a seed packet, so nothing was filled in. Take another photo with the printed side filling the picture, or fill this in by hand."
            : "The writing in that photo was too hard to read, so nothing was filled in. Try again closer up and in good light.";
      } else {
        const reason = (error as { problem?: string } | undefined)?.problem;
        problem =
          response.status === 400
            ? "That picture couldn't be used. Please take a new photo with the camera."
            : reason === "TooMany"
              ? "Ask Hortiplanner has been asked a lot this hour, so it can't read another photo yet. Please fill this in by hand, or try again later."
              : ((reason && reason !== "Unavailable"
                  ? askRefusals[reason]
                  : undefined) ??
                "The photo couldn't be read just now. Please try again, or fill this in by hand.");
      }
    } catch {
      problem =
        "The photo couldn't be read just now. Please try again, or fill this in by hand.";
    } finally {
      reading = false;
    }
  }

  function stop() {
    inFlight?.abort();
    reading = false;
    problem =
      "The photo couldn't be read just now. Please try again, or fill this in by hand.";
  }

  function use() {
    if (read && photo) {
      onUse(read, photo.blob, keep && isOn("BatchPhotos"));
      read = null;
      problem = "";
      URL.revokeObjectURL(photo.url);
      photo = null;
    }
  }
</script>

<svelte:window
  ononline={() => (online = true)}
  onoffline={() => (online = false)}
/>

{#if shown}
  <section class="stack read-label" aria-label="Read a seed packet">
    <!-- One picker for every "take a photo" button: iOS offers the camera or the library. -->
    <input
      bind:this={input}
      class="visually-hidden"
      type="file"
      accept="image/*"
      tabindex="-1"
      aria-hidden="true"
      onchange={chosen}
    />
    {#if !photo}
      <button
        class="secondary"
        disabled={!online}
        onclick={() => input?.click()}
        ><ScanText size={18} aria-hidden="true" /> Read a photo of the seed packet</button
      >
      <p class="muted">
        {online
          ? "Ask Hortiplanner reads the packet and fills in what it can, for you to check."
          : "Reading a photo needs a signal. You can still fill this in by hand."}
      </p>
    {:else}
      <div class="card read-label-card">
        <img class="read-label-photo" src={photo.url} alt="The seed packet" />
        <div class="stack tight">
          {#if reading}
            <p role="status">
              Ask Hortiplanner is reading the packet. This usually takes a few
              seconds.
            </p>
            <button class="secondary" onclick={stop}
              ><X size={18} aria-hidden="true" /> Stop reading</button
            >
          {:else if read?.readable}
            <h2>What the packet says</h2>
            <p class="muted">
              Check each of these against the photo. Nothing is saved until you
              start the batch.
            </p>
            <dl class="facts read-label-facts">
              {#each rows as row (row.field)}
                <dt>{row.name}</dt>
                <dd>
                  {#if row.value}
                    <span>{row.value}</span>
                    {#if row.unsure}
                      <span class="read-label-unsure"
                        >This wasn't clear in the photo. Please check it
                        carefully.</span
                      >
                    {/if}
                  {:else}
                    <span class="muted">This wasn't found on the packet.</span>
                  {/if}
                </dd>
              {/each}
            </dl>
            {#if isOn("BatchPhotos")}
              <label class="tick"
                ><input type="checkbox" bind:checked={keep} /> Keep this photo with
                the batch.</label
              >
            {/if}
            <p class="muted credit">
              Reading this photo cost {cost < 0.01
                ? "under 1 cent"
                : `about $${cost.toFixed(2)}`}.
            </p>
            <button onclick={use}
              ><Check size={18} aria-hidden="true" /> Use these details</button
            >
          {/if}
          {#if problem}
            <p class="problem" role="alert">{problem}</p>
          {/if}
          {#if !reading}
            <button class="secondary" onclick={() => input?.click()}
              ><Camera size={18} aria-hidden="true" /> Take another photo</button
            >
            <button class="secondary" onclick={clear}
              ><X size={18} aria-hidden="true" /> Carry on without the photo</button
            >
          {/if}
        </div>
      </div>
    {/if}
  </section>
{/if}
