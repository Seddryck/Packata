using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public abstract class BaseTypeMapper<T>
{
    protected struct TypeFormat : IEquatable<TypeFormat>
    {
        public TypeFormat(string type, string? format)
        {
            Type = type;
            Format = format;
        }

        public string Type { get; set; }
        public string? Format { get; set; }
        public bool Equals(TypeFormat other)
            => Type == other.Type && (Format == other.Format
                    || (other.Format is null && Format is null)
            );
    }

    protected Dictionary<TypeFormat, T> Mappings { get; } = [];
    private Dictionary<TypeFormat, T> CustomMappings { get; } = [];
    protected abstract T DefaultMapping { get; }

    public BaseTypeMapper()
    {
        Initialize();
    }

    protected abstract void Initialize();
    protected virtual bool TryMapFormat(string type, string? format, out T mapping)
    {
        mapping = default!;
        return false;
    }
    public void Register(string type, T runtimeType)
        => Register(type, null, runtimeType);

    public void Register(string type, string? format, T runtimeType)
    {
        var key = new TypeFormat(type, format);
        CustomMappings[key] = runtimeType;
        if (!Mappings.TryAdd(key, runtimeType))
            Mappings[key] = runtimeType;
    }

    protected void RegisterDefault(string type, string? format, T mapping)
        => Mappings[new TypeFormat(type, format)] = mapping;

    protected bool TryGetCustomMapping(string? type, string? format, out T mapping)
    {
        if (type is not null && CustomMappings.TryGetValue(new(type, format), out mapping!)) return true;
        mapping = default!;
        return false;
    }

    protected bool TryGetResolutionMapping(FieldTypeResolution result, T offsetMapping, out T mapping)
    {
        var field = result.Field;
        if (TryGetCustomMapping(field.LogicalType, field.Format, out mapping)) return true;
        // Match Map's hook precedence, without treating the built-in offset rule as a
        // customization capable of overriding rich range/precision diagnostics.
        if (TryGetCustomFormatMapping(field.LogicalType, field.Format, offsetMapping, out mapping)) return true;
        if (AllowsUnformattedCustomMapping(result)
            && TryGetCustomMapping(field.LogicalType, null, out mapping)) return true;
        mapping = default!;
        return false;
    }

    private bool TryGetCustomFormatMapping(string? type, string? format, T offsetMapping, out T mapping)
    {
        mapping = default!;
        if (type is null || Mappings.ContainsKey(new(type, format))) return false;
        if (!TryMapFormat(type, format, out mapping)) return false;
        return !TypeMappingCatalog.HasOffset(type, format)
            || !EqualityComparer<T>.Default.Equals(mapping, offsetMapping);
    }

    private static bool AllowsUnformattedCustomMapping(FieldTypeResolution result)
    {
        if (result.SourceFormat is null) return false;
        if (TypeMappingCatalog.HasOffset(result.LogicalType ?? "", result.SourceFormat)) return false;
        var format = result.SourceFormat.Trim().ToLowerInvariant();
        return !TypeMappingCatalog.Entries.Any(e => e.LogicalType == result.LogicalType && e.Format == format);
    }

    public T Map(string? type, string? format)
    {
        if (type is not null)
        {
            var key = new TypeFormat(type, format);
            if (Mappings.TryGetValue(key, out var runtimeType))
                return runtimeType;
            else if (TryMapFormat(type, format, out runtimeType))
                return runtimeType;
            else if (Mappings.TryGetValue(new TypeFormat(type, null), out runtimeType))
                return runtimeType;
        }
        return DefaultMapping;
    }
}
