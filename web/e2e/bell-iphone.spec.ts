import { type Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

type Alert = {
  kind: string;
  title: string;
  why: string;
  where: string;
  count: number;
  level: string;
};

const jobsDue: Alert = {
  kind: "JobsDue",
  title: "1 job is due",
  why: "It's on Record work, ready to tick off once it's done.",
  where: "record",
  count: 1,
  level: "Tidy",
};

const seedlings: Alert = {
  kind: "NotCountedLong",
  title: "2 trays of seedlings have gone three weeks without a count",
  why: "We know what was sown, but not how many plants came up.",
  where: "production",
  count: 2,
  level: "Tidy",
};

const invoices: Alert = {
  kind: "InvoicesOverdue",
  title: "1 invoice is overdue",
  why: "It hasn't been marked as paid.",
  where: "invoices",
  count: 1,
  level: "Soon",
};

// A pretend server whose list of things that need a look can change while the app is open.
async function pretend(
  page: Page,
  state: { alerts: Alert[]; offline: boolean },
) {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;

    if (path === "/api/alerts") {
      if (state.offline) {
        await route.abort("internetdisconnected");
      } else {
        await route.fulfill({ json: state.alerts });
      }
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });
}

test("the bell shows what needs a look from any screen on a phone", async ({
  page,
}) => {
  const state = { alerts: [jobsDue, seedlings], offline: false };
  await pretend(page, state);
  await page.goto("/");

  const bell = page.getByRole("button", { name: "Needs a look, 2 things" });
  await expect(bell).toBeVisible();

  // From another screen, it's still there and opens the list.
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Production" })
    .click();
  await bell.click();
  await expect(
    page.getByRole("heading", { name: "Needs a look" }),
  ).toBeVisible();
  await expect(page.getByText(seedlings.title)).toBeVisible();
  await expect(page.getByText(jobsDue.title)).toBeVisible();
  await expectAccessible(page, "Needs a look");

  // An item's button goes to where it's dealt with.
  await page.getByRole("button", { name: "Go to Production" }).click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();

  // Dealt with elsewhere: coming back into view checks again, and the number falls.
  state.alerts = [seedlings];
  await page.evaluate(() =>
    document.dispatchEvent(new Event("visibilitychange")),
  );
  await expect(
    page.getByRole("button", { name: "Needs a look, 1 thing" }),
  ).toBeVisible();

  // Something new while the app is open is said once, politely.
  state.alerts = [seedlings, invoices];
  await page.evaluate(() =>
    document.dispatchEvent(new Event("visibilitychange")),
  );
  await expect(page.locator(".needs-said")).toHaveText(
    "Something new needs a look: 1 invoice is overdue.",
  );

  // Without a signal the last list stays, with when it was checked.
  state.offline = true;
  await page.getByRole("button", { name: "Needs a look, 2 things" }).click();
  await expect(
    page.getByText(
      /This list was last checked at \d\d:\d\d, and there's no signal/,
    ),
  ).toBeVisible();
  await expect(page.getByText(invoices.title, { exact: true })).toBeVisible();

  // Nothing at all: the list says so.
  state.offline = false;
  state.alerts = [];
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Production" })
    .click();
  await page.getByRole("button", { name: "Needs a look, 2 things" }).click();
  await expect(page.getByText("Nothing needs a look just now.")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Needs a look, nothing just now" }),
  ).toBeVisible();

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});

test("the bell is in the sidebar on a desk", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const state = {
    alerts: Array.from({ length: 12 }, (_, i) => ({
      ...seedlings,
      kind: `K${i}`,
    })),
    offline: false,
  };
  await pretend(page, state);
  await page.goto("/");

  const sidebar = page.locator(".sidebar");
  const entry = sidebar.getByRole("button", {
    name: "Needs a look, 12 things",
  });
  await expect(entry).toBeVisible();
  await expect(entry.locator(".bell-badge")).toHaveAttribute(
    "data-count",
    "9+",
  );
  await entry.click();
  await expect(
    page.getByRole("heading", { name: "Needs a look" }),
  ).toBeVisible();
  await expect(entry).toHaveAttribute("aria-current", "page");

  // Collapsed, the number stays on the bell.
  await sidebar.getByRole("button", { name: "Collapse sidebar" }).click();
  await expect(entry.locator(".bell-badge")).toBeVisible();
  await expectAccessible(page, "Needs a look on a desk");
});

test("ticking a job lowers the bell, and work that wasn't saved is on it", async ({
  page,
}) => {
  const jobId = "0193a6f0-0000-7000-8000-0000000000j1";
  let done = false;

  // Something recorded on this phone that the server refused.
  await page.addInitScript(() =>
    localStorage.setItem(
      "hortiplanner.queue.owner",
      JSON.stringify({
        waiting: [],
        failed: [
          { eventId: "e1", description: "Lost 3 Lavender", reason: "Too many" },
        ],
      }),
    ),
  );

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === "/api/alerts") {
      await route.fulfill({ json: done ? [invoices] : [jobsDue, invoices] });
      return;
    }

    if (path === `/api/jobs/${jobId}/done`) {
      done = true;
      await route.fulfill({ status: 201, json: { nextDue: "2026-11-27" } });
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/jobs": [
        {
          id: jobId,
          title: "Water house 1",
          notes: null,
          everyDays: 7,
          nextDue: "2026-11-20",
          locationId: null,
          locationName: null,
          lastDoneOn: null,
          lastDoneBy: null,
          due: !done,
          daysLate: 0,
        },
      ],
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });

  await page.goto("/");
  // The job, the invoice and the work that wasn't saved.
  const bell = page.getByRole("button", { name: "Needs a look, 3 things" });
  await expect(bell).toBeVisible();

  // It can take the keyboard's focus. Record work puts the cursor in its scan box as it opens, so this waits for
  // the page to settle.
  await expect(async () => {
    await bell.focus();
    await expect(bell).toBeFocused({ timeout: 500 });
  }).toPass();

  // From Settings too, and Back returns there.
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Settings" })
    .click();
  await bell.click();
  await expect(page).toHaveTitle(/^Needs a look/);
  await expect(
    page.getByText("1 thing you recorded couldn't be saved", { exact: true }),
  ).toBeVisible();
  await page.goBack();
  await expect(page.getByRole("heading", { name: "Settings" })).toBeVisible();

  // Ticking the job off on Record work lowers the number without a reload.
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Record" })
    .click();
  await page.getByRole("button", { name: "Done" }).first().click();
  await expect(
    page.getByRole("button", { name: "Needs a look, 2 things" }),
  ).toBeVisible();
});
