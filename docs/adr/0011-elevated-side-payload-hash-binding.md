# 11. The elevated side re-verifies payload bytes through a handle it opens itself

- Status: Accepted
- Date: 2026-08-22
- Deciders: Peter Falkesand

## Context

Two elevation crossings send a file path from the unelevated engine to the elevated companion
and trust the companion to run whatever is at that path as SYSTEM:

- `MsiExecutor` (`src/FalkForge.Engine/Execution/MsiExecutor.cs`) sends `MsiInstall`, and
  `MsiInstallCommand.Execute` (`src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs`)
  used to call straight into `MsiInstallProductW` after only `File.Exists`.
- `PreUIBootstrapOrchestrator`'s elevated-child path launches prerequisites through
  `PreUIPrerequisiteInstaller.RunAllAsync`
  (`src/FalkForge.Engine/Bootstrap/PreUIPrerequisiteInstaller.cs`), which used to launch the
  process at `exePath` after only a path-containment check.

Both payloads are hashed once already, at cache time: `PackageCache.CachePackage`
(`src/FalkForge.Engine/Cache/PackageCache.cs:19-62`) computes SHA-256 and compares it against
`package.Sha256Hash` before the file is trusted to sit in the cache at all. But that check runs
long before the elevated launch, and nothing re-checked the bytes at the point the elevated side
actually used them. A same-user process running at medium integrity can write to the same cache
locations the engine writes to:

- The MSI cache (`CacheLayout`, `src/FalkForge.Engine/Cache/CacheLayout.cs:11-22`) is
  `%ProgramData%\FalkForge\Cache` for a per-machine bundle or `%LocalAppData%\FalkForge\Cache`
  for a per-user one, and `PackageCache.CachePackage` creates the target directory with a plain
  `Directory.CreateDirectory` (`PackageCache.cs:26`) while the engine is still running unelevated.
- The pre-UI bootstrap extraction directory (`BootstrapperRunner.cs:82`) is
  `Path.GetTempPath()` + `FalkForge\bundles\{guid}`.

In both cases the directory is created by, and owned by, the same unprivileged user account the
attacker is assumed to control. Between the engine's cache-time hash check and the elevated
companion's use of the file, that same user can overwrite the cached bytes and have the swapped
file run as SYSTEM — a time-of-check-to-time-of-use (TOCTOU) gap across a privilege boundary.

## Decision

The engine sends the manifest-declared SHA-256 hash across the elevation boundary alongside the
path, and the elevated side re-verifies it independently before using the file:

- `MsiExecutor` writes `action.Package.Sha256Hash` as a third field in the `MsiInstall` payload
  (`MsiExecutor.cs:178-184`).
- `MsiInstallCommand.Execute` opens the file itself with `FileMode.Open, FileAccess.Read,
  FileShare.Read`, hashes it with `IncrementalHash`, and compares against the sent hash with
  `CryptographicOperations.FixedTimeEquals` before calling `MsiInstallProductW`
  (`MsiInstallCommand.cs`, the `Execute` / `InstallLocked` split).
- `PreUIPrerequisiteInstaller.OpenAndVerifyPayloadHash` does the same for each prerequisite
  before `_runner.RunAsync` launches it (`PreUIPrerequisiteInstaller.cs`, added method).

In both cases the `FileStream` opened for hashing is not closed after the hash check. It is held
open, with `FileShare.Read`, for the entire install call (`MsiInstallCommand`) or the entire child
process run (`PreUIPrerequisiteInstaller`), and only disposed once that call returns. `FileShare.Read`
denies every other process write, rename, and delete access to the file for as long as the handle
is open.

Holding the handle is not incidental to the fix — it is the fix. Hashing the file and then closing
the handle before installing or launching it would still leave a window between the hash check and
the use of the file. That window is smaller than the original one (cache-time hash to elevated use,
versus elevated-open to elevated-use), but it is the same class of gap in a smaller box, not a
closed one. Holding the handle across the entire operation removes the window rather than shrinking
it: the bytes `MsiInstallProductW` reads, or the bytes the child process image is mapped from, are
provably the same bytes that were just hashed, because no other process could have touched them in
between.

### Rejected alternative: hardening the DACL on the cache directory

Locking down the ACL on the MSI cache or the pre-UI extraction directory the way
`TrustStateStore.EnsureSecuredDirectory` locks down `%ProgramData%\FalkForge\Trust`
(`src/FalkForge.Engine.Protocol/Integrity/TrustStateStore.cs:314-352`) does not solve this problem,
and the comparison to `TrustStateStore` does not transfer.

`TrustStateStore`'s directory is created and hardened by the *elevated* companion
(`EnsureSecuredDirectory` seizes ownership to `BUILTIN\Administrators`,
`TrustStateStore.cs:319-323, 469-507`), so its owner is never the same-user attacker. The MSI cache
and the pre-UI extraction directory are the opposite: both are created by the engine while it is
still running unelevated, as the same account the attacker is assumed to control. A directory's
owner holds `WRITE_DAC` on that directory regardless of what the DACL says
(`TrustStateStore.IsAclConforming`'s own comment on this at `TrustStateStore.cs:430-433` states the
same principle for the store it protects). An ACL cannot exclude its own owner. So even if the
engine set a restrictive DACL on the cache directory at creation time, the same user who created it
could rewrite that DACL and grant themselves write access back, because they own the object. There
is no DACL shape that keeps a same-user attacker out of a directory that user's own process created.

## Consequences that are not a full fix

The hash the elevated side compares against comes from the engine process, over the same pipe, and
the engine runs at the same integrity level as the attacker in this threat model. Someone who can
inject code into the engine process — not overwrite a file on disk, but run code inside the engine
itself — can simply send a hash that matches whatever file they placed, and the elevated side will
verify it successfully and run it anyway. This change converts a passive file-overwrite attack into
a requirement to achieve code injection into the engine process, and it removes the timing window
that made the passive attack possible. It does not make the elevated side independently certain of
what it is installing: the elevated side's only source of truth for "what hash should this be" is
still a value the unprivileged, same-user engine process sent it.

The durable fix is a trust anchor that does not depend on the engine process's own honesty —
something rooted in `FalkForge.Engine.Elevation` itself, plus lifting the existing signed-manifest
mechanism (`ManifestSignatureEnvelope`,
`src/FalkForge.Engine.Protocol/Integrity/ManifestSignatureEnvelope.cs`) into
`FalkForge.Engine.Protocol` so the elevated side can check the hash against a signature it verifies
itself, rather than trusting a bare value from the wire payload. That work is not part of this
change and is not done.

## Evidence for the file-lock decision, and its limits

Running `MsiVerifyPackageW` (from the real system `msi.dll`, via a direct P/Invoke) against
`demo/01-hello-world/Hello_World-1.0.0.msi` returned `ERROR_SUCCESS` (0) both with and without a
`FileShare.Read` handle held open on the file at the same time. In the same window where the handle
was held, a write-open (`FileAccess.Write`) on the file and a `File.Delete` on the file both failed
with `IOException` ("The process cannot access the file... because it is being used by another
process"). This confirms that Windows Installer's package-open path is compatible with a
`FileShare.Read` lock held by another handle, and that the lock does what it is meant to do: deny
write and delete to other processes while it is held.

This is not proof that a full, real MSI install tolerates the lock. `MsiVerifyPackageW` only opens
and validates the package structure; it does not run an install. A real `MsiInstallProductW` call
also has the Windows Installer service (`msiexec.exe`, running as a separate process from the
elevated companion) open the package file itself, and that second, separate open is not exercised by
this test. Whether the full install path tolerates the `FileShare.Read` lock held by
`MsiInstallCommand` is not confirmed here and is left as outstanding verification.
