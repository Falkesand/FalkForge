using Xunit;

namespace FalkForge.Core.Tests;

// DisableParallelization prevents this collection from running concurrently with other
// collections in the same process. SOURCE_DATE_EPOCH is a process-global environment variable,
// so any concurrent mutation would be a data race regardless of which collection sets it.
[CollectionDefinition("SourceDateEpoch", DisableParallelization = true)]
public sealed class SourceDateEpochCollection { }
