# 001 · Gateway routes and migration modes

## Context
The shop runs on a legacy service. A modern service is being written next to it. Clients must not notice the migration: they keep calling the same URLs, and a YARP gateway decides, per route, which service answers.

## Behavior
- Proxied routes (YARP config): `customers` (GET `/api/customers/**`), `orders-read` (GET `/api/orders/**`), `orders-write` (POST `/api/orders/**`).
- Each route has a mode, changed at runtime with `PUT /migration/api/routes/{routeId}`:
  - `Legacy`: every request goes to the legacy service;
  - `Shadow`: the client gets the legacy answer, and the same request is replayed against the modern service and compared (see 002);
  - `Canary` with `canaryPercent`: that share of requests goes to the modern service;
  - `Modern`: every request goes to the modern service.
- Every proxied response carries `X-Served-By: legacy|modern`.
- With an `X-Canary-Key` header (e.g. a customer id) a canary decision is stable for that key.
- `GET /migration/api/routes` shows modes and counters; `/` is a dashboard of the same data.

## Acceptance criteria
- A write route can never be `Shadow` (`409`): it would execute twice.
- A route the modern service does not implement (`ModernReady: false`) can only be `Legacy` (`409`).
- `canaryPercent` outside 0–100 → `400`. Unknown route → `404`.
- Rolling back is the same call with `Legacy`.
- Canary 0 % sends nothing to modern, 100 % everything; 30 % over many requests lands near 30 %.

## Out of scope
Persisting the plan across restarts (it starts from configuration), authentication of the admin API.
