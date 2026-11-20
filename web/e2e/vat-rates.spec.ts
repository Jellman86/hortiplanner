import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// The owner sets the VAT rates under Settings, Nursery details; a change is asked about first, used from today, and
// offered on the next invoice.
test("a new VAT rate is confirmed, kept, and offered on invoices", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  let rates = {
    standard: 20,
    reduced: 5,
    changes: [
      {
        band: "Standard",
        percent: 20,
        from: "2000-01-01",
        setBy: null,
        setAt: "2026-11-01T00:00:00Z",
      },
      {
        band: "Reduced",
        percent: 5,
        from: "2000-01-01",
        setBy: null,
        setAt: "2026-11-01T00:00:00Z",
      },
    ],
  };
  const sent: unknown[] = [];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === "/api/settings/vat-rates") {
      if (request.method() === "PUT") {
        sent.push(request.postDataJSON());
        rates = {
          standard: 17.5,
          reduced: 5,
          changes: [
            ...rates.changes,
            {
              band: "Standard",
              percent: 17.5,
              from: "2027-04-01",
              setBy: "Sam Fielding",
              setAt: "2027-04-01T09:00:00Z",
            },
          ],
        };
        await route.fulfill({ json: { changed: ["Standard"] } });
      } else {
        await route.fulfill({ json: rates });
      }
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Sam Fielding",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
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
    .getByRole("button", { name: "Settings", exact: true })
    .click();
  await page
    .getByRole("navigation", { name: "Settings" })
    .getByRole("button", { name: "Nursery details" })
    .click();

  const card = page.locator("form", {
    has: page.getByRole("heading", { name: "VAT rates" }),
  });
  await expect(card.getByLabel("Standard rate (%)")).toHaveValue("20");
  await expectAccessible(page, "Nursery details, VAT rates");

  await card.getByLabel("Standard rate (%)").fill("17.5");
  await card.getByRole("button", { name: "Save the VAT rates" }).click();
  await expect(card.getByRole("alert")).toHaveText(
    /Change the standard rate from 20% to 17.5%\? It will be used from today, and nothing already recorded changes\./,
  );
  await card.getByRole("button", { name: "Change the rate" }).click();
  await expect(
    card.getByText("The standard rate is 17.5% from today."),
  ).toBeVisible();
  expect(sent).toEqual([{ standard: 17.5, reduced: 5 }]);
  await expect(
    card.getByText(
      /The standard rate was 20% until .* Sam Fielding changed it to 17\.5% on/,
    ),
  ).toBeVisible();

  // Saving the same figures again changes nothing.
  await card.getByRole("button", { name: "Save the VAT rates" }).click();
  await expect(
    card.getByText(
      "Those are already the rates in use, so nothing has changed.",
    ),
  ).toBeVisible();

  // The next invoice starts at the new standard rate.
  await page
    .locator(".sidebar")
    .getByRole("button", { name: "Invoices", exact: true })
    .click();
  await page.getByRole("button", { name: "New invoice" }).click();
  const vat = page.locator("label.line-vat select").first();
  await expect(vat.locator("option:checked")).toHaveText("Standard, 17.5%");
});
