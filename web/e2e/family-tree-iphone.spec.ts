import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// On a phone, a plant's page shows where it sits in the plant kingdom as a tree, with the nursery's other plants hanging
// from the branches they share, each a tap away.
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
  ) => ({
    taxonId: `0193a6f0-0000-7000-8000-00000000t00${id}`,
    rank,
    scientificName,
    commonName,
  });
  const taxon = (id: number, scientificName: string, commonName: string) => ({
    taxonId: `0193a6f0-0000-7000-8000-00000000t00${id}`,
    scientificName,
    commonName,
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
          rung(4, "order", "Lamiales", null),
          rung(5, "family", "Lamiaceae", "Mint Family"),
          rung(6, "genus", "Lavandula", "Lavenders"),
          rung(7, "species", "Lavandula angustifolia", "Common Lavender"),
        ],
        relatives: [
          {
            plantId: foxglove,
            name: "Foxglove",
            hasPhoto: false,
            family: taxon(8, "Plantaginaceae", "Plantain Family"),
            genus: taxon(9, "Digitalis", "Foxgloves"),
            species: null,
          },
          {
            plantId: french,
            name: "French Lavender",
            hasPhoto: true,
            family: taxon(5, "Lamiaceae", "Mint Family"),
            genus: taxon(6, "Lavandula", "Lavenders"),
            species: taxon(10, "Lavandula stoechas", "French Lavender"),
          },
          {
            plantId: rosemary,
            name: "Rosemary",
            hasPhoto: false,
            family: taxon(5, "Lamiaceae", "Mint Family"),
            genus: taxon(11, "Salvia", "Sages"),
            species: taxon(12, "Salvia rosmarinus", "Rosemary"),
          },
        ],
      },
      [`/api/plants/${rosemary}/family-tree`]: {
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

  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
  await page.getByRole("button", { name: "About this plant" }).click();

  const tree = page.getByRole("region", { name: "Family tree" });
  const branches = tree.getByRole("list", {
    name: "Family tree, from the order down to this plant and its relatives at the nursery",
  });
  await expect(branches).toContainText("Family: Mint Family, Lamiaceae");
  await expect(branches).toContainText("Genus: Lavenders, Lavandula");
  await expect(branches).toContainText("Genus: Sages, Salvia");
  await expect(branches).not.toContainText("Kingdom");
  await expect(branches).not.toContainText("Foxglove");
  await expect(branches.locator("[aria-current=true]")).toContainText(
    "This plant: Lavender",
  );
  await expect(
    branches.getByRole("button", { name: "French Lavender" }),
  ).toBeVisible();
  await expectAccessible(page, "Plant family tree");

  // The broader groups unfold above the order.
  await tree.getByRole("button", { name: "Show the broader groups" }).click();
  await expect(
    tree.getByRole("list", {
      name: "The broader groups, from the kingdom down",
    }),
  ).toContainText("Kingdom: Plants, Plantae");

  // Plants in other families of the same order wait behind their own button.
  await tree
    .getByRole("button", { name: "Show one more distant relative" })
    .click();
  await expect(
    tree.getByRole("button", { name: "Hide the more distant relatives" }),
  ).toHaveAttribute("aria-expanded", "true");
  await expect(branches).toContainText(
    "Family: Plantain Family, Plantaginaceae",
  );
  await expect(
    branches.getByRole("button", { name: "Foxglove" }),
  ).toBeVisible();
  await expectAccessible(page, "Plant family tree, opened up");

  // On a desk the tree branches across the page, still without scrolling sideways.
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(branches.locator(".ftree-item").first()).toHaveCSS(
    "display",
    "flex",
  );
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.setViewportSize({ width: 390, height: 900 });

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
