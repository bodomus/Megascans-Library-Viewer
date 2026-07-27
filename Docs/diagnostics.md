# About / Diagnostics

ScanVault exposes runtime and local-index state from the **About / Diagnostics** toolbar button. Opening the window performs a fresh read-only index inspection and shows application/build identity, runtime and operating-system information, the configured library, index and cache paths, schema and normalization versions, indexed count, last successful scan metadata, and the current compatibility state.

The report deliberately does not enumerate environment variables, installed software, machine inventory, credentials, or tokens. Optional values use the single `Unavailable` marker. **Copy diagnostics** copies the same fields in a stable order; clipboard failure leaves the information visible and reports a concise error.

A log directory is not displayed because ScanVault currently uses structured debug logging without a file-log provider. The settings file path is shown because it is already an application-owned path.

## Compatibility states

| State | Read catalog | Rescan/write | Meaning and action |
| --- | --- | --- | --- |
| `Compatible` | Yes | Yes | Schema and normalized metadata are current. |
| `RequiresMigration` | Not yet | Migration only | A supported older structural schema was found. ScanVault performs the known transactional migration, then inspects again. |
| `RequiresRescan` | Yes | Yes | Rows remain readable, but normalization is older. Run an explicit Rescan to make metadata authoritative. |
| `NewerVersionUnsupported` | No | No | The database belongs to a newer format. Open it with a compatible ScanVault version; this application preserves it byte-for-byte. |
| `Missing` | Empty catalog | Yes | Normal first-run state. Startup does not create a database; the first explicit Rescan creates it. |
| `Corrupted` | No | No | Validation or reading failed. ScanVault preserves the database and logs technical detail. |

Inspection uses a private, read-only SQLite connection with `query_only` enabled and validates integrity, required tables, the single schema marker, structural schema version, and metadata-normalization version before any writable connection is opened. The write path inspects again immediately before replacement.

## Recovery guidance

For a newer unsupported index, do not attempt a downgrade: close ScanVault and use an application version that supports that database.

For a corrupted index, close ScanVault and first make a backup copy of the database shown in Diagnostics. Restore a known-good copy if available. To deliberately build a replacement, move the damaged file aside manually and run Rescan; ScanVault does not delete, rename, overwrite, or automatically recover it.

A cancelled or failed scan retains the previous committed index. Successful scan metadata is committed in the same SQLite transaction as the replacement catalog and remains available after restart. Older schema-v2 `scan_state` JSON remains readable through a legacy fallback.