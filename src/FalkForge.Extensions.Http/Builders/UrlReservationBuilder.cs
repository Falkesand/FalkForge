using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Builders;

public sealed class UrlReservationBuilder(string url)
{
    private string _user = "";

    public UrlReservationBuilder AllowNetworkService() => AllowUser("D:(A;;GX;;;NS)");
    public UrlReservationBuilder AllowLocalService()   => AllowUser("D:(A;;GX;;;LS)");
    public UrlReservationBuilder AllowLocalSystem()    => AllowUser("D:(A;;GX;;;SY)");
    public UrlReservationBuilder AllowEveryone()       => AllowUser("D:(A;;GX;;;WD)");
    public UrlReservationBuilder AllowBuiltinUsers()   => AllowUser("D:(A;;GX;;;BU)");

    public UrlReservationBuilder AllowUser(string sddlOrUser)
    {
        _user = sddlOrUser;
        return this;
    }

    internal UrlReservationModel Build() => new() { Url = url, User = _user };
}
