import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// On a phone, a plant's page shows where it sits in the plant kingdom, and the nursery's other plants in its genus and
// family, each a tap away.
test("a plant's family tree shows its ranks and relatives on a phone", async ({
  page,
}) => {
  const lavender = "0193a6f0-0000-7000-8000-0000000000a1";
  const french = "0193a6f0-0000-7000-8000-0000000000a2";
  const rosemary = "0193a6f0-0000-7000-8000-0000000000a3";
  const foxglove = "0193a6f0-0000-7000-8000-0000000000a4";
  const rung = (
    id: number,
    rank: string,
    scientificName: string,
    commonName: string | null,
    otherPlants: { plantId: string; name: string }[] = [],
  ) => ({
    taxonId: `0193a6f0-0000-7000-8000-00000000t00${id}`,
    rank,
    scientificName,
    commonName,
    otherPlants,
  });
  const card = (id: string, commonName: string) => ({
    id,
    commonName,
    variety: null,
    botanicalName: null,
    category: null,
    position: null,
    watering: null,
    hardiness: null,
    xylellaHost: false,
    protectedVariety: false,
    archived: false,
    hasPhoto: false,
    photoCredit: null,
    linkedToSpecies: true,
    family: "Lamiaceae",
    nativeToUk: null,
    description: null,
    growing: null,
    notes: [],
    lookupUnavailable: false,
  });

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
      [`/api/plants/${lavender}/card`]: card(lavender, "Lavender"),
      [`/api/plants/${rosemary}/card`]: card(rosemary, "Rosemary"),
      [`/api/plants/${lavender}/family-tree`]: {
        linked: true,
        unavailable: false,
        lineage: [
          rung(1, "kingdom", "Plantae", "Plants"),
          rung(2, "phylum", "Tracheophyta", "Vascular Plants"),
          rung(3, "class", "Magnoliopsida", "Dicots"),
          rung(4, "order", "Lamiales", null, [
            { plantId: french, name: "French Lavender" },
            { plantId: rosemary, name: "Rosemary" },
            { plantId: foxglove, name: "Foxglove" },
          ]),
          rung(5, "family", "Lamiaceae", "Mint Family", [
            { plantId: french, name: "French Lavender" },
            { plantId: rosemary, name: "Rosemary" },
          ]),
          rung(6, "genus", "Lavandula", "Lavenders", [
            { plantId: french, name: "French Lavender" },
          ]),
          rung(7, "species", "Lavandula angustifolia", "Common Lavender"),
        ],
        sameGenus: [{ plantId: french, name: "French Lavender" }],
        sameFamily: [{ plantId: rosemary, name: "Rosemary" }],
      },
      [`/api/plants/${rosemary}/family-tree`]: {
        linked: false,
        unavailable: false,
        lineage: [],
        sameGenus: [],
        sameFamily: [],
      },
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });

  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
  await page.getByRole("button", { name: "About this plant" }).click();

  const tree = page.getByRole("region", { name: "Family tree" });
  const ladder = tree.getByRole("list", {
    name: "Classification, from the order down to this plant",
  });
  await expect(ladder).toContainText("Family: Mint Family, Lamiaceae");
  await expect(ladder).toContainText("Genus: Lavenders, Lavandula");
  await expect(ladder).not.toContainText("Kingdom");
  await expect(ladder.locator("[aria-current=true]")).toContainText(
    "This plant: Lavender",
  );
  await expect(tree).toContainText(
    "You grow one other plant in the same genus.",
  );
  await expect(tree).toContainText(
    "You grow one other plant in the same family.",
  );
  await expectAccessible(page, "Plant family tree");

  // The broader groups unfold above the order.
  await tree.getByRole("button", { name: "Show the broader groups" }).click();
  await expect(
    tree.getByRole("list", {
      name: "Classification, from the kingdom down to this plant",
    }),
  ).toContainText("Kingdom: Plants, Plantae");

  // Plants sharing only the order wait behind their own button.
  await tree
    .getByRole("button", { name: "Show one more distant relative" })
    .click();
  await expect(
    tree.getByRole("button", { name: "Hide the more distant relatives" }),
  ).toHaveAttribute("aria-expanded", "true");
  await expect(tree).toContainText(
    "You grow one other plant in the same order.",
  );
  await expectAccessible(page, "Plant family tree, opened up");

  // A relative opens its own page, and Back comes back.
  await tree.getByRole("button", { name: "Rosemary" }).first().click();
  await expect(page.getByRole("heading", { name: "Rosemary" })).toBeVisible();
  await expect(
    page.getByText("This plant isn't matched to a species yet"),
  ).toBeVisible();
  await page.getByRole("button", { name: "Back" }).click();
  await expect(page.getByRole("heading", { name: "Lavender" })).toBeVisible();

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
