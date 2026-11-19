import { type Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const png = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);
const site = "0193a6f0-0000-7000-8000-0000000000d0";
const greenhouse = "0193a6f0-0000-7000-8000-0000000000d1";
const photo = "0193a6f0-0000-7000-8000-0000000000f9";

// "Ask about this place" and "Ask about this photo" on a place's page, against a pretend server: the question starts
// with that place, and photos are offered only when the model reads them.
async function pretend(
  page: Page,
  asked: { about?: Record<string, unknown> }[],
  readsImages: boolean,
) {
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === "/api/assistant/ask") {
      asked.push(request.postDataJSON());
      await route.fulfill({
        json: {
          answer: "Water the benches and check the vents.",
          sources: [{ kind: "place", key: greenhouse, label: "Greenhouse 2" }],
          cost: 0.001,
          spentThisMonth: 0.1,
          spentInAll: 0.1,
        },
      });
      return;
    }

    if (path.startsWith("/api/catalogue-photos/")) {
      await route.fulfill({ contentType: "image/png", body: png });
      return;
    }

    const place = (
      id: string,
      name: string,
      kind: string,
      parentId: string | null,
      photoIds: string[] = [],
    ) => ({
      id,
      name,
      kind,
      parentId,
      archived: false,
      photoIds,
      askablePhotoIds: photoIds,
      path: name,
      labelCode: `P00000${id.at(-1)}`,
      lastUsedAt: "2026-11-18T10:00:00Z",
    });
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/assistant/status": {
        canAsk: true,
        setUp: true,
        readsImages,
        spent: { thisMonth: 0.1, inAll: 0.1 },
      },
      "/api/locations": [
        place(site, "Croft View", "Site", null),
        place(greenhouse, "Greenhouse 2", "Greenhouse", site, [photo]),
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });
}

async function openGreenhouse(page: Page) {
  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "Manage" })
    .click();
  await page.getByRole("button", { name: "Places" }).click();
  await page.locator(".place-row", { hasText: "Greenhouse 2" }).click();
  await expect(
    page.getByRole("heading", { name: "Greenhouse 2" }),
  ).toBeVisible();
}

test("a place is asked about from its page, and its photo too", async ({
  page,
}) => {
  const asked: { about?: Record<string, unknown> }[] = [];
  await pretend(page, asked, true);
  await openGreenhouse(page);
  await expectAccessible(page, "A place, with Ask about this place");

  await page.getByRole("button", { name: "Ask about this place" }).click();
  await expect(
    page.getByText("About Greenhouse 2", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "Hortiplanner sends what it knows about Greenhouse 2 with your first question.",
    ),
  ).toBeVisible();
  await expectAccessible(page, "Ask Hortiplanner, about a place");
  await page
    .getByRole("button", { name: "What needs doing here this week?" })
    .click();
  await expect(
    page.getByText("Water the benches and check the vents."),
  ).toBeVisible();
  expect(asked[0].about?.placeId).toBe(greenhouse);

  // A photo of the place goes with the question, and is shown beside what it's about.
  await openGreenhouse(page);
  await page.getByRole("button", { name: "Ask about this photo" }).click();
  await expect(
    page.getByText("About a photo of Greenhouse 2", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByAltText("Sent with your first question"),
  ).toBeVisible();
  // A new question about something else starts afresh, with only that.
  await expect(
    page.getByText(
      "Hortiplanner sends this photo, and what it knows about Greenhouse 2, with your first question.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText("Water the benches and check the vents."),
  ).toHaveCount(0);
  await expectAccessible(page, "Ask Hortiplanner, about a photo of a place");
  await page.getByLabel("Your question").fill("What can you see?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect.poll(() => asked.length).toBe(2);
  expect(asked[1].about).toMatchObject({
    placeId: greenhouse,
    placePhotoId: photo,
  });

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});

test("photos aren't offered to a model that can't read them", async ({
  page,
}) => {
  await pretend(page, [], false);
  await openGreenhouse(page);
  await expect(
    page.getByRole("button", { name: "Ask about this place" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Ask about this photo" }),
  ).toHaveCount(0);
});
