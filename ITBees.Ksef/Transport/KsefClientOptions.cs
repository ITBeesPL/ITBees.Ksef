namespace ITBees.Ksef.Transport;

public sealed class KsefClientOptions
{
    public required Uri BaseUrl { get; init; } // np. https://ksef-test.mf.gov.pl/api
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);
}