using System.Data;

namespace Packata.ResourceReaders;

/// <summary>Provides typed field access for resource readers exposed as <see cref="IDataReader"/>.</summary>
public static class ResourceReaderValueExtensions
{
    /// <summary>Gets the field value as the requested CLR type.</summary>
    public static T GetFieldValue<T>(this IDataRecord reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return (T)reader.GetValue(ordinal);
    }
}
