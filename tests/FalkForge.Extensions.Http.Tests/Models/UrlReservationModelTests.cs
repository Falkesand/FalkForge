using FalkForge.Extensions.Http.Models;
using Xunit;

namespace FalkForge.Extensions.Http.Tests.Models;

public sealed class UrlReservationModelTests
{
    [Fact]
    public void UrlReservationModel_StoresUrlAndUser()
    {
        var model = new UrlReservationModel { Url = "http://+:8080/svc/", User = "D:(A;;GX;;;NS)" };

        Assert.Equal("http://+:8080/svc/", model.Url);
        Assert.Equal("D:(A;;GX;;;NS)", model.User);
    }
}
