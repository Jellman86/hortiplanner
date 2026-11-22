/** One column of a table: what it sorts by, and what it shows if that differs. */
export interface Column<T> {
  label: string;
  value: (row: T) => string | number | null | undefined;
  text?: (row: T) => string;
  numeric?: boolean;
  /** Shown in bold, as the figure that counts beside one there to compare. */
  strong?: boolean;
  /** Read out after the heading, such as "the figures that count", for what the bold says by sight. */
  hint?: string;
  /** Shown as a pill badge, such as a tray's growing stage. */
  pill?: boolean;
  /** An optional thumbnail URL shown beside the cell's text. */
  thumb?: (row: T) => string | null;
}

export function cellText<T>(column: Column<T>, row: T): string {
  if (column.text) {
    return column.text(row);
  }

  const value = column.value(row);
  return value === null || value === undefined ? "" : String(value);
}

/** Sorted by one column; empty cells always go last, whichever way round. */
export function sortRows<T>(
  rows: T[],
  column: Column<T> | null,
  ascending: boolean,
): T[] {
  if (!column) {
    return rows;
  }

  const direction = ascending ? 1 : -1;

  return [...rows].sort((a, b) => {
    const first = column.value(a);
    const second = column.value(b);

    if (first === null || first === undefined || first === "") {
      return second === null || second === undefined || second === "" ? 0 : 1;
    }

    if (second === null || second === undefined || second === "") {
      return -1;
    }

    return (
      direction *
      (typeof first === "number" && typeof second === "number"
        ? first - second
        : String(first).localeCompare(String(second), "en-GB", {
            numeric: true,
          }))
    );
  });
}
