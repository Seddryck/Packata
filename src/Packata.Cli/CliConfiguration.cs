using System.Text.Json;
using DubUrl.Application;
using DubUrl.Core.ProviderPacks;

namespace Packata.Cli;

internal sealed class CliException(string message) : Exception(message);

internal sealed record ConfigurationValue(string Key, object Value, string Origin, string? UserValue);

internal sealed class CliConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string PreferencePrefix = "connectors.preferred.";
    private readonly string _path;
    private readonly Func<string, string?> _environment;

    public CliConfiguration(string? path = null, Func<string, string?>? environment = null)
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
        var home = _environment("PACKATA_CONFIG_HOME");
        _path = path ?? Path.Combine(string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Packata") : home,
            "config.json");
    }

    public IReadOnlyList<ConfigurationValue> List()
    {
        var saved = Read();
        return new[] { "connectors.offline", "connectors.store", "connectors.catalog" }
            .Concat(saved.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(key => Effective(key, saved)).ToArray();
    }

    public ConfigurationValue Get(string key) => Effective(ValidateKey(key), Read());

    public void Set(string key, string value) => Update(ValidateKey(key), Normalize(key, value));
    public void Unset(string key) => Update(ValidateKey(key), null);

    private void Update(string key, string? value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Avoid lost updates between concurrent CLI invocations.
        using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var saved = Read();
        if (value is null) saved.Remove(key); else saved[key] = value;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(saved, JsonOptions));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), JsonOptions)
                ?? throw new CliException("The user configuration is empty.");
            foreach (var pair in saved) { ValidateKey(pair.Key); Normalize(pair.Key, pair.Value); }
            return saved;
        }
        catch (JsonException) { throw new CliException("The user configuration is invalid JSON."); }
    }

    private ConfigurationValue Effective(string key, Dictionary<string, string> saved)
    {
        saved.TryGetValue(key, out var user);
        var environment = key == "connectors.store" ? _environment("DUBURL_PROVIDER_STORE") : null;
        var value = !string.IsNullOrWhiteSpace(environment) ? Path.GetFullPath(environment) : user;
        var origin = !string.IsNullOrWhiteSpace(environment) ? "environment:DUBURL_PROVIDER_STORE" : user is null ? "default" : "user";
        value ??= key switch
        {
            "connectors.offline" => "false",
            "connectors.store" => new ProviderPackStore().RootPath,
            "connectors.catalog" => "bundled",
            _ => ""
        };
        return new(key, key == "connectors.offline" ? bool.Parse(value) : value, origin, user);
    }

    private static string ValidateKey(string key)
    {
        if (key is "connectors.offline" or "connectors.store" or "connectors.catalog") return key;
        if (key.StartsWith(PreferencePrefix, StringComparison.Ordinal)
            && Uri.CheckSchemeName(key[PreferencePrefix.Length..]) && key == key.ToLowerInvariant()) return key;
        throw new CliException("Unknown configuration key. Use 'packata config --help' for supported settings.");
    }

    private static string Normalize(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new CliException("A configuration value must not be empty.");
        if (key == "connectors.offline")
            return bool.TryParse(value, out var offline) ? offline.ToString().ToLowerInvariant()
                : throw new CliException("connectors.offline requires true or false; it controls connector downloads only.");
        if (key is "connectors.store" or "connectors.catalog")
        {
            if (value.Contains("://", StringComparison.Ordinal)) throw new CliException("Connector paths must be local filesystem paths.");
            return Path.GetFullPath(value);
        }
        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new CliException("A connector preference requires a canonical connector ID.");
        return value;
    }

    public bool Offline => (bool)Get("connectors.offline").Value;
    public ProviderPackStore Store => new((string)Get("connectors.store").Value);
    public ProviderPackCatalog Catalog()
    {
        var catalog = (string)Get("connectors.catalog").Value;
        if (Get("connectors.catalog").Origin == "default") return BundledProviderCatalog.Create();
        try { using var stream = File.OpenRead(catalog); return ProviderPackCatalog.Read(stream); }
        catch { throw new CliException("The configured connector catalog could not be read or validated."); }
    }

    public Dictionary<string, string> Preferences() => List().Where(value => value.Key.StartsWith(PreferencePrefix, StringComparison.Ordinal))
        .ToDictionary(value => value.Key[PreferencePrefix.Length..], value => (string)value.Value, StringComparer.OrdinalIgnoreCase);
}
