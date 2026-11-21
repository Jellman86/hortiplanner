import type { Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

/** Stands in for the server: someone signed in, with nothing due and nothing in production. */
async function standIn(page: Page, isOwner: boolean) {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: isOwner ? "owner" : "staff",
        name: isOwner ? "Sam Fielding" : "Alex Moss",
        isOwner,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

const button = (page: Page, name: string) =>
  page.getByRole("button", { name, exact: true });

test("a new batch starts from the top of Production and from Record work", async ({
  page,
}) => {
  await standIn(page, false);
  await page.goto("/");
  await button(page, "Start a new batch").click();
  await expect(
    page.getByRole("heading", { name: "Find the plant" }),
  ).toBeVisible();

  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Production" })
    .click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();
  await button(page, "Start a new batch").click();
  await expect(
    page.getByRole("heading", { name: "Find the plant" }),
  ).toBeVisible();
});

test("the owner reaches every job from Jobs due, even with none due today", async ({
  page,
}) => {
  await standIn(page, true);
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Jobs due" })).toBeVisible();
  await expect(page.getByText("No jobs are due today.")).toBeVisible();
  await expectAccessible(page, "Record work, owner with no jobs due");

  await button(page, "See all the jobs").click();
  await expect(page.getByRole("heading", { name: "Jobs" })).toBeVisible();
});

test("staff see no Jobs due when nothing is due", async ({ page }) => {
  await standIn(page, false);
  await page.goto("/");
  await expect(page.getByLabel("Scan a label")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Jobs due" })).toHaveCount(0);
  await expect(button(page, "See all the jobs")).toHaveCount(0);
});
