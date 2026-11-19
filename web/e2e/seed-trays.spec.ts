import { expect, test } from "./fixtures";

// Seed sown by a measure, not a count, shows what went in until somebody counts what's come up.
test("a seed tray shows the seed that went in, not that it hasn't been counted", async ({
  page,
}) => {
  const row = (
    labelCode: string,
    commonName: string,
    extra: Record<string, unknown>,
  ) => ({
    containerId: `tray-${labelCode}`,
    labelCode,
    batchId: `batch-${labelCode}`,
    traceabilityCode: `B-${labelCode}`,
    plantId: `plant-${labelCode}`,
    commonName,
    variety: null,
    botanicalName: null,
    quantity: 0,
    stage: "Sown",
    stageSince: "2026-11-01T09:00:00Z",
    locationName: "Greenhouse 2, Bench 2",
    potSizeName: null,
    supplierName: null,
    hasPhoto: false,
    locationId: "bench",
    notCounted: true,
    ...extra,
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
        row("C000002", "Cornflower", {
          sown: {
            sownAs: "About",
            sownAmount: 200,
            packetShare: null,
            packetSays: null,
          },
        }),
        row("C000003", "Foxglove", {
          sown: {
            sownAs: "Packet",
            sownAmount: null,
            packetShare: "Half",
            packetSays: null,
          },
        }),
        row("C000004", "Lavender", { quantity: 40, notCounted: false }),
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Production" })
    .click();

  await expect(page.getByText("About 200 seeds of Cornflower")).toBeVisible();
  await expect(page.getByText("Half a packet of Foxglove")).toBeVisible();
  await expect(page.getByText("40 + 2 trays").first()).toBeVisible();
  await expect(page.getByText(/not counted/i)).toHaveCount(0);

  // On a desk, the table says the same in its own column.
  await page.setViewportSize({ width: 1280, height: 800 });
  const table = page.locator(".data-table");
  await expect(
    table.getByRole("row", { name: /C000002/ }).getByText("About 200 seeds"),
  ).toBeVisible();
  await expect(
    table.getByRole("row", { name: /C000003/ }).getByText("Half a packet"),
  ).toBeVisible();
});
