// Lets the app open with no signal. Recording is handled by the work queue, so the API is never cached here.
// Each release is staged in its own cache and only promoted once the HTML and every referenced asset are stored,
// so an interrupted update never leaves the offline shell pointing at missing hashed files.
const metaCacheName = "hortiplanner-meta";
const activeReleaseUrl = "/__hortiplanner_active_release__";
const releasePrefix = "hortiplanner-rel-";
const stagingPrefix = "hortiplanner-stage-";

function extractAssets(html) {
  const assets = new Set();
  for (const match of html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)) {
    assets.add(match[1]);
  }
  return [...assets].sort();
}

function releaseNameFor(assets) {
  let hash = 2166136261;
  const key = assets.join("|");
  for (let i = 0; i < key.length; i += 1) {
    hash ^= key.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return `${releasePrefix}${(hash >>> 0).toString(16)}`;
}

async function activeReleaseName() {
  const meta = await caches.open(metaCacheName);
  const stored = await meta.match(activeReleaseUrl);
  if (stored) {
    const name = (await stored.text()).trim();
    if (name && (await caches.has(name))) {
      return name;
    }
  }

  const names = await caches.keys();
  return names.find((name) => name.startsWith(releasePrefix)) ?? null;
}

async function pruneOldCaches(keep) {
  const names = await caches.keys();
  await Promise.all(
    names
      .filter(
        (name) =>
          name !== metaCacheName &&
          name !== keep &&
          !name.startsWith(stagingPrefix),
      )
      .map((name) => caches.delete(name)),
  );
}

/** Stages a complete HTML + assets release atomically before promoting it as active. */
async function stageRelease(htmlResponse) {
  const html = await htmlResponse.clone().text();
  const assets = extractAssets(html);
  const targetName = releaseNameFor(assets);

  if (await caches.has(targetName)) {
    const meta = await caches.open(metaCacheName);
    await meta.put(activeReleaseUrl, new Response(targetName));
    return targetName;
  }

  const stageName = `${stagingPrefix}${targetName.slice(releasePrefix.length)}-${Date.now()}`;
  const stage = await caches.open(stageName);

  try {
    await stage.put("/index.html", htmlResponse.clone());
    await Promise.all(
      assets.map(async (asset) => {
        const res = await fetch(asset, { cache: "no-store" });
        if (!res.ok) {
          throw new Error(`Failed to cache ${asset}: ${res.status}`);
        }
        await stage.put(asset, res);
      }),
    );

    const target = await caches.open(targetName);
    const keys = await stage.keys();
    await Promise.all(
      keys.map(async (req) => {
        const res = await stage.match(req);
        if (res) {
          await target.put(req, res);
        }
      }),
    );

    const meta = await caches.open(metaCacheName);
    await meta.put(activeReleaseUrl, new Response(targetName));
    await pruneOldCaches(targetName);
    return targetName;
  } finally {
    await caches.delete(stageName);
  }
}

async function precache() {
  const response = await fetch("/index.html", { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Failed to fetch /index.html: ${response.status}`);
  }
  await stageRelease(response);
}

async function openPage(request) {
  try {
    const response = await fetch(request);

    if (response.ok) {
      // Stage in the background; only promotes once every referenced asset is cached.
      void stageRelease(response.clone()).catch(() => {});
    }

    return response;
  } catch {
    const active = await activeReleaseName();
    if (active) {
      const cache = await caches.open(active);
      const cached = await cache.match("/index.html");
      if (cached) {
        return cached;
      }
    }
    return caches.match("/index.html");
  }
}

async function fromCacheFirst(request) {
  const active = await activeReleaseName();
  if (active) {
    const cache = await caches.open(active);
    const cached = await cache.match(request);
    if (cached) {
      return cached;
    }
  }

  const response = await fetch(request);

  if (response.ok && active) {
    const cache = await caches.open(active);
    await cache.put(request, response.clone());
  }

  return response;
}

self.addEventListener("install", (event) => {
  event.waitUntil(precache().then(() => self.skipWaiting()));
});

async function cleanOnActivate() {
  const active = await activeReleaseName();
  const names = await caches.keys();
  await Promise.all(
    names
      .filter((name) => name !== metaCacheName && name !== active)
      .map((name) => caches.delete(name)),
  );
}

self.addEventListener("activate", (event) => {
  event.waitUntil(cleanOnActivate().then(() => self.clients.claim()));
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  if (
    event.request.method !== "GET" ||
    url.origin !== self.location.origin ||
    url.pathname.startsWith("/api/") ||
    url.pathname === "/signin-oidc"
  ) {
    return;
  }

  event.respondWith(
    event.request.mode === "navigate"
      ? openPage(event.request)
      : fromCacheFirst(event.request),
  );
});
