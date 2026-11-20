<script lang="ts">
  import { crumbs } from "../../lib/crumbs.svelte";
  import { isOn } from "../../lib/features.svelte";
  import ChevronRight from "@lucide/svelte/icons/chevron-right";
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { nestLocations } from "../../lib/locations";
  import { kindLabel, type AddedPlace, type Place } from "../../lib/places";
  import { attempt } from "../../lib/types";
  import type { AskAbout } from "../../lib/assistant.svelte";
  import PlaceView from "../work/PlaceView.svelte";
  import AddPlaces from "./AddPlaces.svelte";
  import ArchivedEntries from "./ArchivedEntries.svelte";
  import PlaceOnMap from "./PlaceOnMap.svelte";

  let {
    onBack,
    onOpenTray,
    onMoveTraysHere,
    onAsk,
  }: {
    /** Asks Ask Hortiplanner about a place or one of its photos, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
    onBack: () => void;
    onOpenTray: (label: string) => void;
    onMoveTraysHere: (id: string) => void;
  } = $props();

  let places = $state<Place[]>([]);
  let openId = $state<string | null>(null);
  // A place being drawn on the map, just added or opened from its own screen, and where to go back to after.
  let drawing = $state<AddedPlace | null>(null);
  let backTo = $state<string | null>(null);

  // A place being drawn adds itself to the trail; an open place adds its own.
  crumbs(() =>
    drawing
      ? [
          {
            label: drawing.name,
            go: backTo ? doneDrawing : undefined,
          },
          { label: "On the map" },
        ]
      : [],
  );

  function drawOnMap(place: AddedPlace, from: string | null) {
    backTo = from;
    openId = null;
    drawing = place;
  }

  function doneDrawing() {
    drawing = null;
    openId = backTo;
    backTo = null;
    void attempt(load, setProblem);
  }

  let problem = $state("");

  const nested = $derived(nestLocations(places));
  const setProblem = (message: string) => (problem = message);

  async function load() {
    places = (await api.GET("/api/locations")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));
</script>

{#if drawing}
  <main class="wide-map">
    <h1>Places</h1>
    <PlaceOnMap
      place={drawing}
      {places}
      onSaved={doneDrawing}
      onSkip={doneDrawing}
      skipLabel={backTo ? "Cancel" : "Skip the map for now"}
    />
  </main>
{:else if openId}
  {#key openId}
    <PlaceView
      placeId={openId}
      canManage
      onDrawOnMap={(place) => drawOnMap(place, openId)}
      onBack={() => {
        openId = null;
        void attempt(load, setProblem);
      }}
      {onOpenTray}
      onOpenPlace={(id) => (openId = id)}
      {onMoveTraysHere}
      {onAsk}
    />
  {/key}
{:else}
  <main>
    <h1>Places</h1>

    <div class="split">
      <section>
        <FormPanel label="Add a place">
          <div class="form-card stack">
            <h2 class="form-heading">Add a place</h2>
            <AddPlaces
              {places}
              onAdded={async (added) => {
                await load();

                // The map is the next step for a single new place.
                if (added && isOn("PlacesMap")) {
                  drawOnMap(added, null);
                }
              }}
            />
          </div>
        </FormPanel>
      </section>
      <section>
        {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

        <ul class="place-tree place-list">
          {#each nested as { place, depth } (place.id)}
            <li style:--depth={depth}>
              <button class="place-row" onclick={() => (openId = place.id)}>
                <span class="plant-names">
                  <strong>{place.name}</strong>
                  <em>{kindLabel(place.kind)} · {place.labelCode}</em>
                </span>
                {#if place.photoIds.length > 0}
                  {@const stack = place.photoIds.slice(-3).reverse()}
                  <span
                    class="photo-stack"
                    style:--cards={stack.length}
                    role="img"
                    aria-label="{place.photoIds.length} photo{place.photoIds
                      .length === 1
                      ? ''
                      : 's'}"
                  >
                    {#each stack as photoId, index (photoId)}
                      <img
                        src="/api/catalogue-photos/{photoId}"
                        alt=""
                        loading="lazy"
                        style:--card={index}
                      />
                    {/each}
                    {#if place.photoIds.length > stack.length}
                      <span class="photo-stack-more"
                        >+{place.photoIds.length - stack.length}</span
                      >
                    {/if}
                  </span>
                {/if}
                <ChevronRight size={18} />
              </button>
            </li>
          {/each}
        </ul>
        {#if nested.length === 0}
          <p class="note-empty muted">
            There are no places yet. Start with the site, such as the farm.
          </p>
        {/if}
        <ArchivedEntries list="locations" onRestored={load} />
      </section>
    </div>

    <button class="back mobile-only" onclick={onBack}>Back</button>
  </main>
{/if}
