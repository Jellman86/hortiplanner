import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// The cost of potting one plant into each size is set under Settings, Pot sizes: a first amount covers potting
// already recorded, and the figure before VAT is shown as it's typed.
test("the owner sets what it costs to pot one plant into a size", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const size = "0193a6f0-0000-7000-8000-0000000000a1";
  const sent: unknown[] = [];
  let allowance: Record<string, unknown> | null = null;

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === `/api/pot-sizes/${size}/allowance`) {
      const body = request.postDataJSON() as {
        costEach: number;
        includesVat: boolean;
      };
      sent.push(body);
      const first = allowance === null;
      allowance = {
        potSizeId: size,
        name: "9cm",
        costEach: body.costEach,
        includesVat: body.includesVat,
        vatRate: 20,
        each: body.includesVat
          ? { withVat: body.costEach, beforeVat: body.costEach / 1.2 }
          : { withVat: body.costEach * 1.2, beforeVat: body.costEach },
        set: true,
      };
      await route.fulfill({ json: { changed: true, first } });
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
      "/api/pot-sizes": [
        {
          id: size,
          name: "9cm",
          archived: false,
          quantityInStock: null,
          photoIds: [],
        },
      ],
      "/api/pot-sizes/allowances": [
        allowance ?? {
          potSizeId: size,
          name: "9cm",
          costEach: null,
          includesVat: true,
          vatRate: 0,
          each: null,
          set: false,
        },
      ],
      "/api/settings/vat-rates": { standard: 20, reduced: 5, changes: [] },
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
    .getByRole("button", { name: "Pot sizes" })
    .click();

  await expect(
    page.getByText("There's no cost of potting into it yet."),
  ).toBeVisible();
  await page.getByRole("button", { name: "Set the cost of potting" }).click();
  const box = page.getByLabel(
    "What it costs to pot one plant into this size, with VAT (£, optional)",
  );
  await expect(box).toBeFocused();
  await box.fill("0.42");
  await expect(box).toHaveAccessibleDescription(
    /^Count the pot, the compost that fills it and the label\..* That's £0\.35 before VAT at 20%\.$/,
  );
  await expectAccessible(page, "Pot sizes, the cost of potting");

  // Ticked as before VAT, the figure with VAT is worked out instead.
  await page.getByLabel("This amount is before VAT.").check();
  await expect(page.getByText("That's £0.50 with VAT at 20%.")).toBeVisible();
  await page.getByLabel("This amount is before VAT.").uncheck();

  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "As this is the first amount for 9cm, it's used for potting already recorded as well.",
  );
  await expect(
    page.getByText(
      "Potting one plant into it costs £0.42 with VAT, which is £0.35 before VAT.",
    ),
  ).toBeVisible();

  await page
    .getByRole("button", { name: "Change the cost of potting" })
    .click();
  await expect(box).toHaveValue("0.42");
  await box.fill("0.60");
  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "The new amount is used for potting from today. Potting already recorded keeps the amount it had.",
  );
  expect(sent).toEqual([
    { costEach: 0.42, includesVat: true },
    { costEach: 0.6, includesVat: true },
  ]);
});
