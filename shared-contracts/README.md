# shared-contracts

Language-neutral protocol specifications shared by the watch and the companion. Each side keeps its own models; this folder is the single source of truth for message shapes.

| Path | Purpose |
|---|---|
| `protocol-v1.md` | Message catalog, fields, error codes, "no active session" representation, sequence notes, versioning policy. |
| `schemas/` | JSON Schema (draft 2020-12): `envelope`, `common` types, and one file per message type. |
| `examples/` | Valid samples, named `<message type>--<variant>.json`. |
| `examples/invalid/` | Intentionally invalid samples. Names containing `schema-only` can only be rejected by a schema (range rules); the C# parser is not expected to reject them. |

The protocol is versioned (v1). Messages carry structured data (codes and numbers), never localized text.
The C# test suite in `windows-companion/tests/Presenta.Protocol.Tests` executes the schemas against every example and against messages produced by the C# models, and checks that enums, error codes, and limits agree. The Wear OS side should validate its own models against the same files when it implements the client (Phase 5).
