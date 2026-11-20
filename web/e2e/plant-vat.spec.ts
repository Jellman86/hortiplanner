import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

const nursery = (vatRegistered: boolean) => ({
  name: "Croft View",
  address: null,
  phone: null,
  email: null,
  website: null,
  registrationNumber: null,
  vatNumber: vatRegistered ? "GB123456789" : null,
  hasLogo: false,
  vatRegistered,
  paymentTermsDays: 30,
  authorisationRenewsOn: null,
});

/** Stands in for the server: the person, whether the nursery is registered, and no plants yet. */
async function standIn(page: Page, isOwner: boolean, vatRegistered: boolean) {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "person",
        name: isOwner ? "Sam Fielding" : "Jo Reed",
        isOwner,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/species/search": {
        plants: [],
        species: [],
        onlineUnavailable: false,
      },
      "/api/settings/vat-rates": { standard: 20, reduced: 5, changes: [] },
      "/api/settings/nursery": nursery(vatRegistered),
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

async function addByHand(page: Page, plants: string) {
  await page.goto("/");
  await page
    .locator(".sidebar")
    .getByRole("button", { name: plants, exact: true })
    .click();
  await page.getByLabel("Find or add a plant").fill("Basil");
  await page
    .getByRole("button", { name: "Not listed? Add it by hand" })
    .click();
  await page.getByRole("button", { name: "Next" }).click();
}

const unregistered =
  "The nursery isn't registered for VAT, so no VAT is charged on anything it sells. You can still choose the rate now, and it will be used if the nursery registers later.";

test("only the owner chooses a plant's VAT rate, told when the nursery isn't registered", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });

  // A registered nursery: the choice, without the note that nothing is charged.
  await standIn(page, true, true);
  await addByHand(page, "Plants");
  const group = page.getByRole("group", { name: "VAT when it's sold" });
  await expect(
    group.getByRole("radio", { name: "The standard rate, 20%" }),
  ).toBeChecked();
  await expect(group).toHaveAccessibleDescription(
    "Most plants are sold at the standard rate. Please ask your accountant if you aren't sure which rate a plant should have.",
  );
  await expect(page.getByText(unregistered)).toHaveCount(0);

  // Not registered: the note is there, and read with the choice.
  await page.unrouteAll();
  await standIn(page, true, false);
  await addByHand(page, "Plants");
  await expect(page.getByText(unregistered)).toBeVisible();

  // Staff add plants too, but the rate isn't theirs to choose.
  await page.unrouteAll();
  await standIn(page, false, false);
  await addByHand(page, "Plants");
  await expect(
    page.getByRole("button", { name: "Add this plant" }),
  ).toBeVisible();
  await expect(
    page.getByRole("group", { name: "VAT when it's sold" }),
  ).toHaveCount(0);
});

// A plant carries its own VAT rate, so an invoice line for it starts at that rate, with the price worked back from it.
test("an invoice line for a plant starts at the plant's own VAT rate", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const potSize = "0193a6f0-0000-7000-8000-0000000000a1";
  const plant = (id: string, commonName: string, vatBand: string) => ({
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
    vatBand,
    archived: false,
    prices: [{ potSizeId: potSize, price: 6 }],
    hasPhoto: false,
  });

  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Sam Fielding",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/plants": [
        plant("0193a6f0-0000-7000-8000-0000000000b1", "Basil", "Zero"),
        plant("0193a6f0-0000-7000-8000-0000000000b2", "Lavender", "Standard"),
        plant("0193a6f0-0000-7000-8000-0000000000b3", "Compost", "Reduced"),
      ],
      "/api/pot-sizes": [
        {
          id: potSize,
          name: "9cm",
          archived: false,
          quantityInStock: null,
          photoIds: [],
        },
      ],
      "/api/settings/vat-rates": { standard: 20, reduced: 5, changes: [] },
      "/api/settings/nursery": nursery(true),
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });

  await page.goto("/");
  await page
    .locator(".sidebar")
    .getByRole("button", { name: "Invoices", exact: true })
    .click();
  await page.getByRole("button", { name: "New invoice" }).click();

  const what = page.locator("label.line-plant select").first();
  const vat = page.locator("label.line-vat select").first();
  const price = page.getByLabel("Price each (£)").first();
  await expect(vat.locator("option:checked")).toHaveText("Standard, 20%");

  // Basil is zero rated, so the line has no VAT and the whole £6 is the price.
  await what.selectOption({ label: "Basil, 9cm" });
  await expect(vat.locator("option:checked")).toHaveText("None, 0%");
  await expect(price).toHaveValue("6");

  // Lavender is standard rate: £6 with VAT in it is £5 before VAT.
  await what.selectOption({ label: "Lavender, 9cm" });
  await expect(vat.locator("option:checked")).toHaveText("Standard, 20%");
  await expect(price).toHaveValue("5");

  await what.selectOption({ label: "Compost, 9cm" });
  await expect(vat.locator("option:checked")).toHaveText("Reduced, 5%");
  await expect(price).toHaveValue("5.71");

  // Back to something not on the plant list: the line goes back to the standard rate, keeping the price.
  await what.selectOption({ label: "Something else" });
  await expect(vat.locator("option:checked")).toHaveText("Standard, 20%");
  await expect(price).toHaveValue("5.71");
});
