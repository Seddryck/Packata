using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using Packata.LanguageServer.Core;
using Packata.LanguageServer.Handlers;

namespace Packata.LanguageServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            // Protocol traces and exception details can contain document contents; logging is opt-in on the client.
            .ConfigureLogging(logging => logging.ClearProviders())
            .WithServices(services => services.AddSingleton<DocumentStore>().AddSingleton<ContractService>().AddSingleton<DiagnosticPublisher>())
            .WithHandler<DocumentSyncHandler>()
            .WithHandler<InspectHandler>()
            .WithHandler<ValidateHandler>()
            .WithHandler<ProtocolHandler>()
            .WithHandler<DiscoverHandler>());
        await server.WaitForExit;
    }
}
