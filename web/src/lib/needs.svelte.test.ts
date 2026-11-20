import { beforeEach, describe, expect, it, vi } from "vitest";

// A pretend server whose answers can be held back, to see what a late one does.
const answers: ((data: unknown) => void)[] = [];
vi.mock("../api/client", () => ({
  api: {
    GET: () =>
      new Promise((resolve) =>
        answers.push((data) => resolve({ data, response: { ok: true } })),
      ),
  },
}));

// The tests run without a browser, so the page's events are stood in for.
const events = { addEventListener: () => {}, removeEventListener: () => {} };
vi.stubGlobal("document", { ...events, visibilityState: "visible" });
vi.stubGlobal("window", events);

const { loadNeeds, needs, watchNeeds } = await import("./needs.svelte");

const item = (kind: string) => ({
  kind,
  title: kind,
  why: "Because.",
  where: "invoices",
  count: 1,
  level: "Soon",
});

describe("the list that needs a look", () => {
  beforeEach(() => {
    answers.length = 0;
  });

  it("starts empty for each person and is emptied when they leave", async () => {
    const stop = watchNeeds();
    const loading = loadNeeds();
    answers[0]([item("InvoicesOverdue")]);
    await loading;
    expect(needs.alerts).toHaveLength(1);

    stop();
    expect(needs.alerts).toEqual([]);
    expect(needs.loaded).toBe(false);

    // The next person sees nothing of the last one's, even before their own list arrives.
    const next = watchNeeds();
    expect(needs.alerts).toEqual([]);
    next();
  });

  it("drops an answer that arrives after someone has left", async () => {
    const stop = watchNeeds();
    const loading = loadNeeds();
    stop();
    answers[0]([item("InvoicesOverdue")]);
    await loading;

    expect(needs.alerts).toEqual([]);
  });
});
