import type { Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const id = (n: number) =>
  `0193a6f0-0000-7000-8000-${String(n).padStart(12, "0")}`;

const place = (n: number, name: string, photos: number) => ({
  id: id(n),
  name,
  kind: "Greenhouse",
  parentId: null,
  archived: false,
  photoIds: Array.from({ length: photos }, (_, i) => id(100 * n + i)),
  path: name,
  labelCode: `L00000${n}`,
  lastUsedAt: null,
});

/** Stands in for the server: the owner, three places with no, one and six photos, and the logo as every photo. */
async function standIn(page: Page) {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;

    if (path.startsWith("/api/catalogue-photos/")) {
      await route.fulfill({ path: "public/logo-96.png" });
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
      "/api/locations": [
        place(1, "Greenhouse 1", 0),
        place(2, "Greenhouse 2", 1),
        place(3, "Polytunnel", 6),
      ],
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

test("pointing at a place shows its newest photos, larger", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await standIn(page);
  await page.goto("/");
  await page
    .locator(".sidebar")
    .getByRole("button", { name: "Places", exact: true })
    .click();
  await expect(page.getByRole("heading", { name: "Places" })).toBeVisible();

  const row = (name: string) => page.locator(".place-row", { hasText: name });
  await expect(row("Polytunnel").locator(".photo-peek")).toBeHidden();

  await row("Polytunnel").hover();
  const peek = row("Polytunnel").locator(".photo-peek");
  await expect(peek).toBeVisible();
  await expect(peek.locator("img")).toHaveCount(4);
  await expect(peek).toContainText(
    "Polytunnel has 6 photos. These are the 4 newest.",
  );
  // Once it has faded in.
  await page.waitForTimeout(300);
  await page.screenshot({ path: "build/peek/place-photos.png" });
  await expectAccessible(page, "Places, pointing at a place with photos");

  await row("Greenhouse 2").hover();
  await expect(row("Greenhouse 2").locator(".photo-peek")).toContainText(
    "Greenhouse 2 has 1 photo.",
  );
  await expect(peek).toBeHidden();
  await expect(row("Greenhouse 1").locator(".photo-peek")).toHaveCount(0);

  // The keyboard brings it up too.
  await row("Polytunnel").focus();
  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Tab");
  await expect(peek).toBeVisible();
});
