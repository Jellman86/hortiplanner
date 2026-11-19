<script lang="ts">
  import Camera from "@lucide/svelte/icons/camera";
  import RefreshCw from "@lucide/svelte/icons/refresh-cw";
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import Star from "@lucide/svelte/icons/star";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { AskAbout } from "../../lib/assistant.svelte";
  import { photoCaption } from "../../lib/batchPhotos";
  import { isOn } from "../../lib/features.svelte";
  import { shrinkPhoto } from "../../lib/photo";
  import {
    plantPhotoAlt,
    plantPhotoKindLabel,
    plantPhotoKinds,
    plantPhotoProblem,
    type PlantPhotoKind,
  } from "../../lib/plantPhotos";
  import type { Schemas } from "../../lib/types";

  // A plant's own photos (flower, foliage, the whole plant, its label), one of them its main picture, and beside
  // them the photos taken of its batches as they grew.
  let {
    plantId,
    plant,
    hasPicture,
    onAsk,
    onChanged,
  }: {
    plantId: string;
    /** The plant's name, for describing its photos. */
    plant: string;
    /** Whether the plant shows a picture at all, which with no photos of its own is the species picture. */
    hasPicture: boolean;
    /** Asks Ask Hortiplanner about a photo, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
    /** The main picture may have changed, so the page shows it afresh. */
    onChanged: () => void | Promise<void>;
  } = $props();

  type Photo = Schemas["PlantPhotoSummary"];
  type BatchPhoto = Schemas["PlantBatchPhoto"];

  let photos = $state<Photo[]>([]);
  let fromBatches = $state<BatchPhoto[]>([]);
  let loaded = $state(false);
  let unreachable = $state(false);
  let adding = $state(false);
  let saving = $state(false);
  // Problems adding a photo show by "Add a photo"; problems with the photo open in the viewer show there.
  let addProblem = $state("");
  let viewerProblem = $state("");
  let viewerSaved = $state("");
  let viewing = $state<Photo | null>(null);
  let viewingBatch = $state<BatchPhoto | null>(null);
  let caption = $state("");
  let kind = $state<PlantPhotoKind>("Other");
  let dialog = $state<HTMLDialogElement>();
  let captionBox = $state<HTMLInputElement>();
  let heading = $state<HTMLHeadingElement>();

  /** Both lists, shown only once both have come back, so a half-loaded page never says there are no photos. */
  async function load() {
    const own = await api.GET("/api/plants/{id}/photos", {
      params: { path: { id: plantId } },
    });

    if (!own.response.ok) {
      throw new Error("The plant's photos couldn't be loaded.");
    }

    let batches: BatchPhoto[] = [];

    if (isOn("BatchPhotos")) {
      const fromTrays = await api.GET("/api/plants/{id}/batch-photos", {
        params: { path: { id: plantId } },
      });
      batches = fromTrays.response.ok ? (fromTrays.data ?? []) : [];
    }

    photos = own.data ?? [];
    fromBatches = batches;
    loaded = true;
    unreachable = false;
  }

  async function retry() {
    try {
      await load();
    } catch {
      // Photos aren't kept offline. Everything else on the page still works.
      unreachable = true;
    }
  }

  onMount(retry);

  /** Shows the page afresh after a change that has already been saved, so a slow signal can't make it look unsaved. */
  async function refresh() {
    try {
      await load();
      await onChanged();
    } catch {
      // What was saved stays saved; the page catches up next time it's opened.
    }
  }

  function open() {
    viewerProblem = "";
    viewerSaved = "";
    if (!dialog?.open) dialog?.showModal();
  }

  function view(photo: Photo, forCaption = false) {
    viewingBatch = null;
    viewing = photo;
    caption = photo.caption ?? "";
    kind = photo.kind;
    open();
    // Looking at a photo shouldn't raise the keyboard over it; only a photo just added goes to its caption.
    if (forCaption) setTimeout(() => captionBox?.focus());
  }

  function viewBatch(photo: BatchPhoto) {
    viewing = null;
    viewingBatch = photo;
    open();
  }

  function close() {
    dialog?.close();
  }

  async function add(event: Event, chosen: PlantPhotoKind) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    adding = true;
    addProblem = "";
    let saved: Photo | null = null;

    try {
      const response = await fetch(
        `/api/plants/${plantId}/photos?kind=${chosen}`,
        { method: "POST", body: await shrinkPhoto(file) },
      );

      if (!response.ok) {
        addProblem = plantPhotoProblem(response.status);
        return;
      }

      saved = (await response.json()) as Photo;
    } catch {
      addProblem = plantPhotoProblem(0);
      return;
    } finally {
      adding = false;
      input.value = "";
    }

    await refresh();
    // Straight to the new photo, so a caption can be added while it's in mind.
    view(photos.find((photo) => photo.id === saved.id) ?? saved, true);
  }

  async function describe(event: SubmitEvent) {
    event.preventDefault();

    if (!viewing) {
      return;
    }

    saving = true;
    viewerSaved = "";

    try {
      const { data, response } = await api.PUT("/api/plant-photos/{id}", {
        params: { path: { id: viewing.id } },
        body: { kind, caption: caption.trim() || null },
      });

      if (!response.ok || !data) {
        viewerProblem =
          "That caption couldn't be saved. Please close this photo and try again.";
        return;
      }

      viewerProblem = "";
      viewing = data;
      viewerSaved = "The caption and kind are saved.";
    } catch {
      viewerProblem =
        "That caption couldn't be saved. Please check the connection and try again.";
      return;
    } finally {
      saving = false;
    }

    await refresh();
  }

  async function makeMain(photo: Photo) {
    try {
      const { response } = await api.PUT("/api/plant-photos/{id}/main", {
        params: { path: { id: photo.id } },
      });

      if (!response.ok) {
        viewerProblem =
          "That couldn't be made the main picture. Please try again.";
        return;
      }
    } catch {
      viewerProblem =
        "That couldn't be made the main picture. Please check the connection and try again.";
      return;
    }

    viewerProblem = "";
    viewing = { ...photo, isMain: true };
    await refresh();
  }

  function removeQuestion(photo: Photo) {
    if (photos.length === 1) {
      return "Remove this photo? It is the only photo of this plant, and it can't be brought back.";
    }

    return photo.isMain
      ? "Remove this photo? It is the plant's main picture, so the newest of the other photos will take its place. It can't be brought back."
      : "Remove this photo? It can't be brought back.";
  }

  async function remove(photo: Photo) {
    if (!confirm(removeQuestion(photo))) {
      return;
    }

    try {
      const response = await fetch(`/api/plant-photos/${photo.id}`, {
        method: "DELETE",
      });

      if (!response.ok) {
        viewerProblem = "That photo couldn't be removed. Please try again.";
        return;
      }
    } catch {
      viewerProblem =
        "That photo couldn't be removed. Please check the connection and try again.";
      return;
    }

    close();
    // Its thumbnail has gone, so focus goes back to the top of the photos rather than nowhere.
    heading?.focus();
    await refresh();
  }

  function ask(about: AskAbout) {
    close();
    onAsk?.(about);
  }
</script>

<section class="stack plant-photos">
  <h2 bind:this={heading} tabindex="-1">Photos of this plant</h2>

  {#if unreachable}
    <p class="note-empty muted">
      The photos can't be shown without a connection. Everything else on this
      page still works.
    </p>
    <button class="secondary" onclick={retry}
      ><RefreshCw size={18} aria-hidden="true" /> Try again</button
    >
  {:else if loaded && photos.length === 0}
    <p class="note-empty muted">
      {hasPicture
        ? "Nobody has added a photo of this plant yet, so the picture above comes from the online plant details. Add one and it will be used instead."
        : "There are no photos of this plant yet. Add a few, such as the flower, the leaves and the label, so everyone can recognise it."}
    </p>
  {/if}

  {#if photos.length > 0}
    <ol class="photo-strip" aria-label="Photos of {plant}">
      {#each photos as photo (photo.id)}
        <li>
          <button class="photo-strip-item" onclick={() => view(photo)}>
            <img
              src="/api/plant-photos/{photo.id}"
              alt={plantPhotoAlt(photo, plant)}
              loading="lazy"
            />
            <span
              >{plantPhotoKindLabel(photo.kind)}{photo.isMain
                ? " · Main picture"
                : ""}</span
            >
            {#if photo.caption}<span class="muted photo-caption"
                >{photo.caption}</span
              >{/if}
          </button>
        </li>
      {/each}
    </ol>
  {/if}

  {#if !unreachable}
    <fieldset class="stack tight batch-photo-add">
      <legend class="field-label">Add a photo</legend>
      <div class="card-grid">
        {#each plantPhotoKinds as choice (choice.kind)}
          <label class="photo-choice" aria-disabled={adding}>
            <span class="plant-thumb placeholder"
              ><Camera size={22} aria-hidden="true" /></span
            >
            <span>{choice.label}</span>
            <input
              type="file"
              accept="image/*"
              disabled={adding}
              onchange={(event) => add(event, choice.kind)}
            />
          </label>
        {/each}
      </div>
      {#if adding}<p class="muted" role="status">Saving the photo…</p>{/if}
    </fieldset>
  {/if}
  {#if addProblem}<p class="problem" role="alert">{addProblem}</p>{/if}
</section>

{#if isOn("BatchPhotos") && !unreachable}
  <section class="stack plant-photos">
    <h2>Photos from its batches</h2>
    {#if fromBatches.length > 0}
      <ol class="photo-strip" aria-label="Photos from the batches of {plant}">
        {#each fromBatches as photo (photo.id)}
          <li>
            <button class="photo-strip-item" onclick={() => viewBatch(photo)}>
              <img
                src="/api/batch-photos/{photo.id}"
                alt="{plant}, batch {photo.traceabilityCode}"
                loading="lazy"
              />
              <span
                >Batch {photo.traceabilityCode}{photo.stage
                  ? ` · ${photoCaption({ kind: "Growing", stage: photo.stage })}`
                  : ""}</span
              >
            </button>
          </li>
        {/each}
      </ol>
    {:else if loaded}
      <p class="note-empty muted">
        Nobody has taken a photo of this plant's batches yet. Photos of the
        plants growing, added from a tray, will show here.
      </p>
    {/if}
  </section>
{/if}

<dialog
  class="photo-viewer"
  bind:this={dialog}
  onclose={() => {
    viewing = null;
    viewingBatch = null;
    viewerProblem = "";
    viewerSaved = "";
  }}
  aria-label="Photo of {plant}"
>
  {#if viewing}
    {@const photo = viewing}
    <img src="/api/plant-photos/{photo.id}" alt={plantPhotoAlt(photo, plant)} />
    <form class="stack tight" onsubmit={describe}>
      <label>
        What does this photo show? (You can leave this empty.)
        <input bind:this={captionBox} bind:value={caption} maxlength="200" />
      </label>
      <label>
        Kind of photo
        <select bind:value={kind}>
          {#each plantPhotoKinds as choice (choice.kind)}
            <option value={choice.kind}>{choice.label}</option>
          {/each}
        </select>
      </label>
      <button type="submit" class="secondary" disabled={saving}
        >Save the caption and kind</button
      >
      {#if viewerSaved}<p class="muted" role="status">{viewerSaved}</p>{/if}
    </form>
    {#if photo.isMain}
      <p class="muted">This is the plant's main picture.</p>
    {/if}
    <div class="row">
      <!-- svelte-ignore a11y_autofocus -->
      <button onclick={close} autofocus>Close</button>
      {#if !photo.isMain}
        <button class="secondary" onclick={() => makeMain(photo)}
          ><Star size={18} aria-hidden="true" /> Use as the main picture</button
        >
      {/if}
      {#if onAsk && photo.canBeAsked}
        <button
          class="secondary"
          onclick={() =>
            ask({
              title: `a photo of ${plant}`,
              plantId,
              plantPhotoId: photo.id,
            })}
          ><Sparkles size={18} aria-hidden="true" /> Ask about this photo</button
        >
      {/if}
      {#if photo.canRemove}
        <button class="secondary danger" onclick={() => remove(photo)}
          ><Trash2 size={18} aria-hidden="true" /> Remove this photo</button
        >
      {/if}
    </div>
    {#if viewerProblem}<p class="problem" role="alert">{viewerProblem}</p>{/if}
  {:else if viewingBatch}
    {@const photo = viewingBatch}
    <img
      src="/api/batch-photos/{photo.id}"
      alt="{plant}, batch {photo.traceabilityCode}"
    />
    <p><strong>Batch {photo.traceabilityCode}</strong></p>
    <div class="row">
      <!-- svelte-ignore a11y_autofocus -->
      <button onclick={close} autofocus>Close</button>
      {#if onAsk && photo.canBeAsked}
        <button
          class="secondary"
          onclick={() =>
            ask({
              title: `a photo of ${plant}`,
              plantId,
              photoId: photo.id,
            })}
          ><Sparkles size={18} aria-hidden="true" /> Ask about this photo</button
        >
      {/if}
    </div>
  {/if}
</dialog>
