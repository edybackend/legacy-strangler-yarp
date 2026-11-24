// PreToolUse hook: the two rules that keep a strangler migration honest.
// 1. The legacy service is frozen: new behavior goes to the modern service, then the route is migrated.
// 2. The modern service reads the legacy database read-only until a write route is migrated with its own data.
import { readFileSync } from 'node:fs'

const input = JSON.parse(readFileSync(0, 'utf8'))
const filePath = (input.tool_input?.file_path ?? '').replaceAll('\\', '/')

if (/\/src\/Legacy\.Api\//.test(filePath)) {
  console.error(
    `Blocked edit to ${filePath}: the legacy service is frozen during the migration. ` +
      'Implement the behavior in src/Modern.Api and move the route in the gateway. For an urgent production fix, ask the developer.',
  )
  process.exit(2)
}

if (/\/src\/Modern\.Api\/.*\.cs$/.test(filePath)) {
  const written = input.tool_input?.new_string ?? input.tool_input?.content ?? ''
  if (/\b(INSERT|UPDATE|DELETE)\s|\bReadWrite\b|ReadWriteCreate/i.test(written)) {
    console.error(
      `Blocked: ${filePath} would write to the shared legacy database. The modern service reads it read-only; ` +
        'migrating a write route needs its own data store and a sync plan first (see README, "Known trade-offs").',
    )
    process.exit(2)
  }
}
