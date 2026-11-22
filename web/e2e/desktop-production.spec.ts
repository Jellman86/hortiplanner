import { expect, test } from "./fixtures";

// On a desk, Production is a table: choosing several picks trays by a click on the row or its tick box.
test("production's table chooses trays on a desk", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const row = (labelCode: string, commonName: string) => ({
    containerId: `tray-${labelCode}`,
    labelCode,
    batchId: `batch-${labelCode}`,
    traceabilityCode: `B-${labelCode}`,
    plantId: "plant",
    commonName,
    variety: null,
    botanicalName: null,
    quantity: 20,
    stage: "Sown",
    stageSince: "2026-11-01T09:00:00Z",
    locationName: "Greenhouse 1",
    potSizeName: null,
    supplierName: "Chiltern seeds",
    hasPhoto: false,
    locationId: "greenhouse",
    notCounted: false,
  });
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/production": [
        row("C000002", "Forking Larkspur"),
        row("C000003", "Cornflower"),
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Main" })
    .getByRole("button", { name: "Production" })
    .click();
  await page.getByRole("button", { name: "Choose several" }).click();

  const table = page.locator(".data-table");
  await table.getByRole("row", { name: /C000002/ }).click();
  await expect(page.getByText("1 tray chosen")).toBeVisible();
  await expect(
    table.getByRole("checkbox", { name: "Choose C000002" }),
  ).toBeChecked();

  await table.getByRole("checkbox", { name: "Choose C000003" }).click();
  await expect(page.getByText("2 trays chosen")).toBeVisible();

  await table.getByRole("row", { name: /C000002/ }).click();
  await expect(page.getByText("1 tray chosen")).toBeVisible();
  await expect(
    table.getByRole("checkbox", { name: "Choose C000002" }),
  ).not.toBeChecked();

  // Leaving choose mode shows stage pills in the table and opens a tray when clicking anywhere on its row.
  await page.getByRole("button", { name: "Stop choosing" }).click();
  await expect(table.locator(".pill").first()).toHaveText("Sown");
});

test("overview stat tiles jump straight to ready trays on a desk", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/money/summary": { owed: 0 },
      "/api/production": [
        {
          containerId: "tray-ready",
          labelCode: "C000010",
          batchId: "batch-ready",
          traceabilityCode: "B000010",
          plantId: "plant-1",
          commonName: "Lavender",
          variety: null,
          botanicalName: null,
          quantity: 12,
          stage: "Ready",
          stageSince: "2026-11-10T09:00:00Z",
          locationName: "Bench 1",
          potSizeName: "9cm",
          supplierName: null,
          hasPhoto: false,
          locationId: "bench-1",
          notCounted: false,
        },
        {
          containerId: "tray-sown",
          labelCode: "C000011",
          batchId: "batch-sown",
          traceabilityCode: "B000011",
          plantId: "plant-2",
          commonName: "Thyme",
          variety: null,
          botanicalName: null,
          quantity: 20,
          stage: "Sown",
          stageSince: "2026-11-12T09:00:00Z",
          locationName: "Greenhouse 1",
          potSizeName: null,
          supplierName: null,
          hasPhoto: false,
          locationId: "gh-1",
          notCounted: false,
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page.locator(".stat-tile-button", { hasText: "ready" }).click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();
  await expect(page.locator(".data-table")).toContainText("Lavender");
  await expect(page.locator(".data-table")).not.toContainText("Thyme");
});
