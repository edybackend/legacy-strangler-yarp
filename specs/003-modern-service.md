# 003 · Modern service

## Context
The modern service re-implements the read side first, with the same public contract.

## Behavior
- `GET /api/customers`, `/api/customers/{id}`, `/api/orders?customerId=`, `/api/orders/{id}`: same URLs and JSON as the legacy service.
- Order totals: `decimal`, each line rounded to the cent (half away from zero), then summed.
- During the migration it reads the legacy database **read-only** (SQLite `Mode=ReadOnly`); writes stay in the legacy service.

## Acceptance criteria
- Customers: identical answers to the legacy service for every customer (parity test).
- Orders: identical except `total` on the orders affected by the legacy rounding bug (parity test lists them).
- An order created through the gateway (legacy write) is visible through the modern read.

## Out of scope
Writes (orders stay on the legacy service), a separate modern database and the data sync it would need.
