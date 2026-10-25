using System.Runtime.InteropServices;

namespace Packata.Cli;

internal static class ConnectorOutput
{
    internal static string DependencyNote(string? driver) => driver is "System.Data.Odbc" or "System.Data.OleDb"
        ? "A compatible external vendor driver must be installed separately."
        : "Required managed and native driver dependencies must be available for this runtime.";

    public static void Write(IReadOnlyList<ConnectorDescription> connectors, TextWriter output, bool verbose = false)
    {
        output.WriteLine($"Connectors · Runtime: {RuntimeInformation.RuntimeIdentifier}");
        output.WriteLine();
        if (verbose)
        {
            foreach (var info in connectors)
            {
                output.WriteLine($"{info.Id} · {info.Version}");
                Detail("Listed", info.Listed ? "Yes" : "No");
                Detail("Installation", info.Installed ? "Installed" : "Not installed");
                Detail("Activation", info.Active ? "Active" : "Inactive");
                Detail("Health", info.Unhealthy ? "Unhealthy" : "No issue reported (discovery only)");
                Detail("Load status", info.Loaded switch { true => "Loaded", false => "Not loaded", null => "Not inspected" });
                Detail("Download", Download(info));
                output.WriteLine();
                Detail("Aliases", Values(info.Schemes));
                Detail("API range", info.ApiRange ?? "Not specified");
                Detail("Frameworks", Values(info.TargetFrameworks));
                Detail("Runtimes", Values(info.RuntimeIdentifiers));
                Detail("Driver", info.Driver ?? "Not specified");
                Detail("Dependencies", DependencyNote(info.Driver));
                Diagnostics(info);
                output.WriteLine();
            }
        }
        else
        {
            var idWidth = Math.Max("CONNECTOR".Length, connectors.Select(info => info.Id.Length).DefaultIfEmpty().Max());
            var versionWidth = Math.Max("VERSION".Length, connectors.Select(info => info.Version.Length).DefaultIfEmpty().Max());
            Row("CONNECTOR", "VERSION", "STATUS", "DOWNLOAD");
            foreach (var info in connectors)
            {
                Row(info.Id, info.Version, Status(info), Download(info));
                Diagnostics(info);
            }
            output.WriteLine();

            void Row(string id, string version, string status, string download)
                => output.WriteLine($"{id.PadRight(idWidth)}   {version.PadRight(versionWidth)}   {status.PadRight(13)}   {download}");
        }

        var noun = connectors.Count == 1 ? "connector" : "connectors";
        output.WriteLine($"{connectors.Count} {noun} · {connectors.Count(info => info.Installed)} installed · {connectors.Count(info => info.Active)} active");
        output.WriteLine();
        output.WriteLine("Load status and server connectivity are not checked by this command.");
        output.WriteLine("Some connectors require separately installed driver dependencies.");

        void Detail(string label, string value) => output.WriteLine($"  {label.PadRight(14)} {value}");
        void Diagnostics(ConnectorDescription info)
        {
            foreach (var code in info.DiagnosticCodes) Detail("Diagnostic", code);
        }
    }

    private static string Status(ConnectorDescription info) => info.Unhealthy ? "Unhealthy"
        : info.Active ? "Active" : info.Installed ? "Installed" : "Not installed";

    private static string Download(ConnectorDescription info) => info.DownloadAvailable ? "Available" : "Unavailable";

    private static string Values(IReadOnlyList<string> values) => values.Count == 0 ? "Not specified" : string.Join(", ", values);
}
