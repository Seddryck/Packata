using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class ConnectorCommandRegistration
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("connector", connector =>
        {
            connector.SetDescription("Discover and prepare curated database connectors using shared user configuration.");
            connector.AddCommand<ConnectorListCommand>("list").WithDescription("List catalog and installed connectors without downloading or loading them.");
            connector.AddCommand<ConnectorShowCommand>("show").WithDescription("Show versions, availability, compatibility and prerequisites.");
            connector.AddCommand<ConnectorInstallCommand>("install").WithDescription("Install and activate the pinned connector; respects connectors.offline.");
        });
    }
}
