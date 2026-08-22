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
path, and the elevated side re-verifies it independently before using the file. Both crossings call
one shared helper, `FalkForge.Engine.Protocol.Integrity.HashBoundFile.Open`
(`src/FalkForge.Engine.Protocol/Integrity/HashBoundFile.cs`), rather than each carrying its own copy
of open-hash-compare:

- `MsiExecutor` writes `action.Package.Sha256Hash` as a third field in the `MsiInstall` payload
  (`MsiExecutor.cs:181-187`).
- `MsiInstallCommand.Execute` calls `HashBoundFile.Open(msiPath, expectedHashHex)`, which opens the
  file with `FileMode.Open, FileAccess.Read, FileShare.Read`, hashes it, and compares against the
  sent hash with `CryptographicOperations.FixedTimeEquals` (`MsiInstallCommand.cs`, the `Execute` /
  `InstallLocked` split).
- `PreUIPrerequisiteInstaller.RunAllAsync` calls the same helper for each prerequisite before
  `_runner.RunAsync` launches it (`PreUIPrerequisiteInstaller.cs`).

In both cases the `FileStream` opened for hashing is not closed after the hash check. It is held
open, with `FileShare.Read`, for the entire install call (`MsiInstallCommand`) or the entire child
process run (`PreUIPrerequisiteInstaller`), and only disposed once that call returns. `FileShare.Read`
denies every other process write, rename, and delete access to the file object for as long as the
handle is open.

Holding the handle was not, on its own, enough, and this ADR originally claimed otherwise. A handle
pins the file object it was opened against. It does not pin the reparse points in the path used to
reach that object. The first version of this fix hashed the file through the held handle and then
handed the elevated side the original caller-supplied path string, which `MsiInstallProductW` or
`Process.Start` opens a second time. A same-user process needs no privilege to rename the cache
directory, drop an NTFS junction of the same name in its place, and repoint that junction to an
attacker-controlled directory while the hash check runs. Deleting a junction removes only the
reparse point; it does not touch the file the held handle still points at. So the repoint succeeds
while the handle is held, and the second open, through the same path string, lands on the
attacker's file instead of the one that was just hashed. This was verified on this machine: a
junction repointed while the handle was held, and re-opening the same path string afterwards
returned the attacker's bytes. `HashBoundFileTests`, `MsiInstallCommandTests` and
`PreUIPrerequisiteInstallerTests` now build this attack directly against a real NTFS junction
(`tests/TestJunction.cs`) and assert both that the repoint succeeded and that the consumer still
read the publisher's bytes.

The fix is two bindings, not one:

- **The handle binds the bytes.** Opening the file once with `FileShare.Read` and holding that
  handle across the hash and the install/launch means no other process can write, rename, or
  delete the file object while the handle is open. This part was already true before the round of
  fixes described above.
- **`GetFinalPathNameByHandle` binds the identity of the file the consumer opens.** After hashing,
  `HashBoundFile.Open` calls `GetFinalPathNameByHandle` on the same handle and returns that
  resolved path, with every reparse point already followed, instead of the caller-supplied string.
  `MsiInstallCommand` and `PreUIPrerequisiteInstaller` pass the resolved path — not the original
  one — to `MsiInstallProductW` and to the process runner. A junction repointed between the hash
  and the install/launch no longer matters, because the second open never goes through the
  junction again.

Bytes without identity is what this branch shipped first, and a junction repoint defeated it.

### Rejected alternative: hardening the DACL on the cache directory

Locking down the ACL on the MSI cache or the pre-UI extraction directory the way
`TrustStateStore.EnsureSecuredDirectory` locks down `%ProgramData%\FalkForge\Trust`
(`src/FalkForge.Engine.Protocol/Integrity/TrustStateStore.cs:314-352`) is still rejected, but an
earlier version of this section got the reason wrong. It claimed no DACL shape can keep a directory's
own owner out, because the owner always holds implicit `WRITE_DAC`. That claim is false: Windows has
carried the `OWNER RIGHTS` SID (`S-1-3-4`) since Vista specifically to override an owner's implicit
`READ_CONTROL` and `WRITE_DAC`. Tested on this machine: as the owner of a directory carrying an
`OWNER RIGHTS` allow-read ACE and no explicit grant to the owning account, a write into that
directory failed with `UnauthorizedAccessException`, and rewriting its DACL failed with
`PrivilegeNotHeldException`. A DACL using `OWNER RIGHTS` can keep a same-user owner out of a
directory that user's own process created.

The reasons to reject this alternative still stand, on different grounds:

- A DACL on the cache directory would not have stopped the attack that actually broke this branch.
  The junction-repoint attack does not write into the target directory at all — it deletes the
  reparse point and creates a new one next to it, an operation the parent directory's permissions
  govern, not necessarily the target directory's own DACL. Hardening the leaf directory's DACL does
  not, by itself, establish that the parent is equally hardened, and for the pre-UI extraction
  directory in particular the parent is the user's own temp/profile tree, which that user already
  controls regardless of any DACL FalkForge places on the leaf.
- `TrustStateStore`'s directory is created and hardened by the *elevated* companion
  (`EnsureSecuredDirectory` seizes ownership to `BUILTIN\Administrators`,
  `TrustStateStore.cs:319-323, 469-507`), so its owner is never the same-user attacker. The MSI
  cache and the pre-UI extraction directory are created by the engine while it is still running
  unelevated, as the same account the attacker is assumed to control, so the comparison to
  `TrustStateStore` does not transfer without first re-parenting the directory to an elevated owner
  — which this branch does not do.
- Even where an `OWNER RIGHTS` DACL would help, it is defense in depth on top of the actual fix, not
  a replacement for it. The fix has to bind both the bytes and the identity of the file the
  consumer opens; a directory ACL binds neither on its own.

This does not close the door on `OWNER RIGHTS` DACL hardening for the cache directory. It is a
possible future defense-in-depth measure the codebase does not use anywhere yet, not something
this ADR has ruled out as impossible.

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
