using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[CollectionDefinition("DemoEndToEnd")]
public sealed class DemoTestCollection : ICollectionFixture<DemoBuildFixture>;
