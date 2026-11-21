import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// The cost of potting one plant into each size is set under Settings, Pot sizes, on a phone: a first amount covers
// potting already recorded, the figure before VAT is shown as it's typed, and an amount can be taken away.
test("the owner sets what it costs to pot one plant into a size", async ({
  page,
}) => {
  const size = "0193a6f0-0000-7000-8000-0000000000a1";
  const sent: unknown[] = [];
  let allowance: Record<string, unknown> | null = null;

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === `/api/pot-sizes/${size}/allowance`) {
      const body = request.postDataJSON() as {
        costEach: number | null;
        includesVat: boolean;
      };
      sent.push(body);
      const first = allowance === null;
      const each = body.costEach ?? 0;
      allowance = {
        potSizeId: size,
        name: "9cm",
        costEach: body.costEach,
        includesVat: body.includesVat,
        vatRate: 20,
        each:
          body.costEach === null
            ? null
            : body.includesVat
              ? { withVat: each, beforeVat: each / 1.2 }
              : { withVat: each * 1.2, beforeVat: each },
        set: true,
      };
      await route.fulfill({
        json: { changed: true, first, reachesBack: first },
      });
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
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Settings" })
    .click();
  await page.getByRole("button", { name: "Pot sizes" }).click();

  await expect(
    page.getByText(
      "Nobody has said what it costs to pot a plant into this size yet.",
    ),
  ).toBeVisible();
  const set = page.getByRole("button", {
    name: "Set the cost of potting into 9cm",
  });
  await set.click();
  const box = page.getByLabel(
    "What it costs to pot one plant into this size, with VAT (£, optional)",
  );
  await expect(box).toBeFocused();

  // Too much for one plant is said at the box, as it's typed the figure before VAT is shown.
  await box.fill("1000");
  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(box).toHaveAccessibleDescription(
    /^That's more than Hortiplanner can take for one plant\. Please put in £999\.99 or less\./,
  );
  await box.fill("0.42");
  await expect(box).toHaveAccessibleDescription(
    /^That's £0\.35 before VAT at 20%\. Count the pot/,
  );
  await page.getByLabel("This amount is before VAT.").check();
  // £0.42 before VAT is £0.504 with VAT, so it's "about".
  await expect(
    page.getByText("That's about £0.50 with VAT at 20%."),
  ).toBeVisible();
  await page.getByLabel("This amount is before VAT.").uncheck();
  await expectAccessible(page, "Pot sizes, the cost of potting");

  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "As this is the first amount for 9cm, it's used for potting already recorded as well.",
  );
  await expect(
    page.getByText(
      "It costs £0.42 with VAT to pot one plant into this size, which is £0.35 before VAT.",
    ),
  ).toBeVisible();
  // The focus goes back to the button, now to change it.
  const change = page.getByRole("button", {
    name: "Change the cost of potting into 9cm",
  });
  await expect(change).toBeFocused();

  await change.click();
  await expect(box).toHaveValue("0.42");
  await box.fill("0.60");
  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "The new amount is used for potting from today, including any recorded earlier today. Potting before today keeps the amount it had.",
  );

  // Emptied, or nought, takes the amount away.
  await change.click();
  await box.fill("0");
  await page.getByRole("button", { name: "Save the cost of potting" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Nothing is charged for potting into 9cm from today. Potting recorded before today keeps the amount it had.",
  );
  await expect(
    page.getByText(
      "Nothing is charged for potting into this size at the moment.",
    ),
  ).toBeVisible();
  expect(sent).toEqual([
    { costEach: 0.42, includesVat: true },
    { costEach: 0.6, includesVat: true },
    { costEach: null, includesVat: true },
  ]);
});
