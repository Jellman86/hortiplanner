<script lang="ts">
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import type { Schemas } from "../lib/types";

  // This person's own kept conversations, newest first, to open again. Nobody else's are ever listed.
  let {
    conversations,
    current = null,
    loaded,
    onOpen,
    onDeleteAll,
    busy = false,
  }: {
    conversations: Schemas["ConversationSummary"][];
    /** The one open now, marked in the list. */
    current?: string | null;
    loaded: boolean;
    onOpen: (id: string) => void;
    onDeleteAll: () => void;
    /** An answer is on its way, so nothing changes conversation until it has come. */
    busy?: boolean;
  } = $props();

  function lastAdded(at: string) {
    return new Date(at).toLocaleDateString("en-GB", {
      day: "numeric",
      month: "long",
      year:
        new Date(at).getFullYear() === new Date().getFullYear()
          ? undefined
          : "numeric",
    });
  }
</script>

{#if !loaded}
  <p class="muted">Your conversations are loading.</p>
{:else if conversations.length === 0}
  <p class="note-empty muted">
    You haven't any conversations yet. When you ask something, it is kept here
    so you can come back to it.
  </p>
{:else}
  <ul class="ask-conversation-list">
    {#each conversations as conversation (conversation.id)}
      <li>
        <button
          class="ask-conversation-row"
          aria-current={conversation.id === current ? "true" : undefined}
          disabled={busy}
          onclick={() => onOpen(conversation.id)}
        >
          <strong>{conversation.title}</strong>
          {#if conversation.aboutLabel}
            <span class="pill">About {conversation.aboutLabel}</span>
          {/if}
          <span class="muted"
            >You last added to it on {lastAdded(conversation.updatedAt)}.</span
          >
        </button>
      </li>
    {/each}
  </ul>
  {#if conversations.length > 0}
    <button class="secondary danger" onclick={onDeleteAll} disabled={busy}
      ><Trash2 size={18} aria-hidden="true" /> Delete all your conversations</button
    >
  {/if}
{/if}
