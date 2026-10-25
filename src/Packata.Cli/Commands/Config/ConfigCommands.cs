using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class ConfigCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("config", config =>
        {
            config.SetDescription("User settings: connectors.offline (false; downloads only), connectors.store (per-user; DUBURL_PROVIDER_STORE overrides), connectors.catalog (bundled), connectors.preferred.<scheme>. PACKATA_CONFIG_HOME overrides the user config directory.");
            config.AddCommand<ConfigListCommand>("list").WithDescription("Show effective settings and origins.");
            config.AddCommand<ConfigGetCommand>("get").WithDescription("Read an effective setting.");
            config.AddCommand<ConfigSetCommand>("set").WithDescription("Persist a user setting; filesystem paths become absolute.");
            config.AddCommand<ConfigUnsetCommand>("unset").WithDescription("Remove a user override, preserving installed connectors.");
        });
    }
}
