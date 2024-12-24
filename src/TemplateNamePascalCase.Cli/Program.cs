using System;
using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace $TemplateNamePascalCase$.Cli;
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
                });
                logging.SetMinimumLevel(LogLevel.None);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddTransient<RenderOptions>();
                services.AddTransient<RootCommand>(provider =>
                    new RenderCommand(
                        provider.GetRequiredService<RenderOptions>(),
                        provider.GetRequiredService<ILogger<RenderCommand>>()
                    ));
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation($"$TemplateNamePascalCase$ Command Line Interface: version {Assembly.GetExecutingAssembly().GetName().Version}");

        var command = host.Services.GetRequiredService<RootCommand>();
        return await command.InvokeAsync(args);
    }
}
