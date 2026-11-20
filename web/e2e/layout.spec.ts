import { type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { expect, test } from "./fixtures";

// Every screen, with the example data, at a phone, a tablet, a small laptop and two desks: none may scroll sideways.
// It needs example data switched on, so it runs on its own with `npm run e2e:layout`. With LAYOUT_SHOTS set to a
// folder, it also saves a picture of each screen at each width, for checking a layout change by eye.
const shots = process.env.LAYOUT_SHOTS;
const widths = [390, 768, 1024, 1440, 1920];

async function addPasskeySupport(page: Page) {
  const session = await page.context().newCDPSession(page);
  await session.send("WebAuthn.enable");
  await session.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
}

const sidebar = (page: Page, name: string) =>
  page.locator(".sidebar").getByRole("button", { name, exact: true });

test.use({ actionTimeout: 10_000 });

test("every screen fits the width of a phone, a tablet and a desk", async ({
  page,
}) => {
  test.setTimeout(600_000);
  const overflowing: string[] = [];

  async function check(name: string) {
    for (const width of widths) {
      await page.setViewportSize({ width, height: width < 900 ? 844 : 1000 });
      // Container queries and grids settle after the resize.
      await page.waitForTimeout(150);
      const wide = await page.evaluate(
        () => document.documentElement.scrollWidth - window.innerWidth,
      );
      if (wide > 0) {
        overflowing.push(`${name} at ${width}px is ${wide}px too wide`);
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
  await addPasskeySupport(page);
  page.on("dialog", (dialog) => void dialog.accept());
  await page.setViewportSize({ width: 1440, height: 1000 });

  await page.goto("/");
  await check("setup");
  await page.getByLabel("Setup code").fill("e2e-setup-code");
  await page.getByLabel("Your name").fill("Sam Fielding");
  await page.getByRole("button", { name: "Create my passkey" }).click();
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

  for (const button of [
    "New batch",
    "Production",
    "Plants",
    "Places",
    "Trace a batch",
    "Mother plants",
    "Jobs",
    "Climate",
    "Figures",
    "Money",
    "Invoices",
    "Consignments",
    "Customers",
    "Purchases",
    "Suppliers",
  ]) {
    await sidebar(page, button).click();
    await expect(page.locator("main h1").first()).toBeVisible();
    // Lists fill in after the heading.
    await page.waitForTimeout(800);
    await check(button.toLowerCase().replaceAll(" ", "-"));
  }

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

  await sidebar(page, "Trace a batch").click();
  await page.locator(".place-row").first().click();
  await expect(page.locator("h1")).toContainText("Batch");
  await check("batch");

  // One of the batch's trays.
  await page.locator(".split.trace .place-row").first().click();
  await expect(page.locator(".tray-layout")).toBeVisible();
  await check("tray");

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

  expect(overflowing, "screens wider than the window").toEqual([]);
});
