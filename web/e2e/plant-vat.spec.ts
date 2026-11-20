import { expect, test } from "./fixtures";

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
      "/api/settings/nursery": {
        name: "Croft View",
        address: null,
        phone: null,
        email: null,
        website: null,
        registrationNumber: null,
        vatNumber: "GB123456789",
        hasLogo: false,
        vatRegistered: true,
        paymentTermsDays: 30,
        authorisationRenewsOn: null,
      },
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
});
