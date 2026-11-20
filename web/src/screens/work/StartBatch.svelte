<script lang="ts">
  import { isOn } from "../../lib/features.svelte";
  import Camera from "@lucide/svelte/icons/camera";
  import { onDestroy, onMount, tick } from "svelte";
  import {
    focusHeading,
    onStepChange,
    pushStep,
    stepOf,
  } from "../../lib/steps";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import {
    money,
    outcomeProblem,
    plantName,
    submit,
    retainIdentity,
    work,
    workPlaces,
    type BatchPrefill,
    type PickedPlant,
  } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import PlantSearch from "./PlantSearch.svelte";
  import PlantThumb from "./PlantThumb.svelte";
  import ReadLabel from "./ReadLabel.svelte";
  import Stepper from "./Stepper.svelte";
  import SowByPicker from "./SowByPicker.svelte";
  import SupplierRegistrations from "./SupplierRegistrations.svelte";
  import { sowByWords } from "../../lib/sowBy";
  import { moneyProblem, readMoney } from "../../lib/costs";
  import {
    packetShares,
    sowingChoices,
    sowingWords,
    startsCounted,
    type PacketShare,
    type SownMeasure,
  } from "../../lib/sowing";
  import DayPicker from "../DayPicker.svelte";
  import { isoDay, momentOn, onDay } from "../../lib/days";
  import { originCountries } from "../../lib/countries";

  type PhotoKind = Schemas["BatchPhotoKind"] | "Plant";

  let {
    prefill,
    onDone,
    onCancel,
  }: {
    prefill?: BatchPrefill;
    onDone: (message: string, eventId: string, newLabel?: string) => void;
    onCancel: () => void;
  } = $props();

  const stages = [
    "Find the plant",
    "How many?",
    "Where from?",
    "Photos",
    "Where is it going?",
    "Check and start",
  ];

  const containerKinds: Record<Schemas["ContainerKind"], string> = {
    Tray: "Tray",
    PotGroup: "Group of pots",
    BenchLot: "Bench lot",
  };

  const photoChoices: { kind: PhotoKind; label: string }[] = [
    { kind: "Packet", label: "Seed packet" },
    { kind: "Label", label: "Label" },
    { kind: "Packaging", label: "Packaging" },
    { kind: "Plant", label: "The plant itself" },
  ];

  const sources: { source: Schemas["BatchSource"]; label: string }[] = [
    { source: "SeedLot", label: "Seed" },
    { source: "BoughtIn", label: "Bought in" },
    { source: "Cuttings", label: "Cuttings" },
  ];

  let plants = $state<Schemas["PlantResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);

  let stage = $state(1);
  let picked = $state<PickedPlant | null>(null);
  let details = $state<Schemas["SpeciesDetailsResponse"] | null>(null);
  let variety = $state("");
  let plantId = $state("");
  // The species pick and variety a plant was made for, so a changed variety makes a new plant.
  let plantMadeFor = "";
  // Set while the browser moves back to a step, so a second tap can't move it twice.
  let moving = false;
  let quantity = $state(1);
  let source = $state<Schemas["BatchSource"]>("SeedLot");
  let sourceReference = $state("");
  let supplierId = $state("");
  let newSupplier = $state("");
  let newPassport = $state("");
  let newOther = $state("");
  let sowBy = $state("");
  // What the seed or plants cost, if anyone knows. Optional, and the owner can add it later.
  let cost = $state("");
  let costNothing = $state(false);
  // A cost that isn't money is pointed out only once Next is pressed, not while it's being typed.
  let costChecked = $state(false);
  let costBox = $state<HTMLInputElement>();
  // Cuttings: the mother plant they're taken from, for plants already on the list.
  let mothers = $state<Schemas["MotherSummary"][]>([]);
  let motherId = $state("");
  let newMother = $state("");

  $effect(() => {
    const id = plantId;
    motherId = "";
    mothers = [];

    if (id && isOn("MotherPlants")) {
      void api
        .GET("/api/mother-plants", { params: { query: { plantId: id } } })
        .then(({ data }) => (mothers = data ?? []))
        .catch(() => (mothers = []));
    }
  });

  const addMother = () =>
    attempt(async () => {
      const { data, response } = await api.POST("/api/mother-plants", {
        body: {
          plantId,
          name: newMother.trim(),
          locationId: null,
          notes: null,
        },
      });

      if (data) {
        mothers =
          (
            await api.GET("/api/mother-plants", {
              params: { query: { plantId } },
            })
          ).data ?? mothers;
        motherId = data.id;
        newMother = "";
      } else {
        problem =
          response.status === 409
            ? "That plant already has a mother with that name."
            : "Please give the mother plant a name.";
      }
    }, setProblem);
  // How the seed was measured; only "I counted them" starts the tray with a number.
  let sownAs = $state<SownMeasure>("Counted");
  let aboutSeeds = $state(200);
  let grams = $state("");
  let packetShare = $state<PacketShare>("Whole");
  let packetSays = $state("");

  const seed = $derived(source === "SeedLot");
  const counting = $derived(!seed || startsCounted(sownAs));
  const sowingReady = $derived(
    counting
      ? quantity >= 1
      : sownAs === "Grams"
        ? Number(grams) > 0
        : sownAs === "About"
          ? aboutSeeds >= 1
          : true,
  );
  const sowing = $derived(
    sowingWords({
      sownAs: seed ? sownAs : null,
      sownAmount: sownAs === "About" ? aboutSeeds : Number(grams),
      packetShare,
      packetSays: packetSays.trim() || null,
    }),
  );
  let locationId = $state("");
  let potSizeId = $state("");
  let containerKind = $state<Schemas["ContainerKind"]>("Tray");
  // Grown here starts as Great Britain; bought-in plants must have their origin chosen.
  let originCountry = $state("GB");
  let otherCountry = $state(false);
  // Sown, arrived or taken today unless someone says otherwise: work is often written up later.
  let startedOn = $state(isoDay(new Date()));

  const whenAsked: Record<Schemas["BatchSource"], string> = {
    SeedLot: "When were they sown?",
    BoughtIn: "When did they arrive?",
    Cuttings: "When were the cuttings taken?",
  };
  const started: Record<Schemas["BatchSource"], string> = {
    SeedLot: "Sown",
    BoughtIn: "Arrived",
    Cuttings: "Cuttings taken",
  };

  function chooseSource(chosen: Schemas["BatchSource"]) {
    // A cost given for seed isn't the cost of bought-in plants, nor the other way round.
    if (chosen !== source) {
      cost = "";
      costNothing = false;
      costChecked = false;
    }

    source = chosen;

    // Only seed can go in uncounted: plants and cuttings are always a number.
    if (chosen !== "SeedLot" && !startsCounted(sownAs)) {
      sownAs = "Counted";
      quantity = Math.max(1, quantity);
    }

    if (chosen === "BoughtIn") {
      originCountry = "";
      // Suppliers come most recently used first.
      supplierId = supplierId || (suppliers[0]?.id ?? "");
    } else if (!originCountry) {
      originCountry = "GB";
    }
  }
  let photos = $state<Partial<Record<PhotoKind, { blob: Blob; url: string }>>>(
    {},
  );
  let problem = $state("");
  let busy = $state(false);
  // What was read from a seed packet photo, and which fields it filled in, until the person changes them.
  let packet = $state<Schemas["LabelRead"] | null>(null);
  let fromPhoto = $state<Record<string, boolean>>({});
  let searchFor = $state("");
  let searchSeed = $state(0);
  // Whether the packet photo came from reading a packet, rather than being taken by hand at Photos.
  let packetPhotoRead = false;
  // Whether reading a packet changed the source to Seed from something else, to say so.
  let madeSeed = $state(false);
  let packetNote = $state<HTMLParagraphElement>();
  const forget = (field: string) => (fromPhoto[field] = false);
  const packetName = $derived(
    packet ? (packet.commonName ?? packet.botanicalName ?? "") : "",
  );
  // The nursery's own plants that match the packet come first.
  const recentPlants = $derived(
    packet
      ? [
          ...packet.plantMatches,
          ...plants.filter(
            (plant) =>
              !packet!.plantMatches.some((match) => match.id === plant.id),
          ),
        ]
      : plants,
  );

  /** Fills in what was read from a seed packet: nothing is saved until Start batch. */
  async function usePacket(
    read: Schemas["LabelRead"],
    blob: Blob,
    keep: boolean,
  ) {
    const marks: Record<string, boolean> = {};

    // Whatever an earlier packet filled in goes, so nothing from it is left looking typed by hand.
    if (fromPhoto.sourceReference) sourceReference = "";
    if (fromPhoto.sowBy) sowBy = "";
    if (fromPhoto.packetSays) packetSays = "";
    if (fromPhoto.aboutSeeds) aboutSeeds = 200;
    if (fromPhoto.variety) variety = "";
    if (fromPhoto.cost) cost = "";
    if (fromPhoto.supplier) {
      supplierId = "";
      newSupplier = "";
      newPassport = "";
    }

    madeSeed = source !== "SeedLot";
    packet = read;
    chooseSource("SeedLot");

    if (read.contentsAsPrinted) {
      packetSays = read.contentsAsPrinted.slice(0, 200);
      marks.packetSays = true;
    }
    if (read.seedCount) {
      aboutSeeds = Number(read.seedCount);
      marks.aboutSeeds = true;
    }
    if (read.supplierId) {
      supplierId = read.supplierId;
      newSupplier = "";
      newPassport = "";
      marks.supplier = true;
    } else if (read.supplierName) {
      supplierId = "";
      newSupplier = read.supplierName;
      newPassport = read.supplierPassportNumber ?? "";
      marks.supplier = true;
    }
    if (read.lotOrReference) {
      sourceReference = read.lotOrReference;
      marks.sourceReference = true;
    }
    if (read.sowBy) {
      sowBy = read.sowBy;
      marks.sowBy = true;
    }
    // Filled in below, from the price and how much of the packet went in.
    if (read.price && isOn("Costs")) {
      costNothing = false;
      marks.cost = true;
    }
    if (keep) {
      const previous = photos.Packet;
      if (previous) {
        URL.revokeObjectURL(previous.url);
      }
      photos.Packet = { blob, url: URL.createObjectURL(blob) };
      packetPhotoRead = true;
    } else if (packetPhotoRead && photos.Packet) {
      // A photo kept from an earlier packet goes; one taken by hand stays.
      URL.revokeObjectURL(photos.Packet.url);
      delete photos.Packet;
      packetPhotoRead = false;
    }

    fromPhoto = marks;
    searchFor = packetName;
    searchSeed += 1;
    await tick();
    packetNote?.focus();
  }

  // The packet's price, for what this batch used of it: all of it, half, or a part nobody can price.
  const packetPrice = $derived(packet?.price ? Number(packet.price) : null);
  const usedOfPacket = $derived(
    seed && sownAs === "Packet" ? packetShare : "Whole",
  );
  $effect(() => {
    if (packetPrice !== null && fromPhoto.cost) {
      cost =
        usedOfPacket === "Half"
          ? (Math.round(packetPrice * 50) / 100).toFixed(2)
          : usedOfPacket === "Part"
            ? ""
            : packetPrice.toFixed(2);
    }
  });
  const packetPriceWords = $derived.by(() => {
    if (packetPrice === null || !fromPhoto.cost) {
      return "";
    }

    const price = money(packetPrice);
    return usedOfPacket === "Half"
      ? `The packet says ${price} and half of it went in, so ${money(Math.round(packetPrice * 50) / 100)} has been put in. Change it if that isn't right.`
      : usedOfPacket === "Part"
        ? `The packet says ${price} and part of it went in, so put in roughly what that part cost.`
        : `The packet says ${price}.`;
  });

  // A filled-in value changed by hand, however it was changed, is no longer "read from the photo".
  $effect(() => {
    if (packet && aboutSeeds !== Number(packet.seedCount ?? NaN)) {
      fromPhoto.aboutSeeds = false;
    }
  });
  $effect(() => {
    if (packet && sowBy !== (packet.sowBy ?? "")) {
      fromPhoto.sowBy = false;
    }
  });

  const setProblem = (message: string) => (problem = message);

  const confirming = $derived(stage === 1 && picked?.kind === "species");

  const chosenName = $derived.by(() => {
    if (picked?.kind === "species") {
      return plantName(
        picked.commonName ?? picked.scientificName,
        variety.trim(),
      );
    }

    return picked ? plantName(picked.commonName, picked.variety) : "";
  });

  onMount(() =>
    attempt(async () => {
      plants = (await api.GET("/api/plants")).data ?? [];
      suppliers = (await api.GET("/api/suppliers")).data ?? [];
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
      locationId = places[0]?.id ?? "";
      potSizeId = potSizes[0]?.id ?? "";

      if (prefill) {
        source = "BoughtIn";
        originCountry = "";
        supplierId = prefill.supplierId;
        startedOn =
          prefill.receivedOn && prefill.receivedOn <= startedOn
            ? prefill.receivedOn
            : startedOn;
        quantity = prefill.quantity;
      }
    }, setProblem),
  );

  onMount(() => onStepChange(stepped));

  onDestroy(() => {
    for (const photo of Object.values(photos)) {
      URL.revokeObjectURL(photo.url);
    }
  });

  function pick(choice: PickedPlant) {
    problem = "";
    picked = choice;
    details = null;
    plantId = choice.kind === "plant" ? choice.id : "";

    if (choice.kind === "species" && packet?.variety && !variety.trim()) {
      variety = packet.variety;
      fromPhoto.variety = true;
    }

    if (choice.kind === "species") {
      void attempt(async () => {
        const { data } = await api.GET("/api/species/{id}/details", {
          params: { path: { id: choice.id } },
        });

        details = data ?? null;
      }, setProblem);
    }

    if (choice.kind !== "species") {
      goTo(2);
    }
  }

  // Each step is a step in the browser's history, so the phone's Back button goes back a step,
  // keeping what was entered, rather than leaving New batch.
  function goTo(next: number) {
    stage = next;
    pushStep("batchStage", next);
    void focusHeading();
  }

  function stepped() {
    moving = false;
    const kept = stepOf<number>("batchStage") ?? 1;

    if (kept < stage) {
      stage = kept;

      if (stage === 1 && picked?.kind !== "species") {
        picked = null;
      }
    } else {
      stage = kept;
    }

    void focusHeading();
  }

  function back() {
    problem = "";

    if (stage > 1) {
      history.back();
    } else if (picked) {
      picked = null;
    } else {
      onCancel();
    }
  }

  const chosenSupplier = $derived(
    suppliers.find((item) => item.id === supplierId),
  );

  async function reloadSuppliers() {
    suppliers = (await api.GET("/api/suppliers")).data ?? [];
  }

  async function next() {
    problem = "";

    // A new mother plant typed in but not added yet is added now, rather than left behind.
    if (
      stage === 3 &&
      source === "Cuttings" &&
      isOn("MotherPlants") &&
      plantId &&
      !motherId &&
      newMother.trim()
    ) {
      await addMother();

      if (!motherId) {
        return;
      }
    }

    // A supplier typed in but not added would be lost: say so rather than carry on without them.
    if (
      stage === 3 &&
      source !== "Cuttings" &&
      !supplierId &&
      newSupplier.trim()
    ) {
      problem = `Press Add supplier to add ${newSupplier.trim()} to your suppliers, or clear the name to carry on without one.`;
      return;
    }

    if (stage === 3 && costProblem) {
      costChecked = true;
      costBox?.focus();
      return;
    }

    goTo(stage + 1);
  }

  // Back through the browser's history, so Back after changing something still behaves.
  function change(target: number) {
    if (moving) {
      return;
    }

    moving = true;
    problem = "";
    history.go(target - stage);
    // In case the browser never says it moved.
    setTimeout(() => (moving = false), 1000);
  }

  // Cuttings taken here cost nothing to buy, and a batch from a purchase takes its cost from there.
  const asksCost = $derived(
    isOn("Costs") && source !== "Cuttings" && !prefill?.purchaseLineId,
  );
  const paid = $derived(
    !asksCost ? null : costNothing ? 0 : cost.trim() ? readMoney(cost) : null,
  );
  const costProblem = $derived(
    asksCost && !costNothing && cost.trim() !== "" && paid === null,
  );
  const showCostProblem = $derived(costProblem && costChecked);
  // One bought-in plant is "it", not "all 1 of them".
  const onePlant = $derived(source === "BoughtIn" && quantity === 1);

  // The answers on "Where from?" must be complete, even if the browser's Forward skipped past it.
  const whereFromDone = $derived(
    originCountry.trim().length === 2 &&
      (source !== "BoughtIn" || !!supplierId),
  );

  /** Everything about to be recorded, each with the step that changes it. */
  const summary = $derived.by(() => {
    const supplier = suppliers.find((item) => item.id === supplierId)?.name;
    const country =
      originCountries.find((item) => item.code === originCountry)?.name ??
      originCountry;
    const potSize = potSizes.find((item) => item.id === potSizeId)?.name;
    const photoCount = Object.keys(photos).length;
    const rows: {
      name: string;
      value: string;
      stage: number;
      field?: string;
    }[] = [
      { name: "Plant", value: chosenName, stage: 1 },
      {
        name: counting ? "How many" : "Seed sown",
        value: `${counting ? quantity : sowing}, in a ${containerKinds[containerKind].toLowerCase()}${potSize ? `, ${potSize}` : ""}`,
        stage: 2,
        field: !seed
          ? undefined
          : sownAs === "About"
            ? "aboutSeeds"
            : sownAs === "Packet"
              ? "packetSays"
              : undefined,
      },
      {
        name: started[source],
        value: onDay(startedOn).replace(/^on /, ""),
        stage: 3,
      },
    ];

    if (source !== "Cuttings") {
      rows.push({
        name: "Supplier",
        value: supplier ?? "Not listed",
        stage: 3,
        field: "supplier",
      });
      if (sourceReference.trim()) {
        rows.push({
          name: "Lot or reference",
          value: sourceReference.trim(),
          stage: 3,
          field: "sourceReference",
        });
      }

      if (source === "SeedLot" && sowBy) {
        rows.push({
          name: "Sow by",
          value: sowByWords(sowBy),
          stage: 3,
          field: "sowBy",
        });
      }
    }

    rows.push(
      ...(source === "Cuttings" && motherId
        ? [
            {
              name: "Taken from",
              value:
                mothers.find((item) => item.id === motherId)?.name ??
                "A mother plant",
              stage: 3,
            },
          ]
        : []),
      ...(asksCost
        ? [
            {
              name: seed
                ? "Seed cost"
                : onePlant
                  ? "Cost of the plant"
                  : "Cost of the plants",
              value:
                paid === 0
                  ? "Nothing"
                  : costProblem
                    ? `"${cost.trim()}", which isn't an amount`
                    : paid === null
                      ? "Not added yet"
                      : money(paid),
              stage: 3,
            },
          ]
        : []),
      { name: "Country of origin", value: country, stage: 3 },
      {
        name: "Photos",
        value: photoCount === 0 ? "None" : String(photoCount),
        stage: 4,
      },
      {
        name: "Place",
        value:
          places.find((item) => item.id === locationId)?.label ?? "Not set",
        stage: 5,
      },
    );

    return rows;
  });

  const addSupplier = () =>
    attempt(async () => {
      const name = newSupplier.trim();
      const { data, response } = await api.POST("/api/suppliers", {
        body: {
          name,
          contactName: null,
          phone: null,
          email: null,
          address: null,
          accountReference: null,
          supplies: null,
          typicalLeadTimeDays: null,
          passportRegistrationNumber: newPassport.trim() || null,
          notes: null,
          otherRegistrations: newOther.trim() || null,
        },
      });

      if (data) {
        suppliers = (await api.GET("/api/suppliers")).data ?? [];
        supplierId = data.id;
        newSupplier = "";
        newPassport = "";
        newOther = "";
        problem = "";
      } else {
        problem =
          response.status === 409
            ? "That supplier is already on the list."
            : "Please enter a name.";
      }
    }, setProblem);

  async function choosePhoto(event: Event, kind: PhotoKind) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    const blob = await shrinkPhoto(file);
    const previous = photos[kind];

    if (previous) {
      URL.revokeObjectURL(previous.url);
    }

    photos[kind] = { blob, url: URL.createObjectURL(blob) };
    input.value = "";
  }

  /** Adds the plant only now, so abandoning the steps leaves nothing behind. */
  async function resolvePlant(): Promise<string | null> {
    const madeFor =
      picked?.kind === "species" ? `${picked.id}|${variety.trim()}` : "";

    if (
      !picked ||
      picked.kind === "plant" ||
      (plantId && plantMadeFor === madeFor)
    ) {
      return plantId;
    }

    if (picked.kind === "species") {
      const { data, response } = await api.POST("/api/plants/from-species", {
        body: {
          speciesId: picked.id,
          variety: optional(variety),
          category: null,
          position: details?.suggestedPosition ?? null,
          watering: details?.suggestedWatering ?? null,
          hardiness: details?.suggestedHardiness ?? null,
          xylellaHost: null,
          protectedVariety: false,
          prices: [],
        },
      });

      if (!data) {
        problem =
          response.status === 409
            ? "That plant was taken off the list. Ask the owner to restore it."
            : "That plant couldn't be added. Please try again.";
      }

      return data?.id ?? null;
    }

    const { data, response } = await api.POST("/api/plants", {
      body: {
        commonName: picked.commonName,
        variety: picked.variety,
        botanicalName: picked.botanicalName,
        category: null,
        position: null,
        watering: null,
        hardiness: null,
        xylellaHost: null,
        protectedVariety: false,
        prices: [],
      },
    });

    if (!data) {
      problem =
        response.status === 409
          ? "That plant is already on the list. Go back and search for it."
          : "Please check the name.";
    }

    return data?.id ?? null;
  }

  async function sendPhotos(batchId: string, savedPlantId: string) {
    let failed = 0;

    for (const [kind, photo] of Object.entries(photos)) {
      // With batch photos turned off only the plant's own picture is kept.
      if (kind !== "Plant" && !isOn("BatchPhotos")) {
        continue;
      }

      const address =
        kind === "Plant"
          ? `/api/plants/${savedPlantId}/photo`
          : `/api/batches/${batchId}/photos?kind=${kind}`;

      try {
        const response = await fetch(address, {
          method: kind === "Plant" ? "PUT" : "POST",
          body: photo.blob,
        });
        failed += response.ok ? 0 : 1;
      } catch {
        failed += 1;
      }
    }

    return failed;
  }

  let pending: ReturnType<typeof work.startBatch> | null = null;
  const start = () =>
    attempt(async () => {
      busy = true;

      try {
        const savedPlantId = await resolvePlant();

        if (!savedPlantId) {
          return;
        }

        plantId = savedPlantId;
        plantMadeFor =
          picked?.kind === "species" ? `${picked.id}|${variety.trim()}` : "";
        pending = retainIdentity(
          work.startBatch(
            {
              plantId: savedPlantId,
              quantity: counting ? quantity : 0,
              sownAs: seed ? sownAs : null,
              sownAmount: !seed
                ? null
                : sownAs === "About"
                  ? aboutSeeds
                  : sownAs === "Grams"
                    ? Number(grams)
                    : null,
              packetShare: seed && sownAs === "Packet" ? packetShare : null,
              packetSays:
                seed && sownAs === "Packet" ? packetSays.trim() || null : null,
              source,
              sourceReference:
                source === "Cuttings" ? null : optional(sourceReference),
              supplierId: source === "Cuttings" ? null : supplierId || null,
              parentBatchId: null,
              motherPlant: null,
              motherPlantId:
                source === "Cuttings" && motherId ? motherId : null,
              originCountry,
              containerKind,
              locationId: locationId || null,
              potSizeId: potSizeId || null,
              purchaseLineId: prefill?.purchaseLineId ?? null,
              sowBy: source === "SeedLot" && sowBy ? sowBy : null,
              // Nought is taken as it costing nothing.
              costAmount: paid ? paid : null,
              costNothing: paid === 0,
            },
            momentOn(startedOn),
          ),
          pending,
        );
        const outcome = await submit(pending);

        if (outcome.kind !== "stored") {
          problem = outcomeProblem(outcome);
          return;
        }

        const failed = outcome.batchId
          ? await sendPhotos(outcome.batchId, savedPlantId)
          : Object.keys(photos).length;

        onDone(
          `Started ${counting ? `${quantity} ${chosenName}` : `${chosenName} from ${sownAs !== "Unknown" && sowing ? sowing : "seed"}`}. Label: ${outcome.labelCode}${failed > 0 ? `. ${failed} photo${failed === 1 ? "" : "s"} didn't save.` : ""}`,
          outcome.eventId,
          outcome.labelCode,
        );
      } finally {
        busy = false;
      }
    }, setProblem);
</script>

{#snippet tag(field: string)}
  {#if fromPhoto[field]}<span class="pill read-tag">Read from the photo</span
    >{/if}
{/snippet}

<ActionFrame title={stages[stage - 1]} {problem} onCancel={back}>
  <p class="muted">Step {stage} of {stages.length}</p>
  <div class="stage-bar" aria-hidden="true">
    {#each stages as name, index (name)}
      <span class:reached={index < stage}></span>
    {/each}
  </div>
  {#if prefill}<p class="muted">From purchase: {prefill.description}</p>{/if}

  {#if stage === 1 && !confirming}
    {#if !prefill}
      <ReadLabel onUse={usePacket} />
    {/if}
    {#if packetName}
      <p class="muted" tabindex="-1" bind:this={packetNote}>
        The packet says this is {packetName}. Choose the matching plant below.
      </p>
    {/if}
    {#key searchSeed}
      <PlantSearch
        recent={recentPlants}
        onPick={pick}
        initialQuery={searchFor}
      />
    {/key}
  {:else if stage === 1 && picked?.kind === "species"}
    <div class="card">
      <div class="plant-head">
        <PlantThumb url={picked.photoUrl} />
        <span class="plant-names">
          <strong>{picked.commonName ?? picked.scientificName}</strong>
          {#if picked.commonName}<em>{picked.scientificName}</em>{/if}
        </span>
      </div>
      {#if picked.credit}<p class="muted credit">{picked.credit}</p>{/if}
    </div>
    {#if details?.suggestedPosition || details?.suggestedWatering || details?.suggestedHardiness}
      <p class="muted">
        These came from Wikipedia and are saved with the plant:
        {[
          details.suggestedPosition,
          details.suggestedWatering,
          details.suggestedHardiness,
        ]
          .filter(Boolean)
          .join(". ")}. You can change these under Plants.
      </p>
    {/if}
    <label
      >Variety (if there is one) {@render tag("variety")}
      <input bind:value={variety} oninput={() => forget("variety")} /></label
    >
    <button onclick={next}>Next</button>
    <button class="secondary" onclick={() => (picked = null)}
      >Choose a different plant</button
    >
  {:else if stage === 2}
    <p class="muted">{chosenName}</p>
    {#if packet?.contentsAsPrinted}
      <p class="muted">
        The packet says it holds {packet.contentsAsPrinted.replace(/\.$/, "")}.
      </p>
    {/if}
    {#if seed}
      <fieldset class="day-picker">
        <legend class="field-label">How much seed went in?</legend>
        <div class="segmented sowing-choices">
          {#each sowingChoices as choice (choice.measure)}
            <button
              aria-pressed={sownAs === choice.measure}
              onclick={() => (sownAs = choice.measure)}>{choice.label}</button
            >
          {/each}
        </div>
      </fieldset>
    {/if}
    {#if counting}
      <Stepper bind:value={quantity} presets={[10, 24, 40, 42, 84]} />
    {:else if sownAs === "About"}
      <p class="muted">
        About how many seeds? Give a guess, or what the packet says.
      </p>
      {@render tag("aboutSeeds")}
      <Stepper bind:value={aboutSeeds} presets={[50, 100, 250, 500]} />
    {:else if sownAs === "Grams"}
      <label
        >Grams of seed <input
          bind:value={grams}
          inputmode="decimal"
          placeholder="0.5"
        /></label
      >
    {:else if sownAs === "Packet"}
      <div class="segmented">
        {#each packetShares as item (item.share)}
          <button
            aria-pressed={packetShare === item.share}
            onclick={() => (packetShare = item.share)}>{item.label}</button
          >
        {/each}
      </div>
      <label
        >What does the packet say? (optional) {@render tag("packetSays")}
        <input
          oninput={() => forget("packetSays")}
          bind:value={packetSays}
          maxlength="200"
          placeholder="approx. 250 seeds, or 1 g"
        /></label
      >
    {/if}
    {#if !counting}
      <p class="muted">
        The tray shows how much seed went in until someone counts what's come
        up, either when it germinates or when it's pricked out.
      </p>
    {/if}
    <label>
      Going into
      <select bind:value={containerKind}>
        <option value="Tray">Tray</option>
        <option value="PotGroup">Group of pots</option>
        <option value="BenchLot">Bench lot</option>
      </select>
    </label>
    <label>
      Pot size
      <select bind:value={potSizeId}>
        <option value="">Not set</option>
        {#each potSizes as size (size.id)}
          <option value={size.id}>{size.name}</option>
        {/each}
      </select>
    </label>
    <button onclick={next} disabled={!sowingReady}>Next</button>
  {:else if stage === 3}
    <div class="segmented">
      {#each sources as item (item.source)}
        <button
          class={source === item.source ? "" : "secondary"}
          aria-pressed={source === item.source}
          onclick={() => {
            madeSeed = false;
            chooseSource(item.source);
          }}>{item.label}</button
        >
      {/each}
    </div>
    {#if madeSeed && source === "SeedLot"}
      <p class="muted">
        This is now set to Seed, because you read a seed packet.
      </p>
    {/if}
    <DayPicker bind:value={startedOn} label={whenAsked[source]} />
    {#if source === "Cuttings" && plantId && isOn("MotherPlants")}
      <label
        >Taken from <select bind:value={motherId}>
          <option value="">No mother plant recorded</option>
          {#each mothers as mother (mother.id)}
            <option value={mother.id}
              >{mother.name}{mother.locationName
                ? ` · ${mother.locationName}`
                : ""}</option
            >
          {/each}
        </select></label
      >
      {#if !motherId}
        <div class="row">
          <label class="grow"
            >Or a new mother plant <input
              bind:value={newMother}
              maxlength="100"
              placeholder="By the door"
            /></label
          >
          {#if newMother.trim()}
            <button type="button" class="secondary" onclick={addMother}
              >Add this mother plant</button
            >
          {/if}
        </div>
      {/if}
    {/if}
    {#if source !== "Cuttings"}
      <label>
        Supplier {@render tag("supplier")}
        <select bind:value={supplierId} onchange={() => forget("supplier")}>
          <option value="">Not listed</option>
          {#each suppliers as supplier (supplier.id)}
            <option value={supplier.id}>{supplier.name}</option>
          {/each}
        </select>
      </label>
      {#if !supplierId}
        <label
          >New supplier {@render tag("supplier")}
          <input
            bind:value={newSupplier}
            oninput={() => forget("supplier")}
          /></label
        >
        {#if fromPhoto.supplier && newSupplier.trim()}
          <p class="muted">
            {newSupplier.trim()} isn't on your supplier list yet. Press Add supplier
            to add them.
          </p>
        {/if}
        {#if newSupplier.trim()}
          <label
            >Their plant passport number (if known) <input
              bind:value={newPassport}
              autocapitalize="characters"
              placeholder="GB 123456"
            /></label
          >
          <label
            >Other registration numbers <textarea
              bind:value={newOther}
              rows="2"
              placeholder="Seed standards: 12345"></textarea></label
          >
          <button class="secondary" onclick={addSupplier}>Add supplier</button>
        {/if}
      {:else if chosenSupplier}
        <SupplierRegistrations
          supplier={chosenSupplier}
          onSaved={reloadSuppliers}
        />
      {/if}
      <label
        >Lot or reference {@render tag("sourceReference")}
        <input
          bind:value={sourceReference}
          oninput={() => forget("sourceReference")}
        /></label
      >
      {#if source === "SeedLot"}
        <SowByPicker bind:value={sowBy}>{@render tag("sowBy")}</SowByPicker>
      {/if}
    {/if}
    {#if asksCost}
      <label
        >{seed
          ? "What did this seed cost? (£, optional)"
          : onePlant
            ? "What did this plant cost? (£, optional)"
            : "What did these plants cost altogether? (£, optional)"}
        {#if cost}{@render tag("cost")}{/if}
        <input
          bind:this={costBox}
          bind:value={cost}
          oninput={() => forget("cost")}
          inputmode="decimal"
          autocomplete="off"
          disabled={costNothing}
          aria-describedby={[
            showCostProblem ? "new-cost-problem" : "",
            packetPriceWords ? "new-cost-packet" : "",
            "new-cost-help",
          ]
            .filter(Boolean)
            .join(" ")}
          aria-invalid={showCostProblem ? "true" : undefined}
        /></label
      >
      {#if showCostProblem}<p class="problem" id="new-cost-problem">
          {moneyProblem}
        </p>{/if}
      {#if packetPriceWords}<p id="new-cost-packet">{packetPriceWords}</p>{/if}
      <p class="muted" id="new-cost-help">
        {seed
          ? "Put in what was paid for the seed this batch is sown from, with any VAT included, as it's printed on the packet or the receipt. If only part of a packet is sown, put in roughly that part of its price. You can leave this empty and add it later."
          : onePlant
            ? "Put in what was paid for it, with any VAT included. You can leave this empty and add it later."
            : `Put in what was paid for all ${quantity} of them, with any VAT included, and not the price of one. You can leave this empty and add it later.`}
      </p>
      <label class="tick"
        ><input
          type="checkbox"
          bind:checked={costNothing}
          onchange={() => forget("cost")}
        />
        {seed
          ? "It cost nothing, because it was our own seed or a gift."
          : onePlant
            ? "It cost nothing, because it was a gift."
            : "They cost nothing, because they were a gift."}</label
      >
    {:else if isOn("Costs") && prefill?.purchaseLineId}
      <p class="muted">
        The cost comes from the purchase, so there's nothing to add here.
      </p>
    {/if}
    <label>
      Country of origin
      <select
        value={originCountries.some((item) => item.code === originCountry)
          ? originCountry
          : originCountry
            ? "other"
            : ""}
        onchange={(event) => {
          const chosen = event.currentTarget.value;
          originCountry = chosen === "other" ? "" : chosen;
          otherCountry = chosen === "other";
        }}
      >
        <option value="">Choose…</option>
        {#each originCountries as item (item.code)}
          <option value={item.code}>{item.name}</option>
        {/each}
        <option value="other">Somewhere else…</option>
      </select>
    </label>
    {#if otherCountry}
      <label
        >Two-letter country code, as on the passport <input
          bind:value={originCountry}
          maxlength="2"
          autocapitalize="characters"
        /></label
      >
    {/if}
    {#if source === "BoughtIn" && !supplierId}
      <p class="muted">
        Bought-in plants need their supplier, for the passport record.
      </p>
    {/if}
    <button onclick={next} disabled={!whereFromDone}>Next</button>
  {:else if stage === 4}
    <p class="muted">
      Take a photo of anything worth keeping. They're all optional.
    </p>
    <div class="card-grid">
      {#each photoChoices.filter((choice) => choice.kind === "Plant" || isOn("BatchPhotos")) as choice (choice.kind)}
        <label class="photo-choice">
          {#if photos[choice.kind]}
            <img class="plant-thumb" src={photos[choice.kind]?.url} alt="" />
          {:else}
            <span class="plant-thumb placeholder"><Camera size={26} /></span>
          {/if}
          <span>{choice.label}</span>
          <input
            type="file"
            accept="image/*"
            onchange={(event) => choosePhoto(event, choice.kind)}
          />
        </label>
      {/each}
    </div>
    <button onclick={next}
      >{Object.keys(photos).length > 0 ? "Next" : "Skip photos"}</button
    >
  {:else if stage === 5}
    <label>
      Place
      <select bind:value={locationId}>
        <option value="">Not set</option>
        {#each places as place (place.id)}
          <option value={place.id}>{place.label}</option>
        {/each}
      </select>
    </label>
    <button onclick={next}>Next</button>
  {:else}
    <p class="muted">
      Check these before starting: the label prints with them, and the passport
      record relies on them.
    </p>
    <dl class="facts card batch-check">
      {#each summary as row (row.name)}
        <dt>{row.name}</dt>
        <dd>
          <span
            >{row.value}
            {#if row.field}{@render tag(row.field)}{/if}</span
          >
          <button class="link-button" onclick={() => change(row.stage)}
            >Change<span class="visually-hidden">
              {row.name.toLowerCase()}</span
            ></button
          >
        </dd>
      {/each}
    </dl>
    {#if !whereFromDone}
      <p class="problem" role="alert">
        "Where from?" isn't finished: bought-in plants need their supplier, and
        every batch its country of origin.
      </p>
    {/if}
    {#if costProblem}
      <p class="problem" role="alert">
        The cost on "Where from?" isn't an amount in pounds and pence. Please
        change it to one, such as 2.99, or leave it empty.
      </p>
    {/if}
    <button
      disabled={busy ||
        !whereFromDone ||
        costProblem ||
        !picked ||
        !sowingReady}
      onclick={start}>Start batch</button
    >
  {/if}
</ActionFrame>
