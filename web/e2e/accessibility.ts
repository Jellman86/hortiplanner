import { appendFileSync, mkdirSync } from "node:fs";
import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

// The WCAG 2.2 AA rules, checked on a screen as it stands, in light and then in dark.
const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

// A11Y_REPORT=1 writes every problem to test-results/accessibility.jsonl and carries on, so one run lists them all.
const reportOnly = !!process.env.A11Y_REPORT;

export async function expectAccessible(page: Page, screen: string) {
  const before = await page.evaluate(
    () => document.documentElement.dataset.theme ?? null,
  );
  const viewport = page.viewportSize();
  // Colours mustn't be caught halfway through changing theme.
  await page.evaluate(() => {
    const still = document.createElement("style");
    still.id = "a11y-still";
    still.textContent =
      "*, *::before, *::after { transition: none !important; animation: none !important; }";
    document.head.append(still);
  });
  const found: {
    width: number;
    theme: string;
    id: string;
    impact: string | null | undefined;
    help: string;
    targets: string[];
  }[] = [];

  // On a phone and on a desk, whichever the screen is on now, then back.
  for (const size of [
    viewport!,
    viewport!.width < 700
      ? { width: 1280, height: 800 }
      : { width: 390, height: 900 },
  ]) {
    await page.setViewportSize(size);
    await page.evaluate(() => new Promise(requestAnimationFrame));
    for (const theme of ["light", "dark"]) {
      await page.evaluate(
        (t) => (document.documentElement.dataset.theme = t),
        theme,
      );
      const results = await new AxeBuilder({ page }).withTags(tags).analyze();
      for (const v of results.violations) {
        found.push({
          width: size.width,
          theme,
          id: v.id,
          impact: v.impact,
          help: v.help,
          targets: v.nodes.map((n) => n.target.join(" ")),
        });
      }
    }
  }
  await page.setViewportSize(viewport!);

  await page.evaluate((t) => {
    if (t === null) delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = t;
  }, before);
  // Only once the theme is back, so nothing is left changing colour.
  // Reading the layout makes the browser settle every colour now, before transitions are allowed again.
  await page.evaluate(() => {
    void document.documentElement.offsetHeight;
    document.getElementById("a11y-still")?.remove();
  });

  if (reportOnly) {
    mkdirSync("test-results", { recursive: true });
    for (const f of found) {
      appendFileSync(
        "test-results/accessibility.jsonl",
        JSON.stringify({ screen, ...f }) + "\n",
      );
    }
    return;
  }

  expect(
    found.map(
      (f) =>
        `${f.width}px ${f.theme} ${f.id}: ${f.help} (${f.targets.join(", ")})`,
    ),
    `accessibility problems on ${screen}`,
  ).toEqual([]);
}
