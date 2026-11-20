import { type Page } from "@playwright/test";
import { expectAccessible } from "./accessibility";
import { expect, test } from "./fixtures";

const png = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);
const plant = "0193a6f0-0000-7000-8000-0000000000a1";
const supplier = "0193a6f0-0000-7000-8000-0000000000s1";

type Sent = { url: string; method: string; body: unknown };

const reading = {
  readable: true,
  what: "seed_packet",
  commonName: "Sweet Pea",
  variety: "Cupani",
  botanicalName: "Lathyrus odoratus",
  supplierName: "Chiltern Seeds",
  supplierPassportNumber: "GB 12345",
  lotOrReference: "L2291",
  sowBy: "2027-12-31",
  contentsAsPrinted: "approx. 25 seeds",
  seedCount: 25,
  price: 2.99,
  priceAsPrinted: "£2.99",
  unsure: ["lotOrReference"],
  plantMatches: [
    {
      id: plant,
      commonName: "Sweet Pea",
      variety: "Cupani",
      botanicalName: "Lathyrus odoratus",
      hasPhoto: false,
    },
  ],
  supplierId: supplier,
  cost: 0.0004,
  spentThisMonth: 0.1,
  spentInAll: 0.1,
};

// Reading a seed packet into New batch against a pretend server: the photo is read, shown to check, and filled in
// only when asked, and nothing is saved until the batch is started.
async function pretend(
  page: Page,
  sent: Sent[],
  reply: () => { status: number; json: unknown },
  readsImages = true,
) {
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (method !== "GET") {
      sent.push({
        url: `${path}${url.search}`,
        method,
        // Photos go up as images, everything else as JSON.
        body: (request.headers()["content-type"] ?? "").includes("json")
          ? request.postDataJSON()
          : null,
      });
    }

    if (path === "/api/assistant/read-label") {
      await route.fulfill(reply());
      return;
    }

    if (path === "/api/batches" && method === "POST") {
      await route.fulfill({
        status: 201,
        json: {
          labelCode: "C000042",
          batchId: "0193a6f0-0000-7000-8000-0000000000b1",
        },
      });
      return;
    }

    if (path.startsWith("/api/batches/") && method === "POST") {
      await route.fulfill({ status: 201, json: {} });
      return;
    }

    if (path === "/api/species/search") {
      await route.fulfill({
        json: {
          plants: reading.plantMatches,
          species: [],
          onlineUnavailable: false,
        },
      });
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
        readsImages,
        spent: { thisMonth: 0.1, inAll: 0.1 },
      },
      "/api/suppliers": [
        {
          id: supplier,
          name: "Chiltern Seeds",
          passportRegistrationNumber: "GB 12345",
          otherRegistrations: null,
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, path) ? data[path] : [],
    });
  });
}

async function startNewBatch(page: Page) {
  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Phone" })
    .getByRole("button", { name: "New batch" })
    .click();
}

test("a seed packet photo fills in New batch, to check before starting", async ({
  page,
}) => {
  const sent: Sent[] = [];
  await pretend(page, sent, () => ({ status: 200, json: reading }));
  await startNewBatch(page);

  await expect(
    page.getByText(
      "Your photo goes to Ask Hortiplanner, which reads the packet and fills in what it can, for you to check. Nothing from your records is sent with it.",
    ),
  ).toBeVisible();
  await page.locator(".read-label input[type=file]").setInputFiles({
    name: "packet.png",
    mimeType: "image/png",
    buffer: png,
  });

  const card = page.locator(".read-label-card");
  await expect(
    card.getByRole("heading", { name: "What the packet says" }),
  ).toBeVisible();
  await expect(card).toContainText("Sweet Pea, Lathyrus odoratus");
  await expect(card).toContainText(
    "This wasn't clear in the photo. Please check it carefully.",
  );
  await expect(card).toContainText("Reading this photo cost under 1 cent.");
  await expect(
    page.getByRole("checkbox", { name: "Keep this photo with the batch." }),
  ).toBeChecked();
  await expectAccessible(page, "New batch, a seed packet read");
  expect(sent.filter((item) => item.url === "/api/batches")).toHaveLength(0);
  expect(
    (
      sent.find((item) => item.url === "/api/assistant/read-label")!.body as {
        kind: string;
        image: string;
      }
    ).image,
  ).toMatch(/^data:image\//);

  await page.getByRole("button", { name: "Use these details" }).click();
  await expect(
    page.getByText(
      "The packet says this is Sweet Pea. Choose the matching plant below.",
    ),
  ).toBeVisible();
  await page.locator(".plant-card", { hasText: "Sweet Pea 'Cupani'" }).click();

  // How many: what the packet says it holds is shown.
  await expect(
    page.getByText("The packet says it holds approx. 25 seeds."),
  ).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();

  // Where from: filled in and marked, until changed.
  await expect(page.getByRole("combobox", { name: /^Supplier/ })).toHaveValue(
    supplier,
  );
  await expect(page.getByLabel(/Lot or reference/)).toHaveValue("L2291");
  await expect(page.locator(".read-tag")).toHaveCount(4);
  await expectAccessible(page, "New batch, where from, read from a packet");
  await page.getByLabel(/Lot or reference/).fill("L2292");
  await expect(page.locator(".read-tag")).toHaveCount(3);

  // The packet's price is put in as the cost, and marked, until it's changed.
  const priced = page.getByLabel(/What did this seed cost\?/);
  await expect(priced).toHaveValue("2.99");
  await expect(priced).toHaveAccessibleDescription(/^The packet says £2\.99\./);

  // What the seed cost is asked, and optional. Something that isn't money is pointed out when Next is pressed.
  const costBox = page.getByLabel("What did this seed cost? (£, optional)");
  await expect(costBox).toHaveAccessibleDescription(/with any VAT included/);
  await costBox.fill("2.");
  await expect(costBox).not.toHaveAttribute("aria-invalid", "true");
  await costBox.fill("two pounds");
  await page.getByRole("button", { name: "Next" }).click();
  await expect(costBox).toHaveAttribute("aria-invalid", "true");
  await expect(costBox).toBeFocused();
  await expect(costBox).toHaveAccessibleDescription(
    /^Please put the cost in as pounds and pence, such as 2\.99\./,
  );
  await expectAccessible(page, "New batch, a cost that isn't money");

  // Nought is taken as nothing.
  await costBox.fill("0");
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByRole("button", { name: "Next" }).click();

  const check = page.locator(".batch-check");
  await expect(check).toContainText("Chiltern Seeds");
  await expect(check).toContainText("Dec 2027");
  await expect(check).toContainText("L2292");
  await expect(check).toContainText(/Seed cost\s*Nothing/);

  // Change from the check page keeps what was typed.
  await page.getByRole("button", { name: "Change seed cost" }).click();
  await expect(costBox).toHaveValue("0");
  await costBox.fill("£1.80");
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByRole("button", { name: "Next" }).click();
  await page.getByRole("button", { name: "Next" }).click();
  await expect(check).toContainText(/Seed cost\s*£1\.80/);
  await page.getByRole("button", { name: "Start batch" }).click();
  await expect(page.getByRole("status")).toContainText("Started");

  const started = sent.find((item) => item.url === "/api/batches")!
    .body as Record<string, unknown>;
  expect(started).toMatchObject({
    plantId: plant,
    supplierId: supplier,
    sourceReference: "L2292",
    sowBy: "2027-12-31",
    source: "SeedLot",
    costAmount: 1.8,
    costNothing: false,
  });
  await expect
    .poll(() => sent.some((item) => item.url.includes("kind=Packet")))
    .toBe(true);

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});

test("a photo that isn't a packet, or can't be read, says so and fills nothing in", async ({
  page,
}) => {
  let answer: { status: number; json: unknown } = {
    status: 200,
    json: { ...reading, readable: false, what: "other", plantMatches: [] },
  };
  await pretend(page, [], () => answer);
  await startNewBatch(page);
  const file = { name: "cat.png", mimeType: "image/png", buffer: png };

  await page.locator(".read-label input[type=file]").setInputFiles(file);
  await expect(
    page.getByText(
      "That doesn't look like a seed packet, so nothing was filled in.",
      {
        exact: false,
      },
    ),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Use these details" }),
  ).toHaveCount(0);
  await expectAccessible(page, "New batch, not a seed packet");

  answer = { status: 409, json: { problem: "TooMany" } };
  await page.locator(".read-label input[type=file]").setInputFiles(file);
  await expect(
    page.getByText(
      "Ask Hortiplanner has been asked a lot this hour, so it can't read another photo yet.",
      { exact: false },
    ),
  ).toBeVisible();

  await page
    .getByRole("button", { name: "Carry on without the photo" })
    .click();
  await expect(
    page.getByRole("button", { name: "Read a photo of the seed packet" }),
  ).toBeVisible();
});

test("the packet can't be read with a model that can't see photos", async ({
  page,
}) => {
  await pretend(page, [], () => ({ status: 200, json: reading }), false);
  await startNewBatch(page);
  await expect(page.getByLabel("What are you planting?")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Read a photo of the seed packet" }),
  ).toHaveCount(0);
});

test("reading a second packet leaves nothing from the first behind", async ({
  page,
}) => {
  let answer = reading;
  await pretend(page, [], () => ({ status: 200, json: answer }));
  await startNewBatch(page);
  const file = { name: "packet.png", mimeType: "image/png", buffer: png };

  await page.locator(".read-label input[type=file]").setInputFiles(file);
  await page.getByRole("button", { name: "Use these details" }).click();

  answer = {
    ...reading,
    lotOrReference: null,
    sowBy: null,
    supplierId: null,
    supplierName: null,
    supplierPassportNumber: null,
    price: null,
    priceAsPrinted: null,
    unsure: [],
  };
  await page.locator(".read-label input[type=file]").setInputFiles(file);
  await page.getByRole("button", { name: "Use these details" }).click();
  await page.locator(".plant-card", { hasText: "Sweet Pea 'Cupani'" }).click();
  await page.getByRole("button", { name: "Next" }).click();

  await expect(page.getByLabel(/Lot or reference/)).toHaveValue("");
  await expect(page.getByRole("combobox", { name: /^Supplier/ })).toHaveValue(
    "",
  );
  await expect(page.getByLabel("Month")).toHaveValue("");
  await expect(page.getByLabel(/What did this seed cost\?/)).toHaveValue("");
  await expect(page.locator(".read-tag")).toHaveCount(0);
});

test("the cost question follows where the plants came from", async ({
  page,
}) => {
  await pretend(page, [], () => ({ status: 200, json: reading }));
  await startNewBatch(page);
  await page.getByLabel("What are you planting?").fill("Sweet");
  await page.locator(".plant-card", { hasText: "Sweet Pea 'Cupani'" }).click();
  await page.getByRole("button", { name: "Next" }).click();

  // One bought-in plant is "it".
  await page.getByRole("button", { name: "Bought in", exact: true }).click();
  const plantBox = page.getByLabel("What did this plant cost? (£, optional)");
  await expect(plantBox).toHaveAccessibleDescription(
    "Put in what was paid for it, with any VAT included. You can leave this empty and add it later.",
  );
  await plantBox.fill("3");

  // A cost given for plants isn't carried over to seed.
  await page.getByRole("button", { name: "Seed", exact: true }).click();
  await expect(
    page.getByLabel("What did this seed cost? (£, optional)"),
  ).toHaveValue("");

  // Cuttings taken here cost nothing to buy, so nothing is asked.
  await page.getByRole("button", { name: "Cuttings", exact: true }).click();
  await expect(page.getByLabel(/cost\? \(£, optional\)/)).toHaveCount(0);
});

test("half a packet puts in half its price, and part of one asks for a rough part", async ({
  page,
}) => {
  await pretend(page, [], () => ({ status: 200, json: reading }));
  await startNewBatch(page);
  await page
    .locator(".read-label input[type=file]")
    .setInputFiles({ name: "packet.png", mimeType: "image/png", buffer: png });
  await expect(page.getByText("Price")).toBeVisible();
  await page.getByRole("button", { name: "Use these details" }).click();
  await page.locator(".plant-card", { hasText: "Sweet Pea 'Cupani'" }).click();

  await page.getByRole("button", { name: "A packet", exact: true }).click();
  await page.getByRole("button", { name: "Half", exact: true }).click();
  await page.getByRole("button", { name: "Next" }).click();
  const box = page.getByLabel(/What did this seed cost\?/);
  await expect(box).toHaveValue("1.50");
  await expect(box).toHaveAccessibleDescription(
    /^The packet says £2\.99 and half of it went in, so £1\.50 has been put in\. Change it if that isn't right\./,
  );

  await page.getByRole("button", { name: "Back", exact: true }).click();
  await page.getByRole("button", { name: "Part", exact: true }).click();
  await page.getByRole("button", { name: "Next" }).click();
  await expect(box).toHaveValue("");
  await expect(box).toHaveAccessibleDescription(
    /^The packet says £2\.99 and part of it went in, so put in roughly what that part cost\./,
  );

  // Typed over, it's no longer from the photo.
  await box.fill("0.75");
  await expect(page.getByText(/^The packet says/)).toHaveCount(0);
});
