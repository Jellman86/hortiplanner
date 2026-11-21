import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

/** Stands in for the server: the owner, with some parts of Hortiplanner turned off. */
async function standIn(page: Page, off: string[]) {
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
      "/api/settings/features": [
        "Climate",
        "PlacesMap",
        "Passports",
        "MotherPlants",
        "Jobs",
        "Invoices",
        "Purchases",
        "BatchPhotos",
        "Assistant",
        "Costs",
        "TraySales",
        "BenchStations",
      ].map((feature) => ({ feature, on: !off.includes(feature) })),
      "/api/money/summary": { months: [], owed: 0, owedToUs: 0 },
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

const sidebar = (page: Page) => page.getByRole("navigation", { name: "Main" });

test("on a desk the sidebar has the places, and Selling goes when nothing in it is on", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await standIn(page, []);
  await page.goto("/");
  await expect(sidebar(page).getByRole("button")).toHaveText([
    "Record work",
    "Production",
    "Plants",
    "Places",
    "Selling",
    "Buying",
    "Money and figures",
  ]);

  // Selling opens on Invoices, with its other pages in a row under the heading.
  await sidebar(page).getByRole("button", { name: "Selling" }).click();
  await expect(page.getByRole("heading", { name: "Invoices" })).toBeVisible();
  const row = page.getByRole("navigation", { name: "Selling" });
  await expect(row.getByRole("button")).toHaveText([
    "Invoices",
    "Customers",
    "Consignments",
  ]);
  await expect(row.getByRole("button", { name: "Invoices" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  await row.getByRole("button", { name: "Customers" }).click();
  await expect(page.getByRole("heading", { name: "Customers" })).toBeVisible();
  await expect(
    sidebar(page).getByRole("button", { name: "Selling" }),
  ).toHaveAttribute("aria-current", "page");
  const crumbs = page.getByRole("navigation", { name: "Breadcrumb" });
  await expect(crumbs).toContainText("Selling");

  // With trade invoices and plant passports both off, there's nothing to sell from here.
  await standIn(page, ["Invoices", "Passports"]);
  await page.reload();
  await expect(
    sidebar(page).getByRole("button", { name: "Record work" }),
  ).toBeVisible();
  await expect(
    sidebar(page).getByRole("button", { name: "Selling" }),
  ).toHaveCount(0);
});

test("on a phone More lists the places that aren't in the bar below", async ({
  page,
}) => {
  await standIn(page, ["Purchases"]);
  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "More" })
    .click();
  await expect(page.getByRole("heading", { name: "More" })).toBeVisible();
  await expect(page.getByRole("heading", { level: 2 })).toHaveText([
    "Plants",
    "Places",
    "Selling",
    "Buying",
    "Money and figures",
  ]);
  // Purchases is off, so Buying is just Suppliers; and Trace a batch is on Production.
  await expect(
    page.getByRole("button", { name: "Purchases", exact: true }),
  ).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Trace a batch" })).toHaveCount(
    0,
  );

  await page.getByRole("button", { name: "Suppliers", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Suppliers" })).toBeVisible();
  // Alone in its place, so no row.
  await expect(page.getByRole("navigation", { name: "Buying" })).toHaveCount(0);
  await page.getByRole("button", { name: "Back", exact: true }).click();
  await expect(page.getByRole("heading", { name: "More" })).toBeVisible();
});
