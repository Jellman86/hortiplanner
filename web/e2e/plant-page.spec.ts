import { expect, test } from "./fixtures";

// A plant's articles are read in full on a desk, where there is room beside the picture, and shortened behind Read
// more on a phone.
test("a plant's articles are in full on a desk and behind Read more on a phone", async ({
  page,
}) => {
  const lavender = "0193a6f0-0000-7000-8000-0000000000a1";
  const paragraphs = [
    "Lavender is a strongly aromatic shrub with grey-green leaves and spikes of violet flowers in summer.",
    "It comes from dry, stony hillsides around the Mediterranean, so it likes sun and soil that drains quickly.",
    "Bees and butterflies visit the flowers all summer, and the stems dry well for bunches and sachets.",
    "Plants grown from cuttings come true to the parent, while seedlings vary a little in height and in colour.",
    "In a cold, wet winter the roots can rot, so it is best planted on a slope or in a raised bed with grit added.",
    "The last paragraph says that a light trim after flowering keeps the plant compact for years.",
  ];

  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/containers/by-label/C000001": {
        id: "0193a6f0-0000-7000-8000-0000000000c1",
        labelCode: "C000001",
        kind: "Tray",
        contents: [
          {
            batchId: "0193a6f0-0000-7000-8000-0000000000b1",
            traceabilityCode: "B000001",
            plantId: lavender,
            commonName: "Lavender",
            variety: null,
            quantity: 30,
            source: "SeedLot",
            notCounted: false,
            rough: false,
          },
        ],
        locationId: null,
        locationName: "Greenhouse 2",
        potSizeId: null,
        potSizeName: null,
        stage: "Germinated",
        stageSince: "2026-11-10T09:00:00Z",
        startedAt: "2026-11-01T09:00:00Z",
      },
      [`/api/plants/${lavender}/card`]: {
        id: lavender,
        commonName: "Lavender",
        variety: null,
        botanicalName: "Lavandula angustifolia",
        category: "Shrub",
        position: "Full sun",
        watering: null,
        hardiness: null,
        xylellaHost: false,
        protectedVariety: false,
        archived: false,
        hasPhoto: false,
        photoCredit: null,
        linkedToSpecies: false,
        family: null,
        nativeToUk: null,
        description: {
          text: paragraphs.join("\n\n"),
          source: "Wikipedia",
          licence: "CC BY-SA 4.0",
          url: null,
        },
        growing: null,
        notes: [],
        lookupUnavailable: false,
      },
      [`/api/plants/${lavender}/family-tree`]: {
        linked: false,
        unavailable: false,
        lineage: [],
        relatives: [],
      },
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
  await page.getByRole("button", { name: "About this plant" }).click();

  // Long enough to be shortened where there isn't room.
  expect(paragraphs.join("\n\n").length).toBeGreaterThan(420);
  const about = page.locator(".article-block", { hasText: "About this plant" });
  const last = about.getByText(paragraphs.at(-1)!);
  await expect(last).toBeVisible();
  await expect(about.getByRole("button", { name: "Read more" })).toHaveCount(0);
  // Nothing of the last paragraph is hidden behind a fade.
  expect(
    await last.evaluate((element) => {
      const text = element.getBoundingClientRect();
      const box = element.parentElement!.getBoundingClientRect();
      return text.bottom <= box.bottom + 1;
    }),
  ).toBe(true);

  await page.setViewportSize({ width: 390, height: 844 });
  const more = about.getByRole("button", { name: "Read more" });
  await expect(more).toBeVisible();
  await more.click();
  await expect(about.getByRole("button", { name: "Show less" })).toBeVisible();
  await expect(last).toBeVisible();
});
