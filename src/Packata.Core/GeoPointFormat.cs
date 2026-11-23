namespace Packata.Core;

/// <summary>Selects the input format accepted by <see cref="GeoPointParser"/>.</summary>
public enum GeoPointFormat
{
    /// <summary>A comma-separated longitude and latitude.</summary>
    Default,

    /// <summary>A JSON array containing longitude and latitude.</summary>
    Array,

    /// <summary>A JSON object with longitude and latitude properties.</summary>
    Object,

    /// <summary>Detects the format from the input.</summary>
    Auto
}
