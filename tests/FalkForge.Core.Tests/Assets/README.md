# Vendored test assets

## `spdx-2.3-schema.json`

The official SPDX 2.3 JSON schema, vendored **verbatim** — do not hand-edit it. It is the input to
`Sbom/SpdxSchemaConformanceTests.cs`, which validates what `SpdxSbomGenerator` emits against the
spec's own machine-readable definition instead of against a human reading clauses.

| | |
|---|---|
| Source | <https://github.com/spdx/spdx-spec> |
| Path | `schemas/spdx-schema.json` |
| Branch | `support/2.3` |
| Pinned commit | `415b8fea3ed743bbddec58081c6505e5e29e44f0` |
| Raw URL | <https://raw.githubusercontent.com/spdx/spdx-spec/415b8fea3ed743bbddec58081c6505e5e29e44f0/schemas/spdx-schema.json> |
| Size | 45,305 bytes |
| SHA-256 | `ca7fd7cc2c8107c3b6b5976058bb72363e8c072f0e446609d4fe7234860c2894` |
| Schema dialect | JSON Schema draft-07 |
| Licence | SPDX specification content, CC-BY-3.0 (Linux Foundation / SPDX workgroup) |

Pinned to an immutable commit rather than the branch tip so the check cannot silently change meaning
under a spec revision — and so the bytes in this repo are verifiable against a fixed upstream object
rather than "whatever `support/2.3` said the day someone looked".

The file declares no external `$ref`, so validation is fully offline; nothing in the test suite
reaches the network.

### Refreshing it

Re-fetch from a new pinned commit and update every row above, including the SHA-256:

```sh
curl -o spdx-2.3-schema.json \
  https://raw.githubusercontent.com/spdx/spdx-spec/<commit>/schemas/spdx-schema.json
sha256sum spdx-2.3-schema.json
```

A refresh that makes the conformance test fail is a real finding about the emitted document, not a
reason to edit the schema.
