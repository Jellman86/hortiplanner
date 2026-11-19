import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const png = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);

// On a phone, a plant builds up photos of its own: added by kind, captioned, one chosen as its main picture, a wrong
// one removed, and any of them handed to Ask Hortiplanner. Its batches' photos sit beside them.
test("a plant's photos are added, captioned, chosen and asked about on a phone", async ({
  page,
}) => {
  const plantId = "0193a6f0-0000-7000-8000-0000000000a1";
  type Photo = {
    id: string;
    kind: string;
    caption: string | null;
    isMain: boolean;
    takenAt: string;
    canRemove: boolean;
    canBeAsked: boolean;
  };
  const photos: Photo[] = [];
  const asked: { about?: { plantPhotoId?: string } }[] = [];
  let next = 1;
  let refuseNext = true;
  const listed = () =>
    [...photos].sort((a, b) => Number(b.isMain) - Number(a.isMain));

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;

    if (path === `/api/plants/${plantId}/photos`) {
      if (request.method() === "POST" && refuseNext) {
        refuseNext = false;
        await route.fulfill({ status: 413 });
      } else if (request.method() === "POST") {
        const photo: Photo = {
          id: `0193a6f0-0000-7000-8000-00000000f00${next++}`,
          kind: url.searchParams.get("kind") ?? "Other",
          caption: null,
          isMain: photos.length === 0,
          takenAt: "2026-11-19T10:00:00Z",
          canRemove: true,
          canBeAsked: true,
        };
        photos.push(photo);
        await route.fulfill({ status: 201, json: photo });
      } else {
        await route.fulfill({ json: listed() });
      }
      return;
    }

    const one = /^\/api\/plant-photos\/([^/]+)(\/main)?$/.exec(path);
    if (one) {
      const photo = photos.find((item) => item.id === one[1])!;
      if (request.method() === "GET") {
        await route.fulfill({ contentType: "image/png", body: png });
      } else if (one[2]) {
        photos.forEach((item) => (item.isMain = item === photo));
        await route.fulfill({ status: 204 });
      } else if (request.method() === "PUT") {
        Object.assign(photo, request.postDataJSON());
        await route.fulfill({ json: photo });
      } else {
        photos.splice(photos.indexOf(photo), 1);
        if (photo.isMain && photos.length > 0) photos.at(-1)!.isMain = true;
        await route.fulfill({ status: 204 });
      }
      return;
    }

    if (
      path.startsWith("/api/batch-photos/") ||
      path === `/api/plants/${plantId}/photo`
    ) {
      await route.fulfill({ contentType: "image/png", body: png });
      return;
    }

    if (path === "/api/assistant/ask") {
      asked.push(request.postDataJSON());
      await route.fulfill({
        json: {
          answer: "That looks like a foxglove flower.",
          sources: [],
          cost: 0.001,
          spentThisMonth: 0.1,
          spentInAll: 0.1,
        },
      });
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/assistant/status": {
        canAsk: true,
        setUp: true,
        readsImages: true,
        spent: { thisMonth: 0.1, inAll: 0.1 },
      },
      "/api/containers/by-label/C000001": {
        id: "0193a6f0-0000-7000-8000-0000000000c1",
        labelCode: "C000001",
        kind: "Tray",
        contents: [
          {
            batchId: "0193a6f0-0000-7000-8000-0000000000b1",
            traceabilityCode: "B000001",
            plantId,
            commonName: "Foxglove",
            variety: null,
            quantity: 30,
            source: "SeedLot",
            notCounted: false,
            rough: false,
          },
        ],
        locationId: null,
        locationName: "Greenhouse 2, Bench 2",
        potSizeId: null,
        potSizeName: null,
        stage: "Germinated",
        stageSince: "2026-11-10T09:00:00Z",
        startedAt: "2026-11-01T09:00:00Z",
      },
      [`/api/plants/${plantId}/card`]: {
        id: plantId,
        commonName: "Foxglove",
        variety: null,
        botanicalName: "Digitalis purpurea",
        category: null,
        position: null,
        watering: null,
        hardiness: null,
        xylellaHost: false,
        protectedVariety: false,
        archived: false,
        hasPhoto: photos.length > 0,
        photoCredit: null,
        linkedToSpecies: false,
        family: null,
        nativeToUk: null,
        description: null,
        growing: null,
        notes: [],
        lookupUnavailable: false,
      },
      [`/api/plants/${plantId}/batch-photos`]: [
        {
          id: "0193a6f0-0000-7000-8000-0000000000e1",
          batchId: "0193a6f0-0000-7000-8000-0000000000b1",
          traceabilityCode: "B000001",
          takenAt: "2026-11-12T10:00:00Z",
          stage: "Germinated",
          canBeAsked: true,
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });

  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
  await page.getByRole("button", { name: "About this plant" }).click();
  await expect(page.getByRole("heading", { name: "Foxglove" })).toBeVisible();

  // Nothing yet: it says what to add, and the batches' photos are already there.
  await expect(
    page.getByText("There are no photos of this plant yet."),
  ).toBeVisible();
  await expect(
    page.getByRole("list", { name: "Photos from the batches of Foxglove" }),
  ).toContainText("Batch B000001");
  await expectAccessible(page, "Plant photos, empty");

  const add = page.getByRole("group", { name: "Add a photo" });
  for (const kind of ["Flower", "Foliage", "Whole plant", "Label"]) {
    const choice = add.locator(".photo-choice", { hasText: kind });
    expect((await choice.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  }

  // A photo too big to save says so by "Add a photo", and nowhere else.
  await add
    .locator(".photo-choice", { hasText: "Flower" })
    .locator("input[type=file]")
    .setInputFiles({ name: "huge.png", mimeType: "image/png", buffer: png });
  await expect(page.getByRole("alert")).toHaveText(
    "That photo is too big to save. Please try a smaller one.",
  );

  // A flower photo opens straight away for its caption, and the old problem has gone.
  await add
    .locator(".photo-choice", { hasText: "Flower" })
    .locator("input[type=file]")
    .setInputFiles({ name: "flower.png", mimeType: "image/png", buffer: png });
  const viewer = page.getByRole("dialog", { name: "Photo of Foxglove" });
  await expect(viewer).toBeVisible();
  await expect(
    viewer.getByText("This is the plant's main picture."),
  ).toBeVisible();
  await viewer
    .getByLabel("What does this photo show? (You can leave this empty.)")
    .fill("First flowers in June");
  await expect(page.getByRole("alert")).toHaveCount(0);
  await viewer
    .getByRole("button", { name: "Save the caption and kind" })
    .click();
  await expect(viewer.getByRole("status")).toHaveText(
    "The caption and kind are saved.",
  );
  expect(photos[0].caption).toBe("First flowers in June");
  await expectAccessible(page, "Plant photo viewer");
  await viewer.getByRole("button", { name: "Close" }).click();

  // A second photo, made the main picture.
  await add
    .locator(".photo-choice", { hasText: "Foliage" })
    .locator("input[type=file]")
    .setInputFiles({ name: "leaves.png", mimeType: "image/png", buffer: png });
  await expect(viewer).toBeVisible();
  await viewer.getByRole("button", { name: "Use as the main picture" }).click();
  await expect(
    viewer.getByText("This is the plant's main picture."),
  ).toBeVisible();
  expect(photos.find((photo) => photo.kind === "Foliage")!.isMain).toBe(true);
  await viewer.getByRole("button", { name: "Close" }).click();

  const strip = page.getByRole("list", { name: "Photos of Foxglove" });
  await expect(strip.locator("li")).toHaveCount(2);
  await expect(strip.locator("li").first()).toContainText(
    "Foliage · Main picture",
  );
  await expect(strip).toContainText("First flowers in June");
  await expectAccessible(page, "Plant photos, with photos");

  // The main picture is removed, after saying what will happen; the flower takes its place.
  page.once("dialog", (dialog) => {
    expect(dialog.message()).toContain("the newest of the other photos");
    void dialog.accept();
  });
  await strip.locator("li").first().getByRole("button").click();
  await viewer.getByRole("button", { name: "Remove this photo" }).click();
  await expect(strip.locator("li")).toHaveCount(1);
  await expect(strip).toContainText("Flower · Main picture");

  // Asked about, it goes to Ask Hortiplanner with the photo.
  await strip.locator("li").first().getByRole("button").click();
  await viewer.getByRole("button", { name: "Ask about this photo" }).click();
  await expect(page.getByText("About a photo of Foxglove")).toBeVisible();
  await page.getByLabel("Your question").fill("What is this?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(
    page.getByText("That looks like a foxglove flower."),
  ).toBeVisible();
  expect(asked[0].about?.plantPhotoId).toBe(photos[0].id);

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
