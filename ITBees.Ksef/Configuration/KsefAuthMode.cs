namespace ITBees.Ksef.Configuration;

/// <summary>
/// How the library proves the caller's identity to KSeF when opening a session.
/// </summary>
public enum KsefAuthMode
{
    /// <summary>
    /// Authorization token generated in the KSeF web application (POST /auth/ksef-token).
    /// </summary>
    Token,

    /// <summary>
    /// Qualified/KSeF certificate — the challenge is answered with a XAdES-BES signed
    /// AuthTokenRequest (POST /auth/xades-signature). No token is stored anywhere.
    /// </summary>
    Certificate
}
