using ITBees.Ksef.Core;
using ITBees.Ksef.KsefV2;
using ITBees.Ksef.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace TestKsefConsoleApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(cfg =>
                {
                    cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                        o.TimestampFormat = "[HH:mm:ss] ";
                    });
                })
                .ConfigureServices((ctx, services) =>
                {
                    services.Configure<KsefOptions>(ctx.Configuration.GetSection("Ksef"));

                    // HttpClient with retry & timeout
                    services.AddHttpClient<IKsefClient, KsefClient>((sp, http) =>
                        {
                            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KsefOptions>>().Value;
                            http.BaseAddress = new Uri(opt.BaseUrl);
                            http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
                        })
                        .AddPolicyHandler(HttpPolicyExtensions
                            .HandleTransientHttpError()
                            .OrResult(r => (int)r.StatusCode == 429)
                            .WaitAndRetryAsync(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7) }));

                    services.AddSingleton<App>();
                })
                .Build();

            await host.Services.GetRequiredService<App>().RunAsync();
        }
    }
}