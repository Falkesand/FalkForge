namespace FalkForge.Signing.SignServer;

/// <summary>
/// How the <see cref="SignServerSignatureProvider"/> authenticates to the SignServer REST endpoint.
/// SignServer's default worker <c>AUTHTYPE</c> is <c>NOAUTH</c> (<see cref="None"/>); production
/// deployments front the worker with client-certificate (mTLS), HTTP Basic, or bearer-token auth.
/// </summary>
public enum SignServerAuthMode
{
    /// <summary>No authentication (SignServer <c>NOAUTH</c>) — network-isolated CI only.</summary>
    None = 0,

    /// <summary>Mutual TLS: a client certificate is presented on the TLS handshake (SignServer <c>CLIENTCERT</c>).</summary>
    ClientCert,

    /// <summary>HTTP Basic authentication (username + password).</summary>
    Basic,

    /// <summary>Bearer token in the <c>Authorization</c> header (e.g. a JWT).</summary>
    Bearer
}
