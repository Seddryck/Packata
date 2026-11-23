namespace Packata.ResourceReaders;

internal static class RepresentationType
{
    internal static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    internal static bool Same(Type left, Type right) => Underlying(left) == Underlying(right);

    internal static bool Accepts(Type type, object value) => Underlying(type).IsInstanceOfType(value);
}
