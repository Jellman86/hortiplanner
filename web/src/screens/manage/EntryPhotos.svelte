<script lang="ts">
  import { Trash2 } from "@lucide/svelte";
  import { shrinkPhoto } from "../../lib/photo";
  import type { AskAbout } from "../../lib/assistant.svelte";
  import AskAboutButton from "../AskAboutButton.svelte";

  let {
    entry,
    id,
    photoIds,
    gallery = false,
    onChanged,
    onProblem,
    askAbout,
    askable = [],
    onAsk,
  }: {
    entry: "PotSize" | "Location";
    id: string;
    photoIds: string[];
    /** Large photos in a grid, for a page about the place, rather than small ones in a row. */
    gallery?: boolean;
    onChanged: () => Promise<void>;
    onProblem: (message: string) => void;
    /** What a question about one of the photos is, for a place's photos; pot sizes have none. */
    askAbout?: (photoId: string) => AskAbout;
    /** The photos small enough to hand to Ask Hortiplanner. */
    askable?: string[];
    /** Asks Ask Hortiplanner about a photo, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
  } = $props();

  let busy = $state(false);

  async function add(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    busy = true;

    try {
      const response = await fetch(`/api/catalogue-photos/${entry}/${id}`, {
        method: "POST",
        body: await shrinkPhoto(file),
      });

      input.value = "";

      if (response.ok) {
        await onChanged();
      } else {
        onProblem("That couldn't be saved. Please choose a photo.");
      }
    } catch {
      onProblem(
        "This device can't reach Hortiplanner. Please check the connection and try again.",
      );
    } finally {
      busy = false;
    }
  }

  async function remove(photoId: string) {
    if (!confirm("Remove this photo?")) {
      return;
    }

    try {
      const response = await fetch(`/api/catalogue-photos/${photoId}`, {
        method: "DELETE",
      });

      if (response.ok) {
        await onChanged();
      } else {
        onProblem("That photo couldn't be removed.");
      }
    } catch {
      onProblem(
        "This device can't reach Hortiplanner. Please check the connection and try again.",
      );
    }
  }
</script>

<div class="entry-photos" class:gallery>
  {#each photoIds as photoId, index (photoId)}
    <span class="entry-photo">
      <a href="/api/catalogue-photos/{photoId}" target="_blank" rel="noopener">
        <img
          class="plant-thumb"
          src="/api/catalogue-photos/{photoId}"
          alt={entry === "PotSize" ? "The pot" : "The place"}
          loading="lazy"
        />
      </a>
      <!-- Below the photo, not on it, so a tap meant for one never lands on the other. -->
      <button
        type="button"
        class="secondary photo-remove"
        title="Remove photo"
        onclick={() => remove(photoId)}
        ><Trash2 size={18} aria-hidden="true" /><span
          class={gallery ? undefined : "visually-hidden"}>Remove photo</span
        ></button
      >
      {#if askAbout}
        <AskAboutButton
          about={askAbout(photoId)}
          askable={askable.includes(photoId)}
          {onAsk}
          name={photoIds.length > 1
            ? `Ask about photo ${index + 1} of ${photoIds.length}`
            : undefined}
        />
      {/if}
    </span>
  {/each}
  <label class="photo-choice photo-add">
    <span>{busy ? "Saving…" : "Add photo"}</span>
    <input type="file" accept="image/*" disabled={busy} onchange={add} />
  </label>
</div>
