import type { Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const pair = (withVat: number, beforeVat: number) => ({ withVat, beforeVat });

const money = (started: string) => ({
  counts: "WithVat",
  vatRegistered: false,
  totals: {
    taken: pair(1024.5, 1024.5),
    spent: pair(312.4, 260.33),
    made: pair(712.1, 764.17),
    lost: pair(38.2, 31.83),
    complete: false,
  },
  unanswered: started === "All" ? 12 : 2,
  notKnown: 3,
  plants: [
    {
      plantId: "0193a6f0-0000-7000-8000-0000000000a1",
      commonName: "Lavender",
      variety: "Hidcote",
      batches: 2,
      sold: 10,
      takings: pair(124, 124),
      unbatched: pair(84, 84),
      cost: pair(40, 33.33),
      complete: false,
      profit: null,
      each: pair(0.8, 0.67),
      lostValue: pair(1.14, 0.95),
    },
  ],
  byYear: [
    {
      name: "2026",
      takings: pair(124, 124),
      cost: pair(40, 33.33),
      complete: false,
      profit: null,
    },
  ],
  bySupplier: [],
  till: { taken: 2410, fromTrays: pair(1630, 1630) },
});

/** Stands in for the server: the owner signed in, the growing figures empty, and the money figures asked for. */
async function standIn(page: Page, asked: string[]) {
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;

    if (path === "/api/figures/money") {
      const started = url.searchParams.get("started") ?? "ThisYear";
      asked.push(started);
      await route.fulfill({ json: money(started) });
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
      "/api/figures": {
        plants: [],
        losses: [],
        bySupplier: [],
        byPlace: [],
        bySeason: [],
      },
      "/api/money/summary": { months: [], owed: 0, owedToUs: 0 },
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

const button = (page: Page, name: string) =>
  page.getByRole("button", { name, exact: true });

test("the owner sees what the plants cost and made, and goes on to fill in the costs", async ({
  page,
}) => {
  const asked: string[] = [];
  await standIn(page, asked);
  await page.goto("/");
  await button(page, "Manage").click();
  await button(page, "Figures").click();

  await expect(
    page.getByRole("heading", { name: "What the plants cost and made" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "The nursery isn't registered for VAT, so the VAT on what it buys can't be claimed back.",
      { exact: false },
    ),
  ).toBeVisible();
  await expect(
    page.getByText("spent so far on what went into them"),
  ).toBeVisible();
  await expect(page.getByText("£260.33 before VAT")).toBeVisible();
  await expect(
    page.getByText(
      "2 batches have no cost yet, so the figures for their plants are incomplete.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText(
      "3 batches are left out of the cost and profit because nobody knows what they cost. What they took is still counted.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText(
      "The till took £2,410.00 this year. Sales recorded from trays came to £1,630.00 in the same time.",
    ),
  ).toBeVisible();

  // On a phone, a card for each plant, with what's missing said in words.
  const card = page.locator(".card", {
    has: page.getByRole("heading", { name: "Lavender 'Hidcote'" }),
  });
  await expect(card.getByText("£40.00 so far").first()).toBeVisible();
  await expect(card.getByText("Not known yet")).toBeVisible();
  await expect(
    card.getByText(
      "£84.00 of these takings is from plants sold without saying which batch, so it's in the plant's takings but not in any batch's.",
    ),
  ).toBeVisible();
  await expectAccessible(page, "Figures, money");

  await button(page, "All of them").click();
  await expect(
    page.getByText("12 batches have no cost yet", { exact: false }),
  ).toBeVisible();
  await expect(button(page, "All of them")).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(asked).toEqual(["ThisYear", "All"]);
  await expect(
    page.getByRole("link", { name: "Download these figures (CSV)" }),
  ).toHaveAttribute("href", "/api/figures/money.csv?started=All");

  await button(page, "Go to Costs to fill in").click();
  await expect(
    page.getByRole("heading", { name: "Costs to fill in" }),
  ).toBeVisible();
});

test("Money points the owner to Figures for what each batch cost and made", async ({
  page,
}) => {
  await standIn(page, []);
  await page.goto("/");
  await button(page, "Manage").click();
  await button(page, "Money").click();

  await expect(
    page.getByText(
      "Money shows what the nursery took and spent. What each batch cost and made is under Figures.",
    ),
  ).toBeVisible();
  await button(page, "Go to Figures").click();
  await expect(
    page.getByRole("heading", { name: "What the plants cost and made" }),
  ).toBeVisible();
});

test("on a desk the plants are a table with the cost that counts in bold", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await standIn(page, []);
  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Main" })
    .getByRole("button", { name: "Figures" })
    .click();

  const table = page.getByRole("table", {
    name: "What each plant cost and made",
  });
  await expect(table).toBeVisible();
  await expect(table.getByRole("columnheader").nth(4)).toContainText(
    "Cost with VAT",
  );
  await expect(table.getByRole("columnheader").nth(5)).toContainText(
    "Cost before VAT",
  );
  await expect(
    table.locator("strong", { hasText: "£40.00 so far" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "£84.00 of the takings for Lavender 'Hidcote' is from plants sold without saying which batch",
      { exact: false },
    ),
  ).toBeVisible();
  await expectAccessible(page, "Figures, money on a desk");

  // The logo and name take you back to Record work.
  await button(page, "Hortiplanner, go to Record work").click();
  await expect(page.getByLabel("Scan a label")).toBeVisible();
});

test("on a phone the logo and name at the top go back to Record work, and Costs to fill in isn't in Manage", async ({
  page,
}) => {
  await standIn(page, []);
  await page.goto("/");
  await button(page, "Manage").click();
  await expect(button(page, "Figures")).toBeVisible();
  await expect(button(page, "Costs to fill in")).toHaveCount(0);
  await button(page, "Figures").click();
  await expect(
    page.getByRole("heading", { name: "What the plants cost and made" }),
  ).toBeVisible();

  await button(page, "Hortiplanner, go to Record work").click();
  await expect(page.getByLabel("Scan a label")).toBeVisible();
});
