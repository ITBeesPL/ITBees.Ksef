using ITBees.Ksef.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                    services.AddITBeesKsef(ctx.Configuration);
                    services.AddSingleton<App>();
                })
                .Build();

            await host.Services.GetRequiredService<App>().RunAsync();
        }
    }
}
