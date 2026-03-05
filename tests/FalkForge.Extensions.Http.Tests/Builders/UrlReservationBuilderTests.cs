using FalkForge.Extensions.Http.Builders;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Builders;

public sealed class UrlReservationBuilderTests
{
    [Fact]
    public void AllowNetworkService_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowNetworkService().Build();
        Assert.Equal("D:(A;;GX;;;NS)", model.User);
    }

    [Fact]
    public void AllowLocalService_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowLocalService().Build();
        Assert.Equal("D:(A;;GX;;;LS)", model.User);
    }

    [Fact]
    public void AllowLocalSystem_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowLocalSystem().Build();
        Assert.Equal("D:(A;;GX;;;SY)", model.User);
    }

    [Fact]
    public void AllowEveryone_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowEveryone().Build();
        Assert.Equal("D:(A;;GX;;;WD)", model.User);
    }

    [Fact]
    public void AllowBuiltinUsers_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowBuiltinUsers().Build();
        Assert.Equal("D:(A;;GX;;;BU)", model.User);
    }

    [Fact]
    public void AllowUser_SetsArbitraryValue()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowUser("DOMAIN\\SvcAccount").Build();
        Assert.Equal("DOMAIN\\SvcAccount", model.User);
    }

    [Fact]
    public void Build_PreservesUrl()
    {
        var model = new UrlReservationBuilder("http://+:9090/api/").AllowEveryone().Build();
        Assert.Equal("http://+:9090/api/", model.Url);
    }
}
