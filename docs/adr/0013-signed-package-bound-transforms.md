# 13. The elevation companion applies only signed, package-bound MSI transforms

- Status: Accepted
- Date: 2026-08-24
- Deciders: Peter Falkesand

## Context

ADR 0012 closed the elevated install to unsigned MSIs: the companion bakes its own publisher-key
set and verifies the manifest's signed integrity envelope before installing anything. The same
change also had to close a second hole. A transform (`.mst`) or a patch (`.msp`) can carry a custom
action, so accepting one from the caller on an elevated install is arbitrary code execution as
SYSTEM. ADR 0012 closed that by rejecting any caller-supplied `TRANSFORMS`/`PATCH` property
outright, and said plainly this left authors with nothing:

> Re-enabling author-declared transforms and patches on the elevated path, bound to the same
> signed-manifest mechanism this ADR adds, is planned follow-up work, not part of this change.

This ADR is that follow-up, for transforms. An author who genuinely needs a transform, say to set a
property the base MSI does not expose, had no way to get it onto an elevated per-machine install
after ADR 0012 shipped. The raw-argument reject had to stay (a caller-supplied transform is still
arbitrary code as SYSTEM), so the only way to give authors a transform back was to make it
something the companion can verify itself, the same way it now verifies the MSI.

## Decision

A bundle package can declare an MSI transform at build time. The declaration becomes a signed
payload, and the elevated companion applies it only when its bytes and its package association
both check out against the signed envelope, never against anything the caller sends over the wire.

- **`BundlePackageBuilder.Transform(string id, string mstPath)`** declares a transform for a
  package (`src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs`). Calling it more than
  once declares several transforms for the same package.
- **The transform is hashed and embedded as a signed bundle payload, keyed by its id.**
  `ManifestGenerator.BuildTransforms` reads the `.mst` file and records it as a
  `PackageTransformInfo`; `BundleIntegritySigner` folds it into the same payload-hash-entry list the
  MSI and every other package payload use, under its own id.
- **A signed package-to-transform association map goes into the integrity envelope.**
  `PackageTransformAssociation` (`src/FalkForge.Engine.Protocol/Integrity/PackageTransformAssociation.cs`)
  lists, per package id, which transform ids that package may have applied. Signing folds the whole
  map into the signed message via `IntegrityEnvelopeCodec.ComputeSignedBytes`/`CanonicalizeTransformAssociations`,
  under its own length-prefixed, separated segment, so an attacker cannot re-associate a signed
  transform onto a different package, add one, or strip one without invalidating the signature.
- **The elevated companion applies a transform only when two signed facts both hold.**
  `MsiInstallCommand.BindAndVerifyTransforms` resolves the signed SHA-256 for the transform's id from
  the verified envelope, then checks the signed association map lists that id under the package
  being installed. Either check failing refuses the whole install. Only then does it bind the
  transform's bytes to that signed hash: it opens the file, holds the `FileShare.Read` handle open
  across the hash comparison and the install, and resolves every reparse point first, the same
  `HashBoundFile` mechanism ADR 0011 built for the MSI itself, so the transform gets the identical
  TOCTOU protection.
- **The raw-argument reject from ADR 0012 stays.** `ValidateAdditionalArgs` still rejects a
  caller-supplied `TRANSFORMS`/`PATCH` property arriving as an install argument. A declared
  transform travels over its own wire channel, bound to the signed set before it is ever merged
  into the MSI command line, so the two paths never converge until after verification succeeds.
- **Patch (`.msp`) gets no equivalent declaration API in this change.** A caller-supplied `PATCH`
  property stays refused outright, with no signed alternative.

## Consequences

**Authors have a signed way to ship a transform on an elevated install again.** The gap ADR 0012
opened for transforms is closed: `.Transform(id, mstPath)` is the one way to get a transform onto an
elevated per-machine install, and every transform that reaches the install is one the publisher
declared and signed at build time.

**A transform is a signed payload, never an installable package in its own right.** It is bound to
exactly one package by the signed association map and carries no independent install semantics
beyond that binding.

**The Protocol signing helpers gained an optional trailing parameter, a binary break.**
`EcdsaManifestSigner.Sign`/`SignAsync` and `IntegrityEnvelopeCodec.ComputeSignedBytes`/`Sign` each
took an added optional `transformAssociations` parameter. Existing source compiles unchanged. An
external assembly built against the old signature needs to recompile against this release.

**A transform id that collides with a package id fails at install, not at compile.** Both share one
flat id space in the signed payload list (`envelope.Files`, matched by name), and
`ResolveSignedTransformHash` finds the first entry whose name matches the requested transform id.
Nothing at compile time checks a transform's id against the bundle's package ids for uniqueness. A
bundle author who names a transform the same as one of the bundle's package ids gets an install-time
binding failure, not a compile error naming the clash. Giving that collision a compile-time
diagnostic is a follow-up, not attempted here.

**Backward compatible: a bundle with no declared transforms signs the byte-identical message it
signed before this field existed.** `CanonicalizeTransformAssociations` returns an empty string for
a null or empty map, so the transform segment is omitted from the signed message entirely, and every
already-shipped, transform-free bundle keeps verifying against the same bytes it always signed.

**Patch is still out of scope.** This change only covers `.mst` transforms. A caller-supplied
`PATCH` property remains refused with no declaration path, unchanged from ADR 0012.
