#nullable enable
using System;
using System.Text.Json.Serialization;

namespace Example.Models;

// A default OptionalValue is absent; a constructed value (including null) is present.
public readonly struct OptionalValue<T>
{
    public bool HasValue { get; }
    private readonly T value;
    public T Value => HasValue ? value : throw new InvalidOperationException("The field is absent.");
    public OptionalValue(T value) { this.value = value; HasValue = true; }
    public static implicit operator OptionalValue<T>(T value) => new(value);
}

public class Order
{
    [JsonPropertyName("id")]
    public required int id { get; set; }
    [JsonPropertyName("label")]
    public required string? label { get; set; }
    [JsonPropertyName("note")]
    public OptionalValue<string?> note { get; set; }
}
