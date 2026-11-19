import type { Schemas } from "./types";

export type PlantPhotoKind = Schemas["PlantPhotoKind"];

/** What a photo of a plant can show, in the order they're offered. */
export const plantPhotoKinds: { kind: PlantPhotoKind; label: string }[] = [
  { kind: "Flower", label: "Flower" },
  { kind: "Foliage", label: "Foliage" },
  { kind: "Habit", label: "Whole plant" },
  { kind: "Label", label: "Label" },
  { kind: "Other", label: "Something else" },
];

/** The words for a photo's kind, as shown under it. "Something else" reads better as "Photo" there. */
export function plantPhotoKindLabel(kind: PlantPhotoKind): string {
  return kind === "Other"
    ? "Photo"
    : (plantPhotoKinds.find((item) => item.kind === kind)?.label ?? "Photo");
}

/** What a screen reader says for a photo: its caption, or its kind if it has none. */
export function plantPhotoAlt(
  photo: { kind: PlantPhotoKind; caption?: string | null },
  plant: string,
): string {
  const what = photo.caption?.trim();
  return what
    ? `${plant}: ${what}`
    : `${plantPhotoKindLabel(photo.kind)} of ${plant}`;
}

/** Why a photo couldn't be saved, from the server's answer. */
export function plantPhotoProblem(status: number): string {
  switch (status) {
    case 409:
      return "This plant already has 30 photos, which is the most it can have. Remove one you no longer need before adding another.";
    case 413:
      return "That photo is too big to save. Please try a smaller one.";
    case 415:
      return "That couldn't be saved because it isn't a photo. Please choose a photo.";
    case 0:
      return "That photo couldn't be saved. Please check the connection and try again.";
    default:
      return "That photo couldn't be saved. Please try again.";
  }
}
