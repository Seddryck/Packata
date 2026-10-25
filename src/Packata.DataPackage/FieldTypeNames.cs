namespace Packata.DataPackage;

internal static class FieldTypeNames
{
    public const string String = "string";
    public const string Integer = "integer";
    public const string Any = "any";

    public static string Get(Field field) => field.Type ?? (field switch
    {
        StringField => String,
        IntegerField => Integer,
        _ => Any
    });

    public static bool SupportsCategories(string type) => type is String or Integer;
}
