namespace ITBees.Ksef.Core;

public sealed class KsefOptions
{
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 100;
    public EndpointsConfig Endpoints { get; set; } = new();
    public sealed class EndpointsConfig
    {
        public string SessionInitToken { get; set; } = "";
        public string InvoicesSubmit { get; set; } = "";
        public string InvoicesStatus { get; set; } = "";
        public string InvoicesUpo { get; set; } = "";
    }
}