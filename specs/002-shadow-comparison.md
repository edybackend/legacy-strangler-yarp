# 002 · Shadow comparison

## Context
Before sending real users to the modern service we want evidence that it answers like the legacy one. Shadow traffic gives that evidence with production requests and no risk to the client.

## Behavior
- In `Shadow` mode the gateway keeps a copy of the legacy response body while streaming it to the client.
- After the response, the same GET is sent to the modern service in the background (bounded queue; when full, comparisons are dropped, never requests).
- Status codes are compared; for 2xx the JSON bodies are compared by meaning: property order is ignored and numbers are compared by value.
- `GET /migration/api/mismatches` lists the latest mismatches with each differing JSON path and both values.

## Acceptance criteria
- The client's response and latency do not depend on the modern service (it can be down).
- Known difference: the legacy service computes order totals with `double` and truncates; the modern one uses `decimal` and rounds half away from zero. Orders whose total ends in a fraction of a cent show up as mismatches on `$.total`; the others match.
- A failure calling the modern service is counted as a shadow error, not as a mismatch.

## Out of scope
Shadowing writes, replaying captured traffic later.
