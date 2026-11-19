<script lang="ts">
  import RotateCcw from "@lucide/svelte/icons/rotate-ccw";
  import ArrowLeft from "@lucide/svelte/icons/arrow-left";
  import History from "@lucide/svelte/icons/history";
  import Info from "@lucide/svelte/icons/info";
  import Pencil from "@lucide/svelte/icons/pencil";
  import Plus from "@lucide/svelte/icons/plus";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import Camera from "@lucide/svelte/icons/camera";
  import Coins from "@lucide/svelte/icons/coins";
  import SendHorizontal from "@lucide/svelte/icons/send-horizontal";
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import X from "@lucide/svelte/icons/x";
  import { tick } from "svelte";
  import type { Schemas } from "../lib/types";
  import AskConversations from "./AskConversations.svelte";
  import { api } from "../api/client";
  import {
    answerBlocks,
    assistant,
    dollars,
    type AskAbout,
    type AskSource,
  } from "../lib/assistant.svelte";
  import { isOn } from "../lib/features.svelte";
  import { shrinkPhoto } from "../lib/photo";

  // Ask Hortiplanner: questions about plants, the nursery's records and photos of plants. Signed in as themselves, a
  // person's conversations are kept on the server for them alone; otherwise the conversation stays on this device for
  // the session. Either way the server keeps what each question cost, who asked and when.
  let {
    me,
    about = null,
    onOpenTray,
    onOpenPlace,
  }: {
    me: { id: string };
    about?: AskAbout | null;
    onOpenTray: (label: string) => void;
    onOpenPlace: (id: string) => void;
  } = $props();

  type Line = {
    role: "user" | "assistant";
    text: string;
    photos?: string[];
    /** Photos sent with a kept question, which aren't kept themselves. */
    photoCount?: number;
    sources?: AskSource[];
    cost?: number;
  };

  const storageKey = $derived(`hortiplanner.ask.${me.id}`);
  const finePointer = matchMedia("(pointer: fine)").matches;
  const mostPhotos = 4;

  // Asking about something on screen starts a new conversation, so the question goes with that alone. A kept
  // conversation comes from the server, not this device.
  // svelte-ignore state_referenced_locally
  let lines = $state<Line[]>(
    about || assistant.savesConversations ? [] : remembered(),
  );
  const saving = $derived(assistant.savesConversations);
  let conversationId = $state<string | null>(null);
  let title = $state("");
  let conversations = $state<Schemas["ConversationSummary"][]>([]);
  let listLoaded = $state(false);
  let showingList = $state(false);
  let renaming = $state(false);
  let newTitle = $state("");
  let showingPrivacy = $state(false);
  let privacyCard = $state<HTMLDivElement>();
  let noticeSeen = $state(seenNotice());
  let question = $state("");
  let photos = $state<string[]>([]);
  // What the question is about, until it's been asked once. Taken once, when the screen opens.
  // svelte-ignore state_referenced_locally
  let context = $state<AskAbout | null>(about);
  let busy = $state(false);
  let problem = $state("");
  let list = $state<HTMLOListElement | null>(null);
  let box = $state<HTMLTextAreaElement | null>(null);

  function remembered(): Line[] {
    try {
      const kept: unknown = JSON.parse(
        sessionStorage.getItem(`hortiplanner.ask.${me.id}`) ?? "[]",
      );
      return Array.isArray(kept) ? (kept as Line[]) : [];
    } catch {
      return [];
    }
  }

  function keep() {
    try {
      // A kept conversation is on the server: this device only remembers which one is open.
      if (saving) {
        if (conversationId) {
          sessionStorage.setItem(openKey(), conversationId);
        } else {
          sessionStorage.removeItem(openKey());
        }
        return;
      }

      // Photos are left out: they're large, and the answer already says what was in them.
      sessionStorage.setItem(
        storageKey,
        JSON.stringify(lines.map((line) => ({ ...line, photos: undefined }))),
      );
    } catch {
      // Without storage the conversation lasts until the screen is left.
    }
  }

  const openKey = () => `hortiplanner.ask.open.${me.id}`;
  const noticeKey = () => `hortiplanner.ask.kept-notice.${me.id}`;

  function seenNotice() {
    try {
      return (
        localStorage.getItem(`hortiplanner.ask.kept-notice.${me.id}`) === "seen"
      );
    } catch {
      return false;
    }
  }

  function dismissNotice() {
    noticeSeen = true;
    try {
      localStorage.setItem(noticeKey(), "seen");
    } catch {
      // Without storage it shows again next time, which does no harm.
    }
  }

  async function loadList() {
    try {
      conversations =
        (await api.GET("/api/assistant/conversations")).data ?? [];
      listLoaded = true;
    } catch {
      // Without a connection the list waits; the conversation open still works.
    }
  }

  // Started once it's known that conversations are kept, which may be after the screen opens, as after a reload.
  let started = false;
  $effect(() => {
    if (saving && !started) {
      started = true;
      void start();
    }
  });

  async function start() {
    await loadList();
    let open: string | null;
    try {
      open = sessionStorage.getItem(openKey());
    } catch {
      open = null;
    }

    if (open && !about) {
      await openConversation(open);
    }
  }

  async function openConversation(id: string) {
    // Nothing changes conversation while an answer is on its way, or it would land in the wrong one.
    if (busy) {
      return;
    }

    problem = "";

    try {
      const { data } = await api.GET("/api/assistant/conversations/{id}", {
        params: { path: { id } },
      });

      if (!data) {
        problem =
          "That conversation isn't here any more. It may have been deleted.";
        conversationId = null;
        keep();
        await loadList();
        return;
      }

      conversationId = data.id;
      title = data.title;
      lines = data.messages.map((message) => ({
        role: message.role === "assistant" ? "assistant" : "user",
        text: message.text,
        photoCount: Number(message.photoCount),
        sources: message.sources,
        cost: message.cost == null ? undefined : Number(message.cost),
      }));
      context = null;
      showingList = false;
      renaming = false;
      keep();
      await scrollDown();
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    }
  }

  async function saveTitle(event: SubmitEvent) {
    event.preventDefault();
    const wanted = newTitle.trim();

    if (!conversationId || !wanted) {
      return;
    }

    try {
      const { response } = await api.PUT(
        "/api/assistant/conversations/{id}/title",
        { params: { path: { id: conversationId } }, body: { title: wanted } },
      );

      if (response.ok) {
        title = wanted;
        renaming = false;
        problem = "";
        await loadList();
      } else {
        problem = "That name couldn't be saved. Please try again.";
      }
    } catch {
      problem =
        "That name couldn't be saved. Please check the connection and try again.";
    }
  }

  async function deleteThis() {
    if (
      busy ||
      !conversationId ||
      !confirm("Delete this conversation? It can't be brought back.")
    ) {
      return;
    }

    try {
      const { response } = await api.DELETE(
        "/api/assistant/conversations/{id}",
        { params: { path: { id: conversationId } } },
      );

      if (!response.ok && response.status !== 404) {
        problem = couldntDelete;
        return;
      }
    } catch {
      problem = couldntDelete;
      return;
    }

    startAgain();
    await loadList();
  }

  const couldntDelete =
    "That couldn't be deleted. Please check the connection and try again.";

  async function deleteAll() {
    const count = conversations.length;

    if (busy) {
      return;
    }

    if (
      !confirm(
        count === 1
          ? "Delete your conversation? It can't be brought back."
          : `Delete all your conversations? There are ${count}, and they can't be brought back.`,
      )
    ) {
      return;
    }

    // On a phone the list stays open, now empty, so it's plain they've gone.
    const wasShowing = showingList;

    try {
      const { response } = await api.DELETE("/api/assistant/conversations");

      if (!response.ok) {
        problem = couldntDelete;
        return;
      }
    } catch {
      problem = couldntDelete;
      return;
    }

    startAgain();
    await loadList();
    showingList = wasShowing;
  }

  // Every source looked at across the conversation, for the side panel on a desk.
  const seen = $derived(
    lines
      .flatMap((line) => line.sources ?? [])
      .filter(
        (source, index, all) =>
          all.findIndex(
            (other) => other.kind === source.kind && other.key === source.key,
          ) === index,
      ),
  );

  // Questions to start with, to suit what's being asked about.
  const suggestions = $derived(
    (context?.kind === "tray"
      ? ["How is this tray doing?", "What should I do with this tray next?"]
      : context?.kind === "plant"
        ? [
            "How do I look after this plant?",
            "When and how should I propagate this plant?",
          ]
        : context?.kind === "place"
          ? [
              "What needs doing here this week?",
              "Has anything here been waiting too long?",
              isOn("Climate") ? "Has it been too cold or too hot here?" : null,
            ]
          : context?.kind === "photo"
            ? [
                context.placePhotoId
                  ? "What can you see in this photo?"
                  : "What can you see in this photo, and is anything wrong?",
              ]
            : [
                "What needs attention this week?",
                "Which sown trays have waited longest without germinating?",
                "What did we lose in the last month, and why?",
                isOn("Climate")
                  ? "How cold did the greenhouses get this week?"
                  : null,
                "How do I take lavender cuttings, and when?",
              ]
    ).filter((item): item is string => item !== null),
  );

  async function addPhoto(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const files = [...(input.files ?? [])].slice(0, mostPhotos - photos.length);
    input.value = "";

    for (const file of files) {
      const shrunk = await shrinkPhoto(file, 1600);
      photos = [
        ...photos,
        await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result));
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(shrunk);
        }),
      ];
    }
  }

  const refusals: Record<string, string> = {
    NotSetUp:
      "Ask Hortiplanner isn't set up yet. The owner adds an OpenRouter key and chooses a model under Settings, AI assistant.",
    TooMany:
      "That's a lot of questions this hour. Please wait a while before asking more.",
    KeyRefused:
      "OpenRouter turned the key away: it may be out of credit or past its limit. The owner can check it at openrouter.ai.",
    ModelUnavailable:
      "The chosen AI model can't answer questions here. The owner can choose another under Settings, AI assistant.",
    Unavailable: "The AI service didn't answer. Please try again in a minute.",
    ConversationFull:
      "This conversation is as long as it can be. Please start a new one to carry on.",
  };

  async function ask(text = question) {
    const wanted = text.trim();

    if (!wanted || busy) {
      return;
    }

    problem = "";
    busy = true;
    // Asked from the list on a phone, the conversation comes back into view to show the answer arriving.
    showingList = false;
    const sent = photos;
    lines = [...lines, { role: "user", text: wanted, photos: sent }];
    question = "";
    photos = [];
    await scrollDown();

    try {
      const { data, response, error } = await api.POST("/api/assistant/ask", {
        body: {
          // A kept conversation's history is on the server, so only the new question goes. Otherwise the latest
          // lines are enough for the model to follow, and keep a long conversation working.
          messages: (saving ? lines.slice(-1) : lines.slice(-12)).map(
            (line) => ({
              role: line.role,
              text: line.text.slice(0, 4000),
            }),
          ),
          conversationId: saving ? conversationId : null,
          images: sent.length > 0 ? sent : null,
          about: context
            ? {
                trayLabel: context.trayLabel ?? null,
                plantId: context.plantId ?? null,
                photoId: context.photoId ?? null,
                plantPhotoId: context.plantPhotoId ?? null,
                placeId: context.placeId ?? null,
                placePhotoId: context.placePhotoId ?? null,
              }
            : null,
        },
      });

      if (data) {
        lines = [
          ...lines,
          {
            role: "assistant",
            text: data.answer,
            sources: data.sources,
            cost: Number(data.cost),
          },
        ];
        assistant.spentThisMonth = Number(data.spentThisMonth ?? 0);
        assistant.spentInAll = Number(data.spentInAll ?? 0);
        context = null;

        if (data.conversationId) {
          conversationId = data.conversationId;
          title = data.title ?? title;
          void loadList();
        }

        keep();
      } else {
        const reason = (error as { problem?: string } | undefined)?.problem;
        problem =
          (reason && refusals[reason]) ??
          (response.status === 403
            ? "The owner hasn't let staff use Ask Hortiplanner."
            : response.status === 404 && conversationId
              ? "That conversation isn't here any more. It may have been deleted. Your question is still in the box, so you can ask it in a new conversation."
              : "That couldn't be asked. Please try again.");

        if (response.status === 404) {
          conversationId = null;
          title = "";
          keep();
        }
        // The question goes back in the box, so it isn't lost. A conversation that has gone takes its lines with it.
        lines = response.status === 404 ? [] : lines.slice(0, -1);
        question = wanted;
        photos = sent;
      }
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
      lines = lines.slice(0, -1);
      question = wanted;
      photos = sent;
    } finally {
      busy = false;
      await scrollDown();

      // On a phone, focusing would bring the keyboard back over the answer.
      if (finePointer) {
        box?.focus();
      }
    }
  }

  async function scrollDown() {
    await tick();
    list?.lastElementChild?.scrollIntoView({
      block: "end",
      behavior: "smooth",
    });
  }

  function startAgain() {
    if (busy) {
      return;
    }

    lines = [];
    context = null;
    problem = "";
    conversationId = null;
    title = "";
    renaming = false;
    showingList = false;
    keep();
  }

  function openSource(source: AskSource) {
    if (source.kind === "tray") {
      onOpenTray(source.key);
    } else if (source.kind === "place") {
      onOpenPlace(source.key);
    }
  }

  const money = (cost: number) => dollars(cost);
</script>

<!-- What goes to the model, and what Hortiplanner keeps: on a desk in the side panel, on a phone behind a button. -->
{#snippet privacy()}
  <p class="muted">
    Your question goes to the model the owner chose through OpenRouter, asking
    for providers that keep nothing. With it go plants, places, trays, work,
    losses, treatments, climate, notes as they were typed, and the photos you
    add or ask about. It is never sent customers, money, where places are on the
    map, or who recorded the work.{saving
      ? " When you carry on a conversation, its earlier questions and answers go too, and if you mention something from before, Ask Hortiplanner can look through your own earlier conversations for it."
      : ""}
  </p>
  {#if saving}
    <p class="muted">
      Hortiplanner keeps your conversations so you can come back to them. Only
      you can open them: nobody else who uses Hortiplanner can, the owner
      included. They are stored with the nursery's information and are in its
      backups, so whoever looks after the server could reach them, and it's best
      not to put anything private in a question. Photos you send aren't kept.
      You can keep up to 100 conversations. After that, starting a new one
      removes the one you added to longest ago. A conversation goes when you
      delete it, or a year after you last added to it. The owner sees what your
      questions cost and when you asked, but never what you asked.
    </p>
  {:else}
    <p class="muted">
      Hortiplanner's server keeps what each question cost, who asked and when,
      but not what was asked. Your conversations aren't kept here, because other
      people can sign in this way on this device.
    </p>
  {/if}
{/snippet}

<!-- What this person's questions have cost: the nursery pays for every one, so it's always in view. -->
{#snippet spend(compact: boolean)}
  {#if assistant.spentThisMonth !== null}
    {#if compact}
      <p class="ask-spend" role="status">
        <Coins size={16} aria-hidden="true" />
        <span
          >Your questions have cost <strong
            >{dollars(assistant.spentThisMonth)}</strong
          > this month, and the nursery pays for them.</span
        >
      </p>
    {:else}
      <div class="card stack tight ask-spend-card">
        <h2 class="form-heading">What your questions cost</h2>
        <p>
          They have cost <strong>{dollars(assistant.spentThisMonth)}</strong>
          this month and <strong>{dollars(assistant.spentInAll ?? 0)}</strong> in
          all.
        </p>
        <p class="muted">
          The nursery pays for every question, and the owner can see this total
          too. Photos, long conversations and questions that need a lot looked
          up cost more.
        </p>
      </div>
    {/if}
  {/if}
{/snippet}

{#snippet sourceList(sources: AskSource[])}
  <ul class="ask-sources">
    {#each sources as source (`${source.kind}/${source.key}`)}
      <li>
        {#if source.kind === "tray" || source.kind === "place"}
          <button class="pill-button" onclick={() => openSource(source)}
            >{source.label}</button
          >
        {:else}
          <span class="pill">{source.label}</span>
        {/if}
      </li>
    {/each}
  </ul>
{/snippet}

<main class="ask">
  <div class="ask-layout">
    <section class="ask-conversation" aria-label="Conversation">
      <div class="title-row">
        <h1><Sparkles size={26} aria-hidden="true" /> Ask Hortiplanner</h1>
        {#if saving}
          <div class="row">
            {#if lines.length > 0 || conversationId}
              <button class="link-button" onclick={startAgain} disabled={busy}
                ><Plus size={16} aria-hidden="true" />New conversation</button
              >
            {/if}
            {#if !showingList}
              <button
                class="link-button mobile-only"
                onclick={() => {
                  showingList = true;
                  void loadList();
                }}
                ><History size={16} aria-hidden="true" />Your conversations</button
              >
            {/if}
          </div>
        {:else if lines.length > 0}
          <button class="link-button" onclick={startAgain}
            ><RotateCcw size={16} aria-hidden="true" />Start again</button
          >
        {/if}
      </div>
      {#if assistant.spentThisMonth !== null}
        <div class="mobile-only">{@render spend(true)}</div>
      {/if}
      <div class="mobile-only">
        <button
          class="link-button"
          aria-expanded={showingPrivacy}
          onclick={() => (showingPrivacy = !showingPrivacy)}
          ><Info size={16} aria-hidden="true" />What is sent and what is kept</button
        >
        {#if showingPrivacy}
          <div class="card stack tight" bind:this={privacyCard}>
            {@render privacy()}
          </div>
        {/if}
      </div>

      {#if saving && showingList}
        <section class="stack" aria-labelledby="your-conversations">
          <div class="title-row">
            <h2 id="your-conversations">Your conversations</h2>
            <button class="link-button" onclick={() => (showingList = false)}
              ><ArrowLeft size={16} aria-hidden="true" />Back to asking</button
            >
          </div>
          <AskConversations
            {conversations}
            current={conversationId}
            loaded={listLoaded}
            onOpen={openConversation}
            onDeleteAll={deleteAll}
            {busy}
          />
        </section>
      {:else}
        {#if conversationId}
          {#if renaming}
            <form class="row ask-rename" onsubmit={saveTitle}>
              <label class="grow"
                >Name of this conversation
                <input bind:value={newTitle} maxlength="80" required />
              </label>
              <button type="submit">Save the name</button>
              <button
                type="button"
                class="secondary"
                onclick={() => (renaming = false)}>Cancel</button
              >
            </form>
          {:else}
            <div class="row ask-title">
              <h2 class="form-heading grow">{title}</h2>
              <button
                class="link-button"
                onclick={() => {
                  newTitle = title;
                  renaming = true;
                }}><Pencil size={16} aria-hidden="true" />Rename</button
              >
              <button
                class="link-button danger-link"
                onclick={deleteThis}
                disabled={busy}
                ><Trash2 size={16} aria-hidden="true" />Delete this conversation</button
              >
            </div>
          {/if}
        {/if}
        {#if !saving && assistant.canAsk}
          <p class="muted">
            Your conversations aren't kept here, because other people can sign
            in this way on this device. Sign in with your own password, passkey
            or staff login to keep them.
          </p>
        {/if}

        {#if lines.length === 0}
          <p class="muted">
            Ask about plants, or about the nursery's own trays, losses,
            treatments{isOn("Climate") ? ", climate" : ""} and jobs. It reads the
            records but never changes them, and it can be wrong, so check before acting
            on it.
          </p>
          {#if context}
            <p class="muted">
              {context.kind === "photo"
                ? `Hortiplanner sends this photo, and what it knows about ${context.subject ?? context.title}, with your first question.`
                : `Hortiplanner sends what it knows about ${context.title} with your first question.`}
            </p>
          {/if}
          <div class="ask-suggestions mobile-only">
            {#each suggestions as suggestion (suggestion)}
              <button class="secondary" onclick={() => ask(suggestion)}
                >{suggestion}</button
              >
            {/each}
          </div>
        {/if}

        <ol class="ask-lines" bind:this={list} aria-live="polite">
          {#each lines as line, index (index)}
            <li class="ask-line {line.role}">
              {#if line.role === "user"}
                <p class="ask-question">{line.text}</p>
                {#if line.photoCount && !line.photos?.length}
                  <p class="muted">
                    {line.photoCount === 1
                      ? "You sent a photo with this question."
                      : `You sent ${line.photoCount} photos with this question.`}
                    Hortiplanner doesn't keep photos.
                  </p>
                {/if}
                {#if line.photos?.length}
                  <div class="ask-photos">
                    {#each line.photos as photo, photoIndex (photoIndex)}
                      <img src={photo} alt="Sent with the question" />
                    {/each}
                  </div>
                {/if}
              {:else}
                <div class="ask-answer card">
                  {#each answerBlocks(line.text) as block, blockIndex (blockIndex)}
                    {#if block.kind === "list"}
                      <ul>
                        {#each block.items as item, itemIndex (itemIndex)}<li>
                            {item}
                          </li>{/each}
                      </ul>
                    {:else}
                      <p>{block.text}</p>
                    {/if}
                  {/each}
                  {#if line.sources?.length}
                    <div class="ask-looked mobile-only">
                      <span class="muted">It looked at</span>
                      {@render sourceList(line.sources)}
                    </div>
                  {/if}
                  {#if line.cost != null}
                    <span class="muted ask-cost"
                      >This answer cost {money(line.cost)}.</span
                    >
                  {/if}
                </div>
              {/if}
            </li>
          {/each}
          {#if busy}
            <li class="ask-line assistant">
              <p class="ask-answer card muted" role="status">
                Looking through the records…
              </p>
            </li>
          {/if}
        </ol>
      {/if}

      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#if saving && !noticeSeen}
        <div class="card stack tight ask-notice">
          <p>
            Hortiplanner now keeps your conversations so you can come back to
            them. Only you can open them, and you can delete them whenever you
            like.
          </p>
          <div class="row">
            <button onclick={dismissNotice}>I understand</button>
            <button
              class="secondary mobile-only"
              onclick={async () => {
                showingPrivacy = true;
                dismissNotice();
                await tick();
                privacyCard?.scrollIntoView({ block: "start" });
              }}>What is sent and what is kept</button
            >
          </div>
        </div>
      {/if}

      <form
        class="ask-composer"
        onsubmit={(event) => {
          event.preventDefault();
          void ask();
        }}
      >
        {#if context}
          <p class="ask-context">
            {#if context.photoUrl}
              <img
                class="ask-context-photo"
                src={context.photoUrl}
                alt="Sent with your first question"
              />
            {/if}
            <span class="pill">About {context.title}</span>
            <button
              type="button"
              class="icon-button"
              aria-label="Stop asking about {context.title}"
              onclick={() => (context = null)}><X size={16} /></button
            >
          </p>
        {/if}
        {#if photos.length > 0}
          <div class="ask-photos">
            {#each photos as photo, index (index)}
              <span class="ask-photo">
                <img src={photo} alt="To send with the question" />
                <button
                  type="button"
                  class="icon-button"
                  aria-label="Remove this photo"
                  onclick={() =>
                    (photos = photos.filter((_, other) => other !== index))}
                  ><X size={16} /></button
                >
              </span>
            {/each}
          </div>
        {/if}
        <div class="ask-input">
          {#if assistant.readsImages && photos.length < mostPhotos}
            <label class="icon-button ask-camera" title="Add a photo">
              <Camera size={22} />
              <span class="visually-hidden">Add a photo</span>
              <input
                type="file"
                accept="image/*"
                multiple
                onchange={addPhoto}
              />
            </label>
          {/if}
          <label class="grow">
            <span class="visually-hidden">Your question</span>
            <textarea
              bind:this={box}
              bind:value={question}
              rows="2"
              maxlength="4000"
              placeholder={photos.length > 0 || context?.photoUrl
                ? "What would you like to know about the photo?"
                : "Ask about plants or the nursery…"}
              onkeydown={(event) => {
                // On a desk Enter asks; on a phone it starts a new line, and the button asks.
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.isComposing &&
                  finePointer
                ) {
                  event.preventDefault();
                  void ask();
                }
              }}></textarea>
          </label>
          <button
            type="submit"
            class="ask-send"
            disabled={busy || !question.trim()}
            aria-label="Ask"><SendHorizontal size={22} /></button
          >
        </div>
      </form>
    </section>

    <aside class="ask-side desktop-only">
      {#if saving}
        <div class="card stack tight ask-conversations-card">
          <h2 class="form-heading">Your conversations</h2>
          <AskConversations
            {conversations}
            current={conversationId}
            loaded={listLoaded}
            onOpen={openConversation}
            onDeleteAll={deleteAll}
            {busy}
          />
        </div>
      {/if}
      {@render spend(false)}
      {#if seen.length > 0}
        <div class="card stack tight">
          <h2 class="form-heading">What it looked at</h2>
          {@render sourceList(seen)}
        </div>
      {/if}
      <div class="card stack tight">
        <h2 class="form-heading">Try asking</h2>
        {#each suggestions as suggestion (suggestion)}
          <button
            class="link-button ask-suggestion"
            onclick={() => ask(suggestion)}>{suggestion}</button
          >
        {/each}
      </div>
      <div class="card stack tight">
        <h2 class="form-heading">What is sent and what is kept</h2>
        {@render privacy()}
      </div>
    </aside>
  </div>
</main>
