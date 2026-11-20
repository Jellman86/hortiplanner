import { describe, expect, it } from "vitest";
import {
  announcement,
  badgeText,
  bellName,
  forRecordWork,
  withRefused,
  type Alert,
} from "./needs";

const alert = (kind: string, title = kind): Alert => ({
  kind,
  title,
  why: "Because.",
  where: "production",
  count: 1,
  level: "Tidy",
});

describe("the bell", () => {
  it("says how many things there are in its name", () => {
    expect(bellName(0)).toBe("Needs a look, nothing just now");
    expect(bellName(1)).toBe("Needs a look, 1 thing");
    expect(bellName(12)).toBe("Needs a look, 12 things");
  });

  it("shows no number at nought, and 9+ above nine", () => {
    expect(badgeText(0)).toBe("");
    expect(badgeText(3)).toBe("3");
    expect(badgeText(9)).toBe("9");
    expect(badgeText(10)).toBe("9+");
  });
});

describe("work this device couldn't save", () => {
  it("comes first, as one item saying how many", () => {
    const list = withRefused([alert("JobsDue")], 2);
    expect(list.map((item) => item.kind)).toEqual(["Refused", "JobsDue"]);
    expect(list[0].title).toBe("2 things you recorded couldn't be saved");
    expect(list[0].level).toBe("Problem");
    expect(list[0].where).toBe("record");
  });

  it("isn't there when everything saved", () => {
    expect(withRefused([alert("JobsDue")], 0)).toHaveLength(1);
    expect(withRefused([], 1)[0].title).toBe(
      "1 thing you recorded couldn't be saved",
    );
  });
});

describe("Record work", () => {
  it("leaves out jobs due, which it lists to tick off", () => {
    expect(
      forRecordWork([alert("JobsDue"), alert("NotCountedLong")]).map(
        (item) => item.kind,
      ),
    ).toEqual(["NotCountedLong"]);
  });
});

describe("what is said when the list changes", () => {
  it("says nothing on first loading", () => {
    expect(announcement(null, [alert("JobsDue")])).toBeNull();
  });

  it("names a single new thing", () => {
    expect(
      announcement([], [alert("InvoicesOverdue", "1 invoice is overdue")]),
    ).toBe("Something new needs a look: 1 invoice is overdue.");
  });

  it("counts several new things", () => {
    expect(announcement([], [alert("A"), alert("B")])).toBe(
      "2 more things need a look.",
    );
  });

  it("says nothing when the list stays the same or shrinks", () => {
    expect(announcement([alert("A")], [alert("A")])).toBeNull();
    expect(announcement([alert("A"), alert("B")], [alert("A")])).toBeNull();
  });
});
