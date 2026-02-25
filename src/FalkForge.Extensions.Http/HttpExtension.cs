using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Builders;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Extensions.Http.Validation;

namespace FalkForge.Extensions.Http;

[SupportedOSPlatform("windows")]
public sealed class HttpExtension : IFalkForgeExtension
{
    private readonly List<UrlReservationModel> _reservations = [];
    private readonly List<SniSslBindingModel>  _bindings     = [];

    public string Name => "Http";

    public HttpExtension AddUrlReservation(string url, Action<UrlReservationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var builder = new UrlReservationBuilder(url);
        configure(builder);
        _reservations.Add(builder.Build());
        return this;
    }

    public HttpExtension AddSniSslBinding(string hostname, int port, Action<SniSslBindingBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        var builder = new SniSslBindingBuilder(hostname, port);
        configure(builder);
        _bindings.Add(builder.Build());
        return this;
    }

    public IReadOnlyList<Error> Validate()
    {
        var errors = new List<Error>();
        errors.AddRange(HttpValidator.ValidateReservations(_reservations));
        errors.AddRange(HttpValidator.ValidateBindings(_bindings));
        return errors;
    }

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(new HttpCustomActionContributor(_reservations, _bindings));
        registry.RegisterTableContributor(new HttpSequenceContributor(_reservations, _bindings));
    }
}
