import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// Staff see what needs their look on Record work: their own items, such as seedlings to count, and the jobs due
// once, in the list they tick them off from.
test("staff see their own items under Needs a look on Record work", async ({
  page,
}) => {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "staff",
        name: "Alex Moss",
        isOwner: false,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/jobs": [
        {
          id: "0193a6f0-0000-7000-8000-0000000000j1",
          title: "Water house 1",
          notes: null,
          everyDays: 7,
          nextDue: "2026-11-20",
          locationId: null,
          locationName: null,
          lastDoneOn: null,
          lastDoneBy: null,
          due: true,
          daysLate: 0,
        },
      ],
      "/api/alerts": [
        {
          kind: "JobsDue",
          title: "1 job is due",
          why: "It's on Record work, ready to tick off once it's done.",
          where: "record",
          count: 1,
          level: "Tidy",
        },
        {
          kind: "NotCountedLong",
          title: "2 trays of seedlings have gone three weeks without a count",
          why: "We know what was sown, but not how many plants came up.",
          where: "production",
          count: 2,
          level: "Tidy",
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Needs a look" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "2 trays of seedlings have gone three weeks without a count",
    ),
  ).toBeVisible();
  // The job is listed once, ready to tick off, rather than also as an item.
  await expect(page.getByText("1 job is due")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Jobs due" })).toBeVisible();
  await expect(page.getByText("Water house 1")).toBeVisible();
  await expectAccessible(page, "Record work, staff with things to look at");

  await page.getByRole("button", { name: "Go to Production" }).click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();
});
