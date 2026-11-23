using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

/// <summary>Configures one PocketCsvReader delimited field and optionally registers its native parsers.</summary>
public sealed record DelimitedFieldBinding(
    Action<ISchemaDescriptorBuilder, DataField, Type> BindField,
    Action<RuntimeParsersDescriptorBuilder>? ConfigureParsers = null)
{
    /// <summary>The binding key for Packata's CSV, TSV, and PSV provider.</summary>
    public const string ReaderKey = "packata.delimited";
}
