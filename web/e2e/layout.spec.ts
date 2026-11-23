import { type Locator, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// The everyday screens and every Settings page, with the example data, at a phone, a tablet, a small laptop and two
// desks: none may scroll sideways, and nothing may stick out past the page's edge.
// It needs its own example data in each browser. Run with npm run e2e:layout or npm run e2e:layout:webkit.
// Set LAYOUT_SHOTS to a folder to save a picture of each screen at each width.
const shots = process.env.LAYOUT_SHOTS;
const widths = [390, 768, 1024, 1440, 1920];

async function box(locator: Locator) {
  await expect(locator).toBeVisible();
  return (await locator.boundingBox())!;
}

const sidebar = (page: Page, name: string) =>
  page.locator(".sidebar").getByRole("button", { name, exact: true });

test.use({ actionTimeout: 10_000 });

test("the screens fit the width of a phone, a tablet and a desk", async ({
  page,
}) => {
  test.setTimeout(600_000);
  const overflowing: string[] = [];
  const main = page.locator("main");

  async function checkDesktop(name: string, width: number) {
    const rem = await page.evaluate(() =>
      parseFloat(getComputedStyle(document.documentElement).fontSize),
    );
    const pageBox = await box(main);
    const pageWidth = [
      "new-batch",
      "lost",
      "new-invoice",
      "consignments",
    ].includes(name)
      ? 48
      : [
            "record-work",
            "figures",
            "production",
            "plants",
            "invoices",
            "costs-to-fill-in",
          ].includes(name)
        ? 92
        : 76;
    expect(pageBox.width, `${name} at ${width}px`).toBeLessThanOrEqual(
      pageWidth * rem + 1,
    );
    if (!name.startsWith("settings-")) {
      const content = await box(page.locator(".with-sidebar > .content"));
      expect(pageBox.width).toBeCloseTo(
        Math.min(content.width, pageWidth * rem),
        0,
      );
      expect(pageBox.x - content.x).toBeCloseTo(
        (content.width - pageBox.width) / 2,
        0,
      );
    }

    async function beside(left: Locator, right: Locator) {
      const [first, next] = await Promise.all([box(left), box(right)]);
      expect(next.x).toBeGreaterThan(first.x + first.width);
      expect(Math.abs(first.y - next.y)).toBeLessThan(2);
    }

    if (name === "money" || name === "purchases") {
      const form = main.locator(".split > section").first();
      const list = main.locator(".split > section").last();
      await beside(form, list);
      const listBox = await box(list);
      const tableBox = await box(list.locator(".table-wrap").first());
      expect(tableBox.width).toBeCloseTo(listBox.width, 0);
      if (name === "money") {
        expect((await box(list.locator(".stat-tiles"))).width).toBeCloseTo(
          listBox.width,
          0,
        );
      }
    } else if (name === "record-work") {
      const scan = main.locator(".split.home > section").first();
      const recent = main.locator(".split.home > section").nth(1);
      const overview = main.locator(".home-glance");
      await beside(scan, recent);
      await beside(recent, overview);
    } else if (name === "figures") {
      for (const item of await main
        .locator(".money-figures > :is(.segmented, .caution)")
        .all()) {
        expect((await box(item)).width).toBeLessThanOrEqual(48 * rem + 1);
      }
    } else if (name === "plants") {
      const search = await box(
        main.getByRole("searchbox", { name: "Find or add a plant" }),
      );
      expect(search.width).toBeLessThanOrEqual(48 * rem + 1);
    } else if (name === "plant") {
      const media = main.locator(".plant-page-media");
      const notes = main.locator(".plant-page-read");
      await beside(media, notes);
      const search = main.getByRole("searchbox", {
        name: "Search for this plant",
      });
      await expect(search).toBeVisible();
      const [searchBox, notesBox] = await Promise.all([
        box(search),
        box(notes),
      ]);
      expect(searchBox.x).toBeGreaterThanOrEqual(notesBox.x - 1);
      expect(searchBox.x + searchBox.width).toBeLessThanOrEqual(
        notesBox.x + notesBox.width + 1,
      );
    } else if (name === "new-invoice") {
      const customer = await box(main.locator("form > .form-card").first());
      for (const item of await main
        .locator("form > :is(.invoice-line, .card)")
        .all()) {
        const itemBox = await box(item);
        expect(itemBox.x).toBeCloseTo(customer.x, 0);
        expect(itemBox.width).toBeCloseTo(customer.width, 0);
      }
    }
  }

  async function check(name: string) {
    // Lists fill in after the heading, and the app may keep asking for things, so idle is waited for only so long.
    await page
      .waitForLoadState("networkidle", { timeout: 5_000 })
      .catch(() => {});
    await page.waitForTimeout(300);
    for (const width of widths) {
      await page.setViewportSize({ width, height: width < 900 ? 844 : 1000 });
      // Container queries and grids settle after the resize.
      await page.waitForTimeout(150);
      const wide = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth,
      );
      if (wide > 0) {
        overflowing.push(`${name} at ${width}px is ${wide}px too wide`);
      }
      // Something can stick out of its page without the page scrolling, such as a form's row too wide for it.
      const sticking = await page.evaluate(() => {
        const main = document.querySelector("main");
        if (!main) {
          return [];
        }
        const edge = main.getBoundingClientRect().right;
        const found: string[] = [];
        for (const element of main.querySelectorAll<HTMLElement>("*")) {
          const box = element.getBoundingClientRect();
          if (box.width === 0 || box.right <= edge + 1) {
            continue;
          }
          // Inside something that scrolls or clips on purpose, such as a wide table's frame.
          let clipped = false;
          for (
            let parent = element.parentElement;
            parent && parent !== main;
            parent = parent.parentElement
          ) {
            if (getComputedStyle(parent).overflowX !== "visible") {
              clipped = true;
              break;
            }
          }
          if (!clipped) {
            found.push(
              `${element.tagName.toLowerCase()}.${[...element.classList].join(".")}`,
            );
          }
        }
        return found.slice(0, 3);
      });
      if (sticking.length > 0) {
        overflowing.push(
          `${name} at ${width}px has ${sticking.join(", ")} past the page's edge`,
        );
      }
      if (width === 390) {
        const bar = await page.evaluate(() => {
          const nav = document.querySelector(".bottom-nav");
          return nav ? nav.scrollWidth - nav.clientWidth : 0;
        });
        if (bar > 0) {
          overflowing.push(`${name}: the bottom bar is ${bar}px too wide`);
        }
      }
      if (width >= 1440 && name !== "setup") {
        await checkDesktop(name, width);
      }
      if (shots) {
        await page.screenshot({
          path: `${shots}/${name}-${width}.png`,
          fullPage: true,
        });
      }
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
  }

  if (shots) {
    mkdirSync(shots, { recursive: true });
  }
  page.on("dialog", (dialog) => void dialog.accept());
  await page.setViewportSize({ width: 1440, height: 1000 });

  await page.goto("/");
  await check("setup");
  await page.getByLabel("Setup code").fill("e2e-setup-code");
  await page.getByLabel("Your name").fill("Sam Fielding");
  await page
    .getByLabel("Password (at least 8 characters)")
    .fill("layout-example-password");
  await page.getByRole("button", { name: "Use this password" }).click();
  await expect(
    page.getByRole("heading", { name: "Hello, Sam Fielding" }),
  ).toBeVisible();

  await sidebar(page, "Settings").click();
  await page.getByRole("button", { name: "Data and backups" }).first().click();
  await page.getByRole("button", { name: "Load example data" }).click();
  await expect(
    page.getByRole("button", { name: "Load example data" }),
  ).toHaveCount(0);

  await sidebar(page, "Record work").click();
  await expect(page.getByRole("heading", { name: "Recent" })).toBeVisible();
  await check("record-work");

  // Each place in the sidebar, then the other pages in its row: the screen's name is the page's.
  const row = (hub: string, name: string) =>
    page.getByRole("navigation", { name: hub }).getByRole("button", { name });
  for (const [hub, pages] of [
    ["Production", ["Production", "Trace a batch"]],
    ["Plants", ["Plants", "Mother plants"]],
    ["Places", ["Places", "Climate"]],
    ["Selling", ["Invoices", "Customers", "Consignments"]],
    ["Buying", ["Purchases", "Suppliers"]],
    ["Money and figures", ["Money", "Figures"]],
  ] as const) {
    await sidebar(page, hub).click();
    for (const [index, name] of pages.entries()) {
      if (index > 0) await row(hub, name).click();
      await expect(
        page.getByRole("heading", { name, exact: true }),
      ).toBeVisible();
      await check(name.toLowerCase().replaceAll(" ", "-"));
    }
  }

  // New batch from the top of Production, and the jobs from Record work.
  await sidebar(page, "Production").click();
  await page.getByRole("button", { name: "Start a new batch" }).click();
  await expect(page.locator("main h1").first()).toBeVisible();
  await check("new-batch");
  await sidebar(page, "Record work").click();
  await page.getByRole("button", { name: "See all the jobs" }).click();
  await expect(page.getByRole("heading", { name: "Jobs" })).toBeVisible();
  await check("jobs");

  // Costs to fill in has no place in the sidebar: it's reached from Figures, as from the bell.
  await sidebar(page, "Money and figures").click();
  await row("Money and figures", "Figures").click();
  await page.getByRole("button", { name: "Go to Costs to fill in" }).click();
  await expect(
    page.getByRole("heading", { name: "Costs to fill in" }),
  ).toBeVisible();
  await check("costs-to-fill-in");

  await sidebar(page, "Plants").click();
  await page.locator(".plant-card").first().click();
  await expect(
    page.locator(".plant-page, .plant-layout").first(),
  ).toBeVisible();
  await check("plant");

  await sidebar(page, "Places").click();
  await page.locator(".place-row").first().click();
  await expect(page.locator("h1")).toBeVisible();
  await check("place");

  await sidebar(page, "Production").click();
  await row("Production", "Trace a batch").click();
  await page.locator(".place-row").first().click();
  await expect(page.locator("h1")).toContainText("Batch");
  await expect(
    page.getByRole("heading", { name: "What it cost and took" }),
  ).toBeVisible();
  await check("batch");
  await expectAccessible(page, "Trace report, with what it cost");

  // One of the batch's trays.
  await page.locator(".split.trace .place-row").first().click();
  await expect(page.locator(".tray-layout")).toBeVisible();
  await check("tray");

  // A one-task step in two columns.
  await page.getByRole("button", { name: "Lost", exact: true }).click();
  await expect(page.locator(".action-columns")).toBeVisible();
  await check("lost");

  // A new invoice, with the customer, line and total cards sharing the same width.
  await sidebar(page, "Selling").click();
  await page.getByRole("button", { name: "New invoice" }).click();
  await expect(
    page.getByRole("heading", { name: "New invoice" }),
  ).toBeVisible();
  await check("new-invoice");

  await sidebar(page, "Settings").click();
  const settings = page.getByRole("navigation", { name: "Settings" });
  for (const item of await settings.getByRole("button").allTextContents()) {
    const name = item.trim();
    if (!name || name === "Sign out") {
      continue;
    }
    await settings.getByRole("button", { name, exact: true }).click();
    await expect(page.locator("main h1").last()).toBeVisible();
    await check(`settings-${name.toLowerCase().replaceAll(" ", "-")}`);
  }

  expect(overflowing, "screens that don't fit").toEqual([]);
});
