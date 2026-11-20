import type { Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const batchId = "0193a6f0-0000-7000-8000-0000000000b1";

const tray = (costMissing: boolean) => ({
  id: "0193a6f0-0000-7000-8000-0000000000c1",
  labelCode: "C000001",
  kind: "Tray",
  contents: [
    {
      batchId,
      traceabilityCode: "B000001",
      plantId: "0193a6f0-0000-7000-8000-0000000000a1",
      commonName: "Foxglove",
      variety: null,
      quantity: 30,
      source: "SeedLot",
      notCounted: false,
      rough: false,
      costMissing,
    },
  ],
  locationId: null,
  locationName: "Greenhouse 2, Bench 2",
  potSizeId: null,
  potSizeName: null,
  stage: "Germinated",
  stageSince: "2026-11-10T09:00:00Z",
  startedAt: "2026-11-01T09:00:00Z",
});

const entry = (overrides: Record<string, unknown> = {}) => ({
  id: "0193a6f0-0000-7000-8000-0000000000d1",
  kind: "Material",
  answer: "Amount",
  amountPaid: 2.99,
  includesVat: true,
  vatBand: "Standard",
  vatRate: 20,
  vatSaid: false,
  cost: { withVat: 2.99, beforeVat: 2.49 },
  note: null,
  addedBy: "Alex Moss",
  occurredAt: "2026-11-03T10:00:00Z",
  addedAt: "2026-11-03T10:00:00Z",
  replaced: false,
  replacesId: null,
  ...overrides,
});

const costing = (material: Record<string, unknown>, history: unknown[]) => ({
  batchId,
  traceabilityCode: "B000001",
  source: "SeedLot",
  plantVatBand: "Standard",
  vatRegistered: false,
  counts: "WithVat",
  material: { purchase: null, superseded: null, ...material },
  others: [],
  total: material.cost ?? null,
  history,
  standardRate: 20,
  reducedRate: 5,
});

/** Stands in for the server: who is signed in, the tray, and what happens to a cost sent. */
async function standIn(
  page: Page,
  isOwner: boolean,
  respond: (body: Record<string, unknown>) => unknown,
  costingNow: () => unknown = () => [],
) {
  let missing = true;

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path === `/api/batches/${batchId}/costs`) {
      const answer = respond(request.postDataJSON());

      if (answer === "offline") {
        await route.abort("internetdisconnected");
        return;
      }

      missing = false;
      await route.fulfill({ status: 201, json: answer });
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: isOwner ? "owner" : "staff",
        name: isOwner ? "Sam Fielding" : "Alex Moss",
        isOwner,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/containers/by-label/C000001": tray(missing),
      [`/api/batches/${batchId}/costing`]: costingNow(),
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });
}

async function openTray(page: Page) {
  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
}

test("staff say what a batch's seed cost from its tray, and see no amount afterwards", async ({
  page,
}) => {
  const sent: Record<string, unknown>[] = [];
  await standIn(page, false, (body) => {
    sent.push(body);
    return { eventId: body.eventId };
  });
  await openTray(page);

  await expect(
    page.getByText("Nobody has said what this batch's seed cost yet."),
  ).toBeVisible();
  await expectAccessible(page, "Tray asking what its seed cost");
  await page.getByRole("button", { name: "Add what it cost" }).click();

  await expect(
    page.getByRole("heading", { name: "What did it cost?" }),
  ).toBeVisible();
  const box = page.getByLabel("What did this seed cost? (£)");
  await expect(box).toHaveAccessibleDescription(/with any VAT included/);
  await expectAccessible(page, "What did it cost?");

  await box.fill("two pounds");
  await page.getByRole("button", { name: "Save the cost" }).click();
  await expect(page.getByRole("alert")).toHaveText(
    "Please put the cost in as pounds and pence, such as 2.99.",
  );

  await box.fill("£2.99");
  await page.getByRole("button", { name: "Save the cost" }).click();
  await expect(page.getByRole("status")).toHaveText("The cost is saved.");
  expect(sent).toMatchObject([
    { kind: "Material", answer: "Amount", amount: 2.99 },
  ]);
  expect(sent[0]).not.toHaveProperty("vatBand");

  // Back on the tray: nothing more to ask, and no amount or costing for staff.
  await expect(
    page.getByText("Nobody has said what this batch's seed cost yet."),
  ).toHaveCount(0);
  await expect(page.getByText("£2.99")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Costs and takings" }),
  ).toHaveCount(0);
});

test("a cost said without a signal waits on the phone and is sent when the signal returns", async ({
  page,
}) => {
  let online = false;
  const sent: Record<string, unknown>[] = [];
  await standIn(page, false, (body) => {
    if (!online) {
      return "offline";
    }

    sent.push(body);
    return { eventId: body.eventId };
  });
  await openTray(page);
  await page.getByRole("button", { name: "Add what it cost" }).click();
  await page
    .getByLabel("It cost nothing, because it was our own seed or a gift.")
    .check();
  await page.getByRole("button", { name: "Save the cost" }).click();

  await expect(page.getByRole("status")).toHaveText(
    "The cost is kept on this device, and it will be sent when there's a signal.",
  );
  // It doesn't ask again while its answer waits on the phone.
  await expect(
    page.getByText("Nobody has said what this batch's seed cost yet."),
  ).toHaveCount(0);

  online = true;
  await page.evaluate(() => window.dispatchEvent(new Event("online")));
  await expect.poll(() => sent.length).toBe(1);
  expect(sent[0]).toMatchObject({ kind: "Material", answer: "Free" });
});

test("the owner sees both figures, changes the VAT and keeps the earlier answer", async ({
  page,
}) => {
  const sent: Record<string, unknown>[] = [];
  let changed = false;
  await standIn(
    page,
    true,
    (body) => {
      sent.push(body);
      changed = true;
      return { eventId: body.eventId };
    },
    () =>
      changed
        ? costing(
            {
              state: "Amount",
              cost: { withVat: 3.59, beforeVat: 2.99 },
              entry: entry({
                id: "0193a6f0-0000-7000-8000-0000000000d2",
                includesVat: false,
                vatSaid: true,
                cost: { withVat: 3.59, beforeVat: 2.99 },
                addedBy: "Sam Fielding",
                addedAt: "2026-11-20T10:00:00Z",
                replacesId: "0193a6f0-0000-7000-8000-0000000000d1",
              }),
            },
            [
              entry({ replaced: true }),
              entry({
                id: "0193a6f0-0000-7000-8000-0000000000d2",
                includesVat: false,
                vatSaid: true,
                cost: { withVat: 3.59, beforeVat: 2.99 },
                addedBy: "Sam Fielding",
                addedAt: "2026-11-20T10:00:00Z",
                replacesId: "0193a6f0-0000-7000-8000-0000000000d1",
              }),
            ],
          )
        : costing({ state: "Amount", cost: entry().cost, entry: entry() }, [
            entry(),
          ]),
  );
  await openTray(page);
  await page.getByRole("button", { name: "Costs and takings" }).click();

  const table = page.getByRole("table", { name: "What batch B000001 cost" });
  await expect(
    table.getByRole("columnheader", {
      name: /^With VAT ?, the figures that count$/,
    }),
  ).toBeVisible();
  await expect(table.getByRole("row", { name: /^Seed/ })).toHaveText(
    /Seed\s*£2\.99\s*£2\.49/,
  );
  await expect(
    page.getByText(
      "Alex Moss added the cost of the seed on 3 November. Nobody has said how much of it was VAT, so it's taken as including VAT at 20%.",
    ),
  ).toBeVisible();
  await expectAccessible(page, "Costs and takings");

  await page.getByRole("button", { name: "Change the VAT" }).click();
  await expect(
    page.getByRole("heading", { name: "Change the VAT" }),
  ).toBeFocused();
  await page
    .getByRole("radio", { name: "No, it's the price before VAT." })
    .check();
  await expectAccessible(page, "Change the VAT");
  await page.getByRole("button", { name: "Save the VAT" }).click();

  await expect(page.getByRole("status")).toHaveText(
    "That makes it £3.59 with VAT and £2.99 before VAT.",
  );
  await expect(
    page.getByRole("heading", { name: "What it cost" }),
  ).toBeFocused();
  expect(sent).toMatchObject([
    {
      kind: "Material",
      answer: "Amount",
      amount: 2.99,
      vatBand: "Standard",
      includesVat: false,
      replacesId: "0193a6f0-0000-7000-8000-0000000000d1",
    },
  ]);
  await expect(table.getByRole("row", { name: /^Seed/ })).toHaveText(
    /Seed\s*£3\.59\s*£2\.99/,
  );
  await expect(
    page.getByText(
      "Alex Moss added the cost of the seed on 3 November. You said £2.99 was the price before VAT, and VAT at 20% was added.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText(
      "Replaced: £2.99 including VAT at 20%, added by Alex Moss on 3 November.",
    ),
  ).toBeVisible();
});

test("the owner changes and removes anything else added, with the figures before VAT first once registered", async ({
  page,
}) => {
  const sent: Record<string, unknown>[] = [];
  const pellets = entry({
    id: "0193a6f0-0000-7000-8000-0000000000e1",
    kind: "Other",
    amountPaid: 3,
    cost: { withVat: 3, beforeVat: 2.5 },
    note: "slug pellets",
  });
  await standIn(
    page,
    true,
    (body) => {
      sent.push(body);
      return { eventId: body.eventId };
    },
    () => ({
      ...costing(
        {
          state: "Free",
          cost: { withVat: 0, beforeVat: 0 },
          entry: entry({ answer: "Free", amountPaid: null, cost: null }),
        },
        [pellets],
      ),
      vatRegistered: true,
      counts: "BeforeVat",
      others: [pellets],
      total: { withVat: 3, beforeVat: 2.5 },
    }),
  );
  await openTray(page);
  await page.getByRole("button", { name: "Costs and takings" }).click();

  const table = page.getByRole("table", { name: "What batch B000001 cost" });
  await expect(table.getByRole("columnheader").nth(1)).toHaveText(
    "Before VAT, the figures that count",
  );
  await expect(
    table.getByRole("row", { name: /^Something else: slug pellets/ }),
  ).toHaveText(/£2\.50\s*£3\.00/);

  // Changing it names the entry it replaces.
  await page.getByRole("button", { name: "Change the cost" }).nth(1).click();
  await expect(page.getByLabel("What was it?")).toHaveValue("slug pellets");
  await page.getByLabel("What did it cost, with VAT? (£)").fill("nonsense");
  await page.getByRole("button", { name: "Save the cost" }).click();
  await expect(
    page.getByLabel("What did it cost, with VAT? (£)"),
  ).toHaveAttribute("aria-invalid", "true");
  await expectAccessible(page, "Change this cost");
  await page.getByLabel("What did it cost, with VAT? (£)").fill("3.60");
  await page.getByRole("button", { name: "Save the cost" }).click();
  await expect(page.getByRole("status")).toHaveText("The cost is changed.");

  page.once("dialog", (dialog) => void dialog.accept());
  await page.getByRole("button", { name: "Remove" }).click();
  await expect(page.getByRole("status")).toHaveText("The cost is removed.");

  expect(sent).toMatchObject([
    {
      kind: "Other",
      answer: "Amount",
      amount: 3.6,
      note: "slug pellets",
      replacesId: pellets.id,
    },
    { kind: "Other", answer: "Removed", replacesId: pellets.id },
  ]);
});
