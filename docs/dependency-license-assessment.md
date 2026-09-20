# Dependency licence assessment

Reviewed 2026-09-20 against the actual cached NuGet package metadata and licence files,
the project references, and the upstream texts linked below. This records the four
packages raised in ledger D120; it is not a complete third-party notice inventory.

FalkForge's current licence is **FSL-1.1-ALv2 with an Additional Grant**, as set out in
[LICENSE.md](../LICENSE.md). The old ledger description of the project as MIT is stale.

| Package examined | Actual terms | Use and disposition |
| --- | --- | --- |
| SecurityCodeScan.VS2019 5.6.7 | LGPL-3.0-or-later | Build analyzer only. Both root and packaged-source Directory.Build.props restrict assets to analyzers/build and mark them PrivateAssets=all. Its assemblies are not runtime references or public NuGet dependencies. |
| JsonSchema.Net 9.4.0 | MIT source; custom Open Source Maintenance Fee Agreement for the supplied binary | Removed at the owner's request. Test-only use did not establish an exemption from the binary agreement. Replaced with NJsonSchema 11.6.1. |
| NETStandard.Library 2.0.3 | MIT | Reference/framework dependency. Preserve its notices if redistributing its files. Its legacy licenseUrl is not evidence of an unknown licence. |
| SonarAnalyzer.CSharp 10.34.0.3385 | SONAR Source-Available License v1.0; no SPDX identifier asserted here | Removed from both repository and packaged-source builds on 2026-09-20 (D137) because the external-AI-use exclusion is incompatible with this workflow. It is not LGPL or MIT. |

The replacement **NJsonSchema 11.6.1** and its dependencies
**NJsonSchema.Annotations 11.6.1**, **Namotion.Reflection 3.5.0**, and
**Newtonsoft.Json 13.0.3** each declare **MIT** in their actual restored nuspec.
Only FalkForge.Core.Tests references the validator. It does not become an installer
runtime dependency. The lock file records the exact resolved closure and content hashes.

PrivateAssets controls NuGet propagation; it is not a universal licence exemption.
The LGPL analyzer is run as a build tool, rather than linked into FalkForge's runtime.
The packaged engine source references the analyzer for consumers to restore, but does
not bundle the analyzer binary. This conclusion depends on retaining the asset filters.
If a future package distributes analyzer binaries, modifies them, or links their code,
reassess the corresponding licence and source/notice obligations. SonarAnalyzer is no
longer referenced by the repository or the packaged engine-source build.

Validation of the schema-validator replacement: the Core suite passed **1,259 tests,
1 skipped** with locked restore. Schema checks include valid SPDX examples and rejection
of missing required fields, wrong types, invalid nested enum values and unexpected
properties. The vendored SPDX schema remains unchanged and has no external references.

Validation of the SonarAnalyzer removal: the full solution and all demos build with zero
warnings under locked restore; the full suite passed **8,960 tests, 511 skipped**, and the
five packaged-engine-source integration tests passed without skips. The repository keeps
the .NET SDK's `latest-all` rules plus Microsoft.VisualStudio.Threading.Analyzers,
IDisposableAnalyzers, Meziantou.Analyzer, SecurityCodeScan.VS2019, and WpfAnalyzers for
WPF projects.

Sources:

- [Security Code Scan LGPL-3.0-or-later text](https://licenses.nuget.org/LGPL-3.0-or-later)
- [NJsonSchema MIT text](https://github.com/RicoSuter/NJsonSchema/blob/master/LICENSE.md)
- [NETStandard.Library MIT text](https://github.com/dotnet/standard/blob/master/LICENSE.TXT)
- [Sonar upstream licence](https://github.com/SonarSource/sonar-dotnet/blob/master/LICENSE.txt)
- JsonSchema.Net 9.4.0: packaged `OSMFEULA.txt` is authoritative for the removed binary;
  [upstream MIT source licence](https://github.com/json-everything/json-everything/blob/master/LICENSE)
  does not alone describe those binary terms.
