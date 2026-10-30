using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class FieldTypeResolverTests
{
    [TestCase("i8", typeof(sbyte), DbType.SByte, 8, true)]
    [TestCase("u8", typeof(byte), DbType.Byte, 8, false)]
    [TestCase("i16", typeof(short), DbType.Int16, 16, true)]
    [TestCase("u16", typeof(ushort), DbType.UInt16, 16, false)]
    [TestCase("i32", typeof(int), DbType.Int32, 32, true)]
    [TestCase("u32", typeof(uint), DbType.UInt32, 32, false)]
    [TestCase("i64", typeof(long), DbType.Int64, 64, true)]
    [TestCase("u64", typeof(ulong), DbType.UInt64, 64, false)]
    public void Integer_formats_retain_the_complete_domain(string format, Type runtime, DbType database, int width, bool signed)
    {
        var result = Resolve(new("value", "integer", Format: format));
        Assert.Multiple(() =>
        {
            Assert.That(result.RuntimeType, Is.EqualTo(runtime));
            Assert.That(result.DatabaseType, Is.EqualTo(database));
            Assert.That(result.Parameters.IntegerWidth, Is.EqualTo(width));
            Assert.That(result.Parameters.Signed, Is.EqualTo(signed));
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Parameters_select_integer_representation_and_format_has_precedence()
    {
        var field = new DataField("value", "integer", Constraints: [new("integerWidth", 64), new("signed", false)]);
        Assert.That(Resolve(field).RuntimeType, Is.EqualTo(typeof(ulong)));
        var formatted = Resolve(field with { Format = "i16" });
        Assert.That(formatted.RuntimeType, Is.EqualTo(typeof(short)));
        Assert.That(formatted.Diagnostics.Single().Code, Is.EqualTo("ConflictingSemantics"));
        var incomplete = Resolve(field with { Constraints = [new("integerWidth", 64)] });
        Assert.That(incomplete.RuntimeType, Is.Null);
    }

    [TestCase("integer", "u128")]
    [TestCase("integer", "garbage")]
    [TestCase("number", "fp128")]
    [TestCase("datetime", "%Z")]
    public void Unsupported_formats_are_diagnosed_with_source_context(string type, string format)
    {
        var result = new RuntimeTypeMapper().Resolve(new("value", type, "ambiguous", format),
            new(SourcePath: "asset/value"));
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.DatabaseType, Is.Null);
        var diagnostic = result.Diagnostics.Single(d => d.Code == "UnsupportedFormat");
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.FieldPath, Is.EqualTo("asset/value"));
            Assert.That(diagnostic.LogicalType, Is.EqualTo(type));
            Assert.That(diagnostic.Format, Is.EqualTo(format));
            Assert.That(diagnostic.PhysicalType, Is.EqualTo("ambiguous"));
        });
        if (format == "u128")
        {
            Assert.That(result.Parameters.IntegerWidth, Is.EqualTo(128));
            Assert.That(result.Parameters.Signed, Is.False);
        }
    }

    [TestCase(TypeMappingFallback.LogicalType, typeof(int), DbType.Int32)]
    [TestCase(TypeMappingFallback.Object, typeof(object), DbType.Object)]
    [TestCase(TypeMappingFallback.String, typeof(string), DbType.String)]
    public void Fallback_is_explicit_and_keeps_errors(TypeMappingFallback policy, Type runtime, DbType database)
    {
        var result = new RuntimeTypeMapper().Resolve(new("value", "integer", Format: "u128"), new(policy));
        Assert.That(result.RuntimeType, Is.EqualTo(runtime));
        Assert.That(result.DatabaseType, Is.EqualTo(database));
        Assert.That(result.Diagnostics.Count(d => d.Code == "Fallback"), Is.EqualTo(2));
        Assert.That(result.Diagnostics.Any(d => d.Code == "UnsupportedFormat"), Is.True);
    }

    [TestCase(28, 0, true)]
    [TestCase(28, 28, true)]
    [TestCase(29, 0, false)]
    [TestCase(29, 28, false)]
    [TestCase(38, 10, false)]
    public void Decimal_checks_full_domain_independently_of_database(int precision, int scale, bool fits)
    {
        var result = Resolve(new("amount", "number", Constraints: [new("precision", precision), new("scale", scale)]));
        Assert.That(result.RuntimeType, Is.EqualTo(fits ? typeof(decimal) : null));
        Assert.That(result.DatabaseType, Is.EqualTo(DbType.Decimal));
        Assert.That(result.Parameters.Precision, Is.EqualTo(precision));
        Assert.That(result.Parameters.Scale, Is.EqualTo(scale));
        Assert.That(result.Diagnostics.Count, Is.EqualTo(fits ? 0 : 1));
    }

    [TestCase(0, 0)]
    [TestCase(4, 5)]
    [TestCase(4, -1)]
    public void Invalid_decimal_parameters_do_not_produce_representations(int precision, int scale)
    {
        var result = Resolve(new("amount", "number", Constraints: [new("precision", precision), new("scale", scale)]));
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.DatabaseType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "InvalidParameter"), Is.True);
    }

    [Test]
    public void Unspecified_parameters_are_not_invented_and_excess_scale_is_reported()
    {
        var unspecified = Resolve(new("amount", "number"));
        Assert.That(unspecified.Parameters.Precision, Is.Null);
        Assert.That(unspecified.Parameters.Scale, Is.Null);
        var excessive = Resolve(new("amount", "number", Constraints: [new("scale", 29)]));
        Assert.That(excessive.Parameters.Scale, Is.EqualTo(29));
        Assert.That(excessive.RuntimeType, Is.Null);
        Assert.That(excessive.DatabaseType, Is.EqualTo(DbType.Decimal));
    }

    [TestCase("i8", "128")]
    [TestCase("u64", "18446744073709551616")]
    [TestCase(null, "2147483648")]
    public void Integer_bounds_must_fit_the_selected_representation(string? format, string maximum)
    {
        var result = Resolve(new("value", "integer", Format: format, Constraints: [new("maximum", maximum)]));
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "RepresentationLoss"), Is.True);
    }

    [Test]
    public void Exact_unsigned_boundary_fits_without_decimal_or_double_rounding()
    {
        var result = Resolve(new("value", "integer", Format: "u64", Constraints: [new("maximum", "18446744073709551615")]));
        Assert.That(result.RuntimeType, Is.EqualTo(typeof(ulong)));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [TestCase("datetime", null, typeof(DateTime), DbType.DateTime, TemporalSemantics.Local)]
    [TestCase("datetime", "%Y-%m-%dT%H:%M:%S%z", typeof(DateTimeOffset), DbType.DateTimeOffset, TemporalSemantics.Offset)]
    [TestCase("datetime", "%Y%%z", typeof(DateTime), DbType.DateTime, TemporalSemantics.Local)]
    [TestCase("timestamp", null, typeof(DateTimeOffset), DbType.DateTimeOffset, TemporalSemantics.Instant)]
    public void Temporal_patterns_and_meaning_are_distinct(string type, string? format, Type runtime, DbType database, TemporalSemantics semantics)
    {
        var result = Resolve(new("value", type, Format: format));
        Assert.That(result.RuntimeType, Is.EqualTo(runtime));
        Assert.That(result.DatabaseType, Is.EqualTo(database));
        Assert.That(result.Parameters.TemporalSemantics, Is.EqualTo(semantics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Temporal_precision_timezone_and_local_timestamp_are_visible()
    {
        var result = Resolve(new("value", "timestamp", Constraints: [new("precision", 9), new("timezone", true), new("defaultTimezone", "Europe/Brussels")]));
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.DatabaseType, Is.EqualTo(DbType.DateTimeOffset));
        Assert.That(result.Parameters.TemporalPrecision, Is.EqualTo(9));
        Assert.That(result.Parameters.Timezone, Is.EqualTo("Europe/Brussels"));
        Assert.That(result.Diagnostics.Count(d => d.Code == "RepresentationLoss"), Is.EqualTo(2));
        var local = Resolve(new("value", "timestamp", Constraints: [new("timezone", false)]));
        Assert.That(local.RuntimeType, Is.EqualTo(typeof(DateTime)));
        var offsetTime = Resolve(new("value", "time", Constraints: [new("timezone", true)]));
        Assert.That(offsetTime.RuntimeType, Is.Null);
        Assert.That(offsetTime.DatabaseType, Is.Null);
        Assert.That(Resolve(new("value", "time", Format: "%H:%M%z")).RuntimeType, Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Requiredness_and_nullability_are_independent(bool required, bool nullable)
    {
        var result = Resolve(new("value", "integer", Required: required, Nullable: nullable));
        Assert.That(result.Required, Is.EqualTo(required));
        Assert.That(result.Nullable, Is.EqualTo(nullable));
        Assert.That(result.RuntimeType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void Arrays_and_objects_keep_recursive_metadata_and_unsupported_map_is_explicit()
    {
        var field = new DataField("rows", "array", Items: new("row", "object", Children: [new("id", "integer", Format: "u64"), new("data", "map")]));
        var result = Resolve(field);
        Assert.That(result.RuntimeType, Is.EqualTo(typeof(object[])));
        Assert.That(result.DatabaseType, Is.Null);
        Assert.That(result.Items!.Children[0].RuntimeType, Is.EqualTo(typeof(ulong)));
        Assert.That(result.Items.Children[1].RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "UnsupportedType" && d.FieldPath == "rows/items/data"), Is.True);
        Assert.That(Resolve(new("rows", "array")).RuntimeType, Is.Null);
        Assert.That(Resolve(field with { Children = [new("bad", "string")] }).RuntimeType, Is.Null);
    }

    [Test]
    public void Explicit_object_is_distinct_from_unknown_type_fallback_and_physical_type_is_not_guessed()
    {
        Assert.That(Resolve(new("value", "object")).Diagnostics, Is.Empty);
        var unknown = new RuntimeTypeMapper().Resolve(new("value", "mystery", PhysicalType: "timestamp"), new(TypeMappingFallback.Object));
        Assert.That(unknown.RuntimeType, Is.EqualTo(typeof(object)));
        Assert.That(unknown.Diagnostics.Any(d => d.Code == "UnsupportedType"), Is.True);
        Assert.That(Resolve(new("value", null, PhysicalType: "timestamp")).RuntimeType, Is.Null);
    }

    [Test]
    public void Register_and_subclass_overrides_work_in_legacy_and_rich_apis()
    {
        var runtime = new RuntimeTypeMapper();
        var database = new DbTypeMapper();
        runtime.Register("integer", "u64", typeof(string));
        database.Register("integer", "u64", DbType.String);
        var field = new DataField("value", "integer", Format: "u64");
        Assert.That(runtime.Resolve(field, TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(string)));
        Assert.That(database.Resolve(field, TypeMappingOptions.Strict).DatabaseType, Is.EqualTo(DbType.String));
        Assert.That(runtime.Map("integer", "u64"), Is.EqualTo(typeof(string)));
        Assert.That(database.Map("integer", "u64"), Is.EqualTo(DbType.String));
        runtime.Register("custom", typeof(Guid));
        Assert.That(runtime.Resolve(new("id", "custom"), TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(Guid)));
        Assert.That(new CustomMapper().Resolve(new("id", "custom"), TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(Guid)));
        var offsetField = new DataField("date", "datetime", Format: "%Y%z");
        Assert.That(new CustomFormatMapper().Resolve(offsetField, TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(string)));
        Assert.That(new CustomFormatDbMapper().Resolve(offsetField, TypeMappingOptions.Strict).DatabaseType, Is.EqualTo(DbType.String));
        var array = runtime.Resolve(new("ids", "array", Items: new("id", "custom")), TypeMappingOptions.Strict);
        Assert.That(array.RuntimeType, Is.EqualTo(typeof(Guid[])));
    }

    [Test]
    public void Legacy_policy_keeps_reader_and_provisioner_representation_with_diagnostics()
    {
        var field = new DataField("value", "integer", Format: "u64");
        var runtime = new RuntimeTypeMapper().Resolve(field, TypeMappingOptions.LegacyCompatibility);
        var database = new DbTypeMapper().Resolve(field, TypeMappingOptions.LegacyCompatibility);
        Assert.That(runtime.RuntimeType, Is.EqualTo(typeof(int)));
        Assert.That(database.DatabaseType, Is.EqualTo(DbType.Int32));
        Assert.That(runtime.Diagnostics.Any(d => d.Code == "LegacyRepresentation"), Is.True);
        Assert.That(runtime.Parameters.IntegerWidth, Is.EqualTo(64));
    }

    private sealed class CustomMapper : RuntimeTypeMapper
    {
        protected override void Initialize() { base.Initialize(); Register("custom", typeof(Guid)); }
    }

    private sealed class CustomFormatMapper : RuntimeTypeMapper
    {
        protected override bool TryMapFormat(string type, string? format, out Type mapping)
        { mapping = typeof(string); return true; }
    }

    private sealed class CustomFormatDbMapper : DbTypeMapper
    {
        protected override bool TryMapFormat(string type, string? format, out DbType mapping)
        { mapping = DbType.String; return true; }
    }

    private static FieldTypeResolution Resolve(DataField field) => FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
}
