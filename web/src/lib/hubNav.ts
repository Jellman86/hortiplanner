import { getContext, setContext } from "svelte";
import type { HubPage } from "./sections";

// How a page's row of buttons opens its neighbours, set once by the app's shell so pages needn't be told.
export type HubNavContext = {
  open: (page: HubPage) => void;
  isOwner: boolean;
  atBench: boolean;
};

const key = Symbol("hub-nav");

export const setHubNav = (context: HubNavContext) => setContext(key, context);

export const getHubNav = () => getContext<HubNavContext | undefined>(key);
