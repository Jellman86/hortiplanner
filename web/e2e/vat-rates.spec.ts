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

    // The rates change again while the invoice is being written, so issuing it is refused.
    if (path === "/api/invoices" && request.method() === "POST") {
      rates = { ...rates, standard: 18 };
      await route.fulfill({ status: 400, json: { problem: "VatRate" } });
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
      "/api/customers": [
        {
          id: "0193a6f0-0000-7000-8000-0000000000c9",
          name: "Hilltop",
          address: "1 High Street",
          email: null,
          phone: null,
          vatNumber: null,
          archived: false,
        },
      ],
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

  // Asked first: the question takes the focus, and "Keep it" puts the focus back on Save.
  await card.getByLabel("Standard rate (%)").fill("17.5");
  await card.getByRole("button", { name: "Save the VAT rates" }).click();
  const asked = card.getByRole("group", {
    name: "Change the standard rate from 20% to 17.5%? It will be used from today, and nothing already recorded changes.",
  });
  await expect(asked).toBeFocused();
  await expectAccessible(page, "Nursery details, asked about a new VAT rate");
  await card.getByRole("button", { name: "Keep it at 20%" }).click();
  await expect(
    card.getByRole("button", { name: "Save the VAT rates" }),
  ).toBeFocused();
  await expect(card.getByLabel("Standard rate (%)")).toHaveValue("20");

  await card.getByLabel("Standard rate (%)").fill("17.5");
  await card.getByRole("button", { name: "Save the VAT rates" }).click();
  await card.getByRole("button", { name: "Change the rate" }).click();
  await expect(
    card.getByRole("button", { name: "Save the VAT rates" }),
  ).toBeFocused();
  await expect(
    card.getByText("The standard rate is 17.5% from today."),
  ).toBeVisible();
  expect(sent).toEqual([{ standard: 17.5, reduced: 5 }]);
  await expect(
    card.getByText(
      "The standard rate was 20% until 31 March 2027. Sam Fielding changed it to 17.5% on 1 April 2027.",
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
    .getByRole("button", { name: "Selling", exact: true })
    .click();
  await page.getByRole("button", { name: "New invoice" }).click();
  const vat = page.locator("label.line-vat select").first();
  await expect(vat.locator("option:checked")).toHaveText("Standard, 17.5%");

  // Refused because the rates changed meanwhile: it says so, and the line moves to the new standard rate.
  await page
    .getByRole("combobox", { name: "Customer" })
    .selectOption({ label: "Hilltop" });
  await page.getByLabel("Description").fill("Mixed perennials");
  await page.getByLabel("Price each (£)").fill("4.50");
  page.once("dialog", (dialog) => void dialog.accept());
  await page.getByRole("button", { name: "Issue invoice" }).click();
  await expect(
    page.getByText(
      "The VAT rates changed while you were writing this invoice. Please check the VAT on each line and issue it again.",
    ),
  ).toBeVisible();
  await expect(vat.locator("option:checked")).toHaveText("Standard, 18%");
});
