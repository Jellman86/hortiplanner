import { describe, expect, it } from "vitest";
import {
  plantPhotoAlt,
  plantPhotoKindLabel,
  plantPhotoProblem,
} from "./plantPhotos";

describe("plant photos", () => {
  it("names each kind as it's shown under a photo", () => {
    expect(plantPhotoKindLabel("Habit")).toBe("Whole plant");
    expect(plantPhotoKindLabel("Other")).toBe("Photo");
  });

  it("describes a photo by its caption, or else its kind", () => {
    expect(
      plantPhotoAlt({ kind: "Flower", caption: "First flowers" }, "Foxglove"),
    ).toBe("Foxglove: First flowers");
    expect(plantPhotoAlt({ kind: "Foliage", caption: " " }, "Foxglove")).toBe(
      "Foliage of Foxglove",
    );
  });

  it("explains why a photo wasn't saved", () => {
    expect(plantPhotoProblem(413)).toContain("too big");
    expect(plantPhotoProblem(0)).toContain("check the connection");
    expect(plantPhotoProblem(500)).toBe(
      "That photo couldn't be saved. Please try again.",
    );
  });
});
