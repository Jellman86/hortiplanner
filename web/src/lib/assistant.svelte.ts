import { api } from "../api/client";
import { isOn } from "./features.svelte";
import type { Schemas } from "./types";

// Whether this person can use Ask Hortiplanner, and whether its model reads photos, for screens to offer it.
/** What a question is asked from: a tray, a plant, a place, or a photo of a batch, a plant or a place, with words to show for it. */
export type AskAbout = {
  title: string;
  kind?: AskKind;
  trayLabel?: string | null;
  plantId?: string | null;
  placeId?: string | null;
  /** A batch's photo. */
  photoId?: string | null;
  /** One of the plant's own photos, rather than a batch's. */
  plantPhotoId?: string | null;
  /** One of a place's photos. */
  placePhotoId?: string | null;
  /** The photo asked about, shown small beside what the question is about. */
  photoUrl?: string | null;
  /** For a photo, what it's of, such as the plant or place. */
  subject?: string | null;
};
export type AskKind = "tray" | "plant" | "place" | "photo";

/** Asking about a tray, by its label code. */
export const aboutTray = (label: string): AskAbout => ({
  kind: "tray",
  title: `tray ${label}`,
  trayLabel: label,
});

/** Asking about a plant, by name. */
export const aboutPlant = (plantId: string, name: string): AskAbout => ({
  kind: "plant",
  title: name,
  plantId,
});

/** Asking about a place, by name. */
export const aboutPlace = (placeId: string, name: string): AskAbout => ({
  kind: "place",
  title: name,
  placeId,
});

/** Asking about a photo of a plant or a place, which goes with the question. */
export const aboutPhoto = (
  of: string,
  photo: Omit<AskAbout, "title" | "kind">,
): AskAbout => ({
  kind: "photo",
  title: `a photo of ${of}`,
  subject: of,
  ...photo,
});

export type AskSource = Schemas["AskSource"];

export const assistant = $state({
  canAsk: false,
  readsImages: false,
  // What this person's questions have cost the nursery, in US dollars as OpenRouter charges.
  spentThisMonth: null as number | null,
  spentInAll: null as number | null,
});

/** A cost in US dollars, as OpenRouter charges, in words a person can take in at a glance. */
export function dollars(amount: number): string {
  if (amount <= 0) {
    return "$0.00";
  }

  return amount < 0.01 ? "under 1 cent" : `$${amount.toFixed(2)}`;
}

export async function loadAssistant() {
  if (!isOn("Assistant")) {
    assistant.canAsk = false;
    return;
  }

  try {
    const { data } = await api.GET("/api/assistant/status");
    assistant.canAsk = !!data?.canAsk;
    assistant.readsImages = !!data?.readsImages;
    assistant.spentThisMonth =
      data?.spent != null ? Number(data.spent.thisMonth) : null;
    assistant.spentInAll =
      data?.spent != null ? Number(data.spent.inAll) : null;
  } catch {
    // Offline: it can't be asked anyway.
    assistant.canAsk = false;
  }
}

/** An answer as paragraphs and lists, read as plain text: nothing from the model is ever treated as markup. */
export function answerBlocks(
  text: string,
): ({ kind: "p"; text: string } | { kind: "list"; items: string[] })[] {
  const blocks: (
    { kind: "p"; text: string } | { kind: "list"; items: string[] }
  )[] = [];

  for (const chunk of text.replace(/\r/g, "").split(/\n{2,}/)) {
    const lines = chunk
      .split("\n")
      .map((line) => line.replace(/\*\*(.+?)\*\*/g, "$1").trimEnd())
      .filter((line) => line.trim().length > 0);
    const listed = lines.filter((line) => /^\s*([-*•]|\d+[.)])\s+/.test(line));

    if (lines.length > 0 && listed.length === lines.length) {
      blocks.push({
        kind: "list",
        items: lines.map((line) => line.replace(/^\s*([-*•]|\d+[.)])\s+/, "")),
      });
    } else if (lines.length > 0) {
      blocks.push({
        kind: "p",
        text: lines.join("\n").replace(/^#+\s*/gm, ""),
      });
    }
  }

  return blocks;
}
