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
  pots: {
    cost: { withVat: 8.4, beforeVat: 7 },
    lines: [
      {
        size: "9cm",
        plants: 20,
        each: { withVat: 0.42, beforeVat: 0.35 },
        cost: { withVat: 8.4, beforeVat: 7 },
      },
    ],
    withoutAllowance: ["2L"],
    unsizedPlants: 0,
  },
  perPlant: {
    alive: 18,
    lost: 2,
    adjusted: 0,
    notCounted: false,
    allLost: false,
    each: { withVat: 0.8, beforeVat: 0.6667 },
    lostValue: { withVat: 1.14, beforeVat: 0.95 },
  },
  takings: {
    invoiced: 0,
    invoiceTakings: { withVat: 0, beforeVat: 0 },
    fromTrays: 4,
    trayTakings: { withVat: 18, beforeVat: 18 },
    noPrice: 0,
    total: { withVat: 18, beforeVat: 18 },
    sold: 4,
  },
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
  // What each plant has cost, with what the lost ones had cost.
  await expect(
    page.getByRole("heading", { name: "What each plant has cost" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "18 plants are still growing or have been sold, so each one has cost about £0.80 with VAT, or about £0.67 before VAT.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText(
      "2 were lost. Between them they had cost about £1.14 with VAT, or about £0.95 before VAT. That's already counted in what each plant has cost.",
    ),
  ).toBeVisible();

  // What it has taken, and what it has made so far.
  await expect(
    page.getByText("4 were sold from the tray for £18.00."),
  ).toBeVisible();
  await expect(
    page.getByText(/^It has taken £18\.00 and cost £/),
  ).toBeVisible();

  // Pots and compost, from the cost of potting set for each size.
  await expect(
    table.getByRole("row", { name: /^Pots and compost/ }),
  ).toHaveText(/Pots and compost\s*£8\.40\s*£7\.00/);
  await expect(
    page.getByText(
      "That's 20 plants potted into 9cm at £0.42 each with VAT, which is £0.35 before VAT.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText(
      "Pots and compost for 2L aren't counted, because no cost of potting is set for that size.",
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

test("the bell leads the owner to costs to fill in, answered one by one or all at once", async ({
  page,
}) => {
  const batch = (id: string, code: string, trays: string[]) => ({
    batchId: id,
    traceabilityCode: code,
    commonName: "Lavender",
    variety: "Hidcote",
    source: "SeedLot",
    startedAt: "2026-03-03T10:00:00Z",
    trays,
  });
  let missing = [
    batch("0193a6f0-0000-7000-8000-0000000000f1", "B000042", ["C000101"]),
    batch("0193a6f0-0000-7000-8000-0000000000f2", "B000043", []),
    batch("0193a6f0-0000-7000-8000-0000000000f3", "B000044", ["C000103"]),
    batch("0193a6f0-0000-7000-8000-0000000000f4", "B000045", ["C000104"]),
  ];
  const costs: Record<string, unknown>[] = [];
  const notKnown: unknown[] = [];
  // How the stand-in server answers the next cost or "mark all": as it should, or not.
  let next: "ok" | "taken" | "broken" = "ok";
  let listBroken = false;

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const answer = next;
    next = "ok";

    if (path.endsWith("/costs") && request.method() === "POST") {
      if (answer === "broken") {
        await route.fulfill({ status: 500, json: {} });
        return;
      }

      costs.push(request.postDataJSON());
      missing = missing.filter((row) => !path.includes(row.batchId));
      await route.fulfill(
        answer === "taken"
          ? { status: 409, json: { problem: "Answered" } }
          : { status: 201, json: {} },
      );
      return;
    }

    if (path === "/api/costs/not-known") {
      if (answer === "broken") {
        await route.fulfill({ status: 500, json: {} });
        return;
      }

      notKnown.push(request.postDataJSON());
      const marked = missing.length;
      missing = [];
      await route.fulfill({ json: { marked } });
      return;
    }

    if (path === "/api/costs/missing" && listBroken) {
      await route.fulfill({ status: 500, json: {} });
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
      "/api/alerts": missing.length
        ? [
            {
              kind: "BatchesNoCost",
              title: `${missing.length} batches have no cost yet`,
              why: "Nobody has said what their seed or plants cost, so we can't work out what they have made. You can add each cost, or say that it isn't known.",
              where: "costs",
              count: missing.length,
              level: "Tidy",
            },
          ]
        : [],
      "/api/costs/missing": missing,
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });

  await page.goto("/");
  await page.getByRole("button", { name: /^Needs a look/ }).click();
  await page.getByRole("button", { name: "Go to Costs to fill in" }).click();
  await expect(
    page.getByRole("heading", { name: "Costs to fill in" }),
  ).toBeVisible();
  await expect(page.getByText("4 batches have no cost yet.")).toBeVisible();
  const card = (code: string) =>
    page.getByRole("form", { name: `Lavender 'Hidcote', batch ${code}` });
  await expect(
    page.getByText("It was sown on 3 March and is in tray C000101."),
  ).toBeVisible();
  await expect(
    page.getByText("It was sown on 3 March and isn't in a tray now."),
  ).toBeVisible();
  await expectAccessible(page, "Costs to fill in");

  // Something that isn't money is pointed out at its box, which takes the focus.
  const first = card("B000042");
  const box = first.getByLabel("What the seed cost, with VAT (£)");
  await box.fill("lots");
  await first.getByRole("button", { name: "Save the cost" }).click();
  await expect(box).toHaveAttribute("aria-invalid", "true");
  await expect(box).toBeFocused();

  // A save that fails says so on its card, and keeps the batch.
  await box.fill("2.99");
  next = "broken";
  await first.getByRole("button", { name: "Save the cost" }).click();
  await expect(first.getByRole("alert")).toHaveText(
    "The answer for batch B000042 couldn't be saved. Please try again in a minute.",
  );
  await first.getByRole("button", { name: "Save the cost" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "The cost of batch B000042 is saved.",
  );
  // The focus goes to the card that took its place.
  await expect(
    page.getByRole("heading", { name: "Lavender 'Hidcote', batch B000043" }),
  ).toBeFocused();

  // A batch in no tray asks before saying nobody knows; declined, nothing is sent.
  page.once("dialog", (dialog) => void dialog.dismiss());
  await card("B000043").getByRole("button", { name: "I don't know" }).click();
  await expect(card("B000043")).toBeVisible();
  page.once("dialog", (dialog) => void dialog.accept());
  await card("B000043").getByRole("button", { name: "I don't know" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "You've said nobody knows what batch B000043 cost.",
  );

  // Someone else answered meanwhile: it says so, and the batch leaves the list.
  next = "taken";
  await card("B000044")
    .getByRole("button", { name: "It cost nothing" })
    .click();
  await expect(page.getByRole("status")).toHaveText(
    "Someone else has already answered for batch B000044, so what you put in wasn't saved and it's no longer on this list.",
  );
  await expect(card("B000044")).toHaveCount(0);
  expect(costs.map((cost) => cost.answer)).toEqual([
    "Amount",
    "NotKnown",
    "Free",
  ]);

  // Marking all: declined does nothing, a failure says so, and then it works.
  page.once("dialog", (dialog) => void dialog.dismiss());
  await page
    .getByRole("button", { name: "I don't know what any of these cost" })
    .click();
  expect(notKnown).toEqual([]);
  next = "broken";
  page.once("dialog", (dialog) => void dialog.accept());
  await page
    .getByRole("button", { name: "I don't know what any of these cost" })
    .click();
  await expect(page.getByRole("alert")).toHaveText(
    "Those answers couldn't be saved. Please try again in a minute.",
  );
  await expect(card("B000045")).toBeVisible();
  page.once("dialog", (dialog) => void dialog.accept());
  await page
    .getByRole("button", { name: "I don't know what any of these cost" })
    .click();
  await expect(page.getByRole("status")).toHaveText(
    "You've said nobody knows what that batch cost.",
  );
  expect(notKnown).toEqual([
    { batchIds: ["0193a6f0-0000-7000-8000-0000000000f4"] },
  ]);
  await expect(
    page.getByText("Every batch has a cost, or you've said it isn't known."),
  ).toBeVisible();
  await expect(page.getByText(/^These batches have no cost yet/)).toHaveCount(
    0,
  );

  // A list that can't be fetched says so, rather than that every batch has a cost.
  listBroken = true;
  await page.getByRole("button", { name: "Back", exact: true }).click();
  await page
    .getByRole("button", { name: "Costs to fill in", exact: true })
    .click();
  await expect(page.getByRole("alert")).toHaveText(
    "The list of batches couldn't be fetched just now. Please try again in a minute.",
  );
  await expect(
    page.getByText("Every batch has a cost, or you've said it isn't known."),
  ).toHaveCount(0);
});

test("plants sold from a ready tray are recorded with the price from the plant list", async ({
  page,
}) => {
  const sent: Record<string, unknown>[] = [];
  const potSize = "0193a6f0-0000-7000-8000-0000000000a9";

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (path.endsWith("/sold") && request.method() === "POST") {
      sent.push(request.postDataJSON());
      await route.fulfill({
        status: 201,
        json: {
          eventId: (request.postDataJSON() as { eventId: string }).eventId,
        },
      });
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
      "/api/containers/by-label/C000001": {
        ...tray(false),
        stage: "Ready",
        potSizeId: potSize,
        potSizeName: "9cm",
        contents: [{ ...tray(false).contents[0], quantity: 12 }],
      },
      "/api/plants": [
        {
          id: "0193a6f0-0000-7000-8000-0000000000a1",
          commonName: "Foxglove",
          variety: null,
          botanicalName: null,
          category: null,
          position: null,
          watering: null,
          hardiness: null,
          xylellaHost: false,
          protectedVariety: false,
          vatBand: "Standard",
          archived: false,
          prices: [{ potSizeId: potSize, price: 4.5 }],
          hasPhoto: false,
        },
      ],
    };
    await route.fulfill({ json: Object.hasOwn(data, path) ? data[path] : [] });
  });

  await openTray(page);
  // A ready tray offers Sold first.
  await page.getByRole("button", { name: "Sold", exact: true }).first().click();
  await expect(page.getByRole("heading", { name: "Sold" })).toBeVisible();

  const price = page.getByLabel("Price for each one (£)");
  await expect(price).toHaveValue("4.50");
  await expect(price).toHaveAccessibleDescription(
    "This is the price on the plant list for 9cm pots. Change it if they went for something different.",
  );
  await expect(
    page.getByText(
      "This takes the plants off the tray. The day's till total still goes in under Money.",
    ),
  ).toBeVisible();
  await expectAccessible(page, "Sold");

  await page.getByRole("button", { name: "+1", exact: true }).click();
  await page.getByRole("button", { name: "+1", exact: true }).click();
  await price.fill("4");
  await page.getByRole("button", { name: "Record the sale" }).click();
  await expect(page.getByRole("status")).toContainText(
    "Recorded 3 Foxglove as sold.",
  );
  expect(sent).toMatchObject([{ quantity: 3, priceEach: 4 }]);
});
