import { describe, expect, it } from "vitest";
import { answerBlocks, clearTemporaryAskState } from "./assistant.svelte";

describe("answerBlocks", () => {
  it("turns an answer into paragraphs and lists, never markup", () => {
    expect(
      answerBlocks(
        "## Check these\n\n- **C000012** looks dry\n- <img src=x onerror=alert(1)>\n\nWater in the morning.",
      ),
    ).toEqual([
      { kind: "p", text: "Check these" },
      {
        kind: "list",
        items: ["C000012 looks dry", "<img src=x onerror=alert(1)>"],
      },
      { kind: "p", text: "Water in the morning." },
    ]);
  });
});

describe("clearTemporaryAskState", () => {
  it("removes temporary Ask keys without touching unrelated session storage", () => {
    const map = new Map<string, string>([
      ["hortiplanner.ask.user1", "[]"],
      ["hortiplanner.ask.open.user1", "conv-1"],
      ["hortiplanner.other", "keep"],
    ]);
    const store = {
      get length() {
        return map.size;
      },
      key: (index: number) => [...map.keys()][index] ?? null,
      removeItem: (key: string) => void map.delete(key),
    };

    clearTemporaryAskState(store);

    expect([...map.keys()]).toEqual(["hortiplanner.other"]);
  });
});
