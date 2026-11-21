import ChartColumn from "@lucide/svelte/icons/chart-column";
import ClipboardList from "@lucide/svelte/icons/clipboard-list";
import Contact from "@lucide/svelte/icons/contact";
import FileText from "@lucide/svelte/icons/file-text";
import Flower2 from "@lucide/svelte/icons/flower-2";
import HandCoins from "@lucide/svelte/icons/hand-coins";
import MapPin from "@lucide/svelte/icons/map-pin";
import Package from "@lucide/svelte/icons/package";
import PoundSterling from "@lucide/svelte/icons/pound-sterling";
import ReceiptText from "@lucide/svelte/icons/receipt-text";
import RouteIcon from "@lucide/svelte/icons/route";
import ScanLine from "@lucide/svelte/icons/scan-line";
import Scissors from "@lucide/svelte/icons/scissors";
import ShoppingCart from "@lucide/svelte/icons/shopping-cart";
import Sprout from "@lucide/svelte/icons/sprout";
import Thermometer from "@lucide/svelte/icons/thermometer";
import Truck from "@lucide/svelte/icons/truck";
import Wallet from "@lucide/svelte/icons/wallet";
import { anyOn, type Feature } from "./features.svelte";

// The places in Hortiplanner and the pages in each. On a desk each place is one button in the sidebar and opens on its
// first page, with a row of buttons under the heading for the others; on a phone, More lists the places that aren't in
// the bar along the bottom. Setting the nursery up lives under Settings (settings.ts).

/** The owner's pages, opened as `{ name: "manage", section }`; the name is kept so remembered screens still open. */
export type ManageSection =
  | "plants"
  | "places"
  | "trace"
  | "jobs"
  | "mothers"
  | "climate"
  | "figures"
  | "costs"
  | "money"
  | "invoices"
  | "consignments"
  | "customers"
  | "purchases"
  | "suppliers";

/** A page in a place: Production has its own screen, and the rest are the owner's pages. */
export type HubPage = "production" | ManageSection;

export type HubName =
  "home" | "production" | "plants" | "places" | "selling" | "buying" | "money";

export type HubItem = {
  page: HubPage;
  label: string;
  icon: typeof Flower2;
  /** Shown while any of these parts of Hortiplanner is on; always shown without. */
  features?: Feature[];
  ownerOnly?: true;
  /** False for a page reached from elsewhere, such as the bell, and listed in no sidebar, row or More. */
  listed?: false;
  /** The page it's reached from, for its breadcrumb and Back. */
  parent?: ManageSection;
};

export type Hub = {
  hub: HubName;
  label: string;
  icon: typeof Flower2;
  /** In the phone's bar along the bottom, so More doesn't list it. */
  inPhoneBar: boolean;
  items: HubItem[];
};

export const hubs: Hub[] = [
  {
    hub: "home",
    label: "Record work",
    icon: ScanLine,
    inPhoneBar: true,
    items: [
      {
        page: "jobs",
        label: "Jobs",
        icon: ClipboardList,
        features: ["Jobs"],
        ownerOnly: true,
        // Reached from the jobs due on Record work, and the bell when one is late.
        listed: false,
      },
    ],
  },
  {
    hub: "production",
    label: "Production",
    icon: Sprout,
    inPhoneBar: true,
    items: [
      { page: "production", label: "Production", icon: Sprout },
      {
        page: "trace",
        label: "Trace a batch",
        icon: RouteIcon,
        ownerOnly: true,
      },
    ],
  },
  {
    hub: "plants",
    label: "Plants",
    icon: Flower2,
    inPhoneBar: false,
    items: [
      { page: "plants", label: "Plants", icon: Flower2 },
      {
        page: "mothers",
        label: "Mother plants",
        icon: Scissors,
        features: ["MotherPlants"],
        ownerOnly: true,
      },
    ],
  },
  {
    hub: "places",
    label: "Places",
    icon: MapPin,
    inPhoneBar: false,
    items: [
      { page: "places", label: "Places", icon: MapPin, ownerOnly: true },
      {
        page: "climate",
        label: "Climate",
        icon: Thermometer,
        features: ["Climate"],
        ownerOnly: true,
      },
    ],
  },
  {
    hub: "selling",
    label: "Selling",
    icon: HandCoins,
    inPhoneBar: false,
    items: [
      {
        page: "invoices",
        label: "Invoices",
        icon: FileText,
        features: ["Invoices"],
        ownerOnly: true,
      },
      {
        page: "customers",
        label: "Customers",
        icon: Contact,
        features: ["Invoices", "Passports"],
        ownerOnly: true,
      },
      {
        page: "consignments",
        label: "Consignments",
        icon: Package,
        features: ["Passports"],
        ownerOnly: true,
      },
    ],
  },
  {
    hub: "buying",
    label: "Buying",
    icon: ShoppingCart,
    inPhoneBar: false,
    items: [
      {
        page: "purchases",
        label: "Purchases",
        icon: ReceiptText,
        features: ["Purchases"],
        ownerOnly: true,
      },
      { page: "suppliers", label: "Suppliers", icon: Truck, ownerOnly: true },
    ],
  },
  {
    hub: "money",
    label: "Money and figures",
    icon: Wallet,
    inPhoneBar: false,
    items: [
      { page: "money", label: "Money", icon: Wallet, ownerOnly: true },
      { page: "figures", label: "Figures", icon: ChartColumn, ownerOnly: true },
      {
        page: "costs",
        label: "Costs to fill in",
        icon: PoundSterling,
        features: ["Costs"],
        ownerOnly: true,
        // Reached from the bell and from Figures.
        listed: false,
        parent: "figures",
      },
    ],
  },
];

const items = hubs.flatMap((hub) => hub.items);

/** Whether this person may see a page and its part of Hortiplanner is on. */
const shown = (item: HubItem, isOwner: boolean) =>
  (isOwner || !item.ownerOnly) && (!item.features || anyOn(...item.features));

/** The pages of a place this person is shown in its row and in More, in order. */
export const listedPages = (hub: Hub, isOwner: boolean) =>
  hub.items.filter((item) => item.listed !== false && shown(item, isOwner));

/** The places in the sidebar, after Record work: each with at least one page this person is shown. */
export const visibleHubs = (isOwner: boolean) =>
  hubs.filter(
    (hub) => hub.hub !== "home" && listedPages(hub, isOwner).length > 0,
  );

/** The place a page belongs to. */
export const hubOf = (page: HubPage): Hub =>
  hubs.find((hub) => hub.items.some((item) => item.page === page)) ?? hubs[0];

/** The page a place opens on: its first one shown. */
export const firstPage = (hub: Hub, isOwner: boolean): HubPage =>
  listedPages(hub, isOwner)[0]?.page ?? hub.items[0].page;

/** A page's own entry, for its name and where it's reached from. */
export const pageItem = (page: HubPage): HubItem | undefined =>
  items.find((item) => item.page === page);

/** Whether a section can be opened, so a remembered or linked one that's been turned off isn't. */
export const isManageSectionOn = (section: ManageSection) =>
  items.some((item) => item.page === section && shown(item, true));

export const isManageSection = (value: unknown): value is ManageSection =>
  value !== "production" && items.some((item) => item.page === value);

/** A section's name, for its breadcrumb and the browser tab. */
export const sectionLabel = (section: ManageSection) =>
  pageItem(section)?.label ?? "More";
