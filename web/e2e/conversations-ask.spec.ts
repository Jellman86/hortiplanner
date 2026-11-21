import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

// Kept conversations against a pretend server: asked, carried on with only the new question sent, renamed, reopened
// from the list, and deleted one at a time or all at once.
test("conversations are kept, carried on, renamed, reopened and deleted on a phone", async ({
  page,
}) => {
  type Summary = {
    id: string;
    title: string;
    updatedAt: string;
    aboutKind: string | null;
    aboutLabel: string | null;
  };
  const first = "0193a6f0-0000-7000-8000-00000000c0a1";
  const older = "0193a6f0-0000-7000-8000-00000000c0a2";
  const asked: { messages: unknown[]; conversationId: string | null }[] = [];
  let kept: Summary[] = [
    {
      id: older,
      title: "Vine weevil in the heucheras",
      updatedAt: "2026-11-12T10:00:00Z",
      aboutKind: "plant",
      aboutLabel: "Heuchera 'Palace Purple'",
    },
  ];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();

    if (path === "/api/assistant/ask") {
      const body = request.postDataJSON();
      asked.push(body);
      if (!body.conversationId) {
        kept = [
          {
            id: first,
            title: "What needs attention this week?",
            updatedAt: "2026-11-19T10:00:00Z",
            aboutKind: null,
            aboutLabel: null,
          },
          ...kept,
        ];
      }
      await route.fulfill({
        json: {
          answer: "Water the benches, and check the vents.",
          sources: [],
          cost: 0.001,
          spentThisMonth: 0.1,
          spentInAll: 0.1,
          conversationId: body.conversationId ?? first,
          title: "What needs attention this week?",
        },
      });
      return;
    }

    if (path === "/api/assistant/conversations") {
      if (method === "DELETE") {
        kept = [];
        await route.fulfill({ status: 204 });
      } else {
        await route.fulfill({ json: kept });
      }
      return;
    }

    const one = /^\/api\/assistant\/conversations\/([^/]+)(\/title)?$/.exec(
      path,
    );
    if (one) {
      const summary = kept.find((item) => item.id === one[1]);
      if (method === "PUT") {
        summary!.title = request.postDataJSON().title;
        await route.fulfill({ status: 204 });
      } else if (method === "DELETE") {
        kept = kept.filter((item) => item !== summary);
        await route.fulfill({ status: 204 });
      } else if (!summary) {
        await route.fulfill({ status: 404 });
      } else {
        await route.fulfill({
          json: {
            ...summary,
            messages: [
              {
                id: "m1",
                role: "user",
                text: "What's eating the heucheras?",
                photoCount: 1,
                sources: [],
                cost: null,
                at: summary.updatedAt,
              },
              {
                id: "m2",
                role: "assistant",
                text: "That looks like vine weevil grubs.",
                photoCount: 0,
                sources: [],
                cost: 0.002,
                at: summary.updatedAt,
              },
            ],
          },
        });
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
      "/api/assistant/status": {
        canAsk: true,
        setUp: true,
        readsImages: true,
        savesConversations: true,
        spent: { thisMonth: 0.1, inAll: 0.1 },
      },
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });

  await page.goto("/");
  await page
    .locator(".topbar")
    .getByRole("button", { name: "Ask Hortiplanner" })
    .click();

  // Told once that conversations are now kept.
  await expect(
    page.getByText("Hortiplanner now keeps your conversations"),
  ).toBeVisible();
  await expectAccessible(page, "Ask Hortiplanner, conversations are kept");
  await page.getByRole("button", { name: "I understand" }).click();
  await expect(
    page.getByText("Hortiplanner now keeps your conversations"),
  ).toHaveCount(0);

  // Asked, then carried on: only the new question goes, with the conversation it belongs to.
  await page
    .getByRole("button", { name: "What needs attention this week?" })
    .click();
  await expect(
    page.getByText("Water the benches, and check the vents."),
  ).toBeVisible();
  expect(asked[0].conversationId).toBeNull();
  await page.getByLabel("Your question").fill("And next week?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect.poll(() => asked.length).toBe(2);
  expect(asked[1].conversationId).toBe(first);
  expect(asked[1].messages).toHaveLength(1);

  // Renamed.
  await page.getByRole("button", { name: "Rename" }).click();
  await page.getByLabel("Name of this conversation").fill("Weekly jobs");
  await expectAccessible(page, "Ask Hortiplanner, renaming a conversation");
  await page.getByRole("button", { name: "Save the name" }).click();
  await expect(
    page.getByRole("heading", { name: "Weekly jobs" }),
  ).toBeVisible();

  // Reopened from the list, saying a photo was sent but not kept.
  await page.getByRole("button", { name: "Your conversations" }).click();
  const list = page.locator(
    "[aria-labelledby=your-conversations] .ask-conversation-list",
  );
  await expect(list).toContainText("Weekly jobs");
  await expect(list).toContainText("About Heuchera 'Palace Purple'");
  await expectAccessible(page, "Ask Hortiplanner, your conversations");
  await list.getByRole("button", { name: /Vine weevil/ }).click();
  await expect(
    page.getByText("That looks like vine weevil grubs."),
  ).toBeVisible();
  await expect(
    page.getByText(
      "You sent a photo with this question. Hortiplanner doesn't keep photos.",
    ),
  ).toBeVisible();

  // Deleted, after asking.
  page.once("dialog", (dialog) => {
    expect(dialog.message()).toBe(
      "Delete this conversation? It can't be brought back.",
    );
    void dialog.accept();
  });
  await page.getByRole("button", { name: "Delete this conversation" }).click();
  await expect.poll(() => kept.length).toBe(1);

  // All deleted, ending on the empty list.
  await page.getByRole("button", { name: "Your conversations" }).click();
  page.once("dialog", (dialog) => void dialog.accept());
  await page
    .getByRole("button", { name: "Delete all your conversations" })
    .click();
  await expect(
    page
      .locator("[aria-labelledby=your-conversations]")
      .getByText("You haven't any conversations yet."),
  ).toBeVisible();
  await expectAccessible(page, "Ask Hortiplanner, no conversations yet");

  // What is sent and what is kept, readable on a phone.
  await page.getByRole("button", { name: "Back to asking" }).click();
  await page
    .getByRole("button", { name: "What is sent and what is kept" })
    .click();
  await expect(
    page
      .locator(".mobile-only")
      .getByText("Only you can open them: nobody else who uses Hortiplanner"),
  ).toBeVisible();
  await expectAccessible(
    page,
    "Ask Hortiplanner, what is sent and what is kept",
  );

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
