<script lang="ts">
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import { assistant, type AskAbout } from "../lib/assistant.svelte";

  // "Ask about this …", the same everywhere: shown only to someone who can ask, and for a photo only when the model
  // reads photos and the photo is small enough to send.
  let {
    about,
    onAsk,
    askable = true,
    before,
    name,
  }: {
    about: AskAbout;
    onAsk?: (about: AskAbout) => void;
    /** For a photo: whether it's small enough to send. */
    askable?: boolean;
    /** Done first, such as closing the photo viewer. */
    before?: () => void;
    /** What a screen reader says, when the label alone would be the same for several buttons. */
    name?: string;
  } = $props();

  const label = $derived(
    about.kind === "photo"
      ? "Ask about this photo"
      : about.kind === "place"
        ? "Ask about this place"
        : about.kind === "tray"
          ? "Ask about this tray"
          : "Ask about this plant",
  );
  const shown = $derived(
    !!onAsk &&
      assistant.canAsk &&
      (about.kind !== "photo" || (assistant.readsImages && askable)),
  );
</script>

{#if shown}
  <button
    type="button"
    class="secondary"
    aria-label={name}
    onclick={() => {
      before?.();
      onAsk?.(about);
    }}><Sparkles size={18} aria-hidden="true" /> {label}</button
  >
{/if}
