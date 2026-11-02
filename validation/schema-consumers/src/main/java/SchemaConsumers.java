import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashSet;
import org.apache.iceberg.SchemaParser;
import org.apache.iceberg.types.Types;
import org.apache.paimon.schema.TableSchema;
import org.apache.paimon.types.RowType;
import org.apache.paimon.types.ArrayType;
import org.apache.spark.sql.types.DataType;
import org.apache.spark.sql.types.StructType;
import org.apache.hudi.AvroConversionUtils;
import org.apache.spark.sql.delta.schema.SchemaMergingUtils;

/** Actual consumer parser smoke checks for CLI-generated fixtures, without any engine or table startup. */
public class SchemaConsumers {
    static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
    static String read(Path directory, String name) throws Exception {
        return Files.readString(directory.resolve(name));
    }
    public static void main(String[] args) throws Exception {
        Path directory = Path.of(args[0]);
        var iceberg = SchemaParser.fromJson(read(directory, "orders.iceberg.schema.json"));
        check(iceberg.schemaId() == 5, "Iceberg schema ID");
        check(iceberg.columns().size() == 6, "Iceberg ordered fields");
        check(iceberg.columns().get(0).fieldId() == 40, "Iceberg supplied ID");
        check(iceberg.columns().get(0).doc().equals("Order identifier"), "Iceberg description");
        check(iceberg.findType("amount").equals(Types.DecimalType.of(12, 2)), "Iceberg decimal");
        check(iceberg.findType("at").equals(Types.TimestampType.withZone()), "Iceberg instant");
        check(iceberg.findType("tags").asListType().isElementOptional(), "Iceberg list nullability");
        check(iceberg.findType("lookup").asMapType().isValueOptional(), "Iceberg map nullability");
        var icebergIds = new HashSet<Integer>();
        iceberg.columns().forEach(f -> check(icebergIds.add(f.fieldId()), "Iceberg distinct top-level IDs"));
        check(iceberg.findField("tags.element.label") != null, "Iceberg nested row in list");

        var paimon = TableSchema.fromJson(read(directory, "orders.paimon.schema.json"));
        check(paimon.version() == 3 && paimon.id() == 5, "Paimon format/revision IDs");
        check(paimon.fields().size() == 6, "Paimon ordered fields");
        check(paimon.fields().get(0).id() == 40, "Paimon supplied ID");
        check(paimon.highestFieldId() == 40, "Paimon highest nested field ID");
        check(paimon.fields().get(0).description().equals("Order identifier"), "Paimon description");
        check(!paimon.fields().get(0).type().isNullable(), "Paimon required value");
        check(paimon.fields().get(1).type().toString().contains("DECIMAL(12, 2)"), "Paimon decimal");
        check(((ArrayType)paimon.fields().get(4).type()).getElementType().isNullable(), "Paimon element nullability");
        check(((RowType)((ArrayType)paimon.fields().get(4).type()).getElementType()).getFields().get(0).name().equals("label"), "Paimon nested IDs");
        check(paimon.comment().equals("Order records"), "Paimon comment");
        check(paimon.primaryKeys().isEmpty() && paimon.partitionKeys().isEmpty() && paimon.options().isEmpty(), "No inferred Paimon configuration");

        var icebergTime = SchemaParser.fromJson(read(directory, "temporal.iceberg.schema.json"));
        check(icebergTime.findType("local").equals(Types.TimestampType.withoutZone()), "Iceberg local timestamp");
        check(icebergTime.findField("local").isOptional(), "Iceberg nullable timestamp");
        check(icebergTime.findType("clock").equals(Types.TimeType.get()), "Iceberg time");
        check(icebergTime.findType("day").equals(Types.DateType.get()), "Iceberg date");
        var paimonTime = TableSchema.fromJson(read(directory, "temporal.paimon.schema.json"));
        check(paimonTime.fields().get(0).type().toString().equals("TIMESTAMP(3)"), "Paimon local timestamp precision");
        check(paimonTime.fields().get(1).type().toString().equals("TIME(6) NOT NULL"), "Paimon time");
        check(paimonTime.fields().get(2).type().toString().equals("DATE NOT NULL"), "Paimon date");

        var avro = new org.apache.avro.Schema.Parser().parse(read(directory, "orders.hudi.avsc"));
        var hudi = AvroConversionUtils.convertAvroSchemaToStructType(avro);
        check(hudi.fields().length == 6, "Hudi Avro to Spark consumer");
        check(hudi.fields()[1].dataType() instanceof org.apache.spark.sql.types.DecimalType decimal && decimal.precision() == 12 && decimal.scale() == 2, "Hudi decimal");
        check(avro.getFields().stream().noneMatch(f -> f.hasDefaultValue()), "No inferred Avro defaults");

        var delta = (StructType)DataType.fromJson(read(directory, "orders.delta.schema.json"));
        SchemaMergingUtils.checkColumnNameDuplication(delta, "schema export", false);
        check(delta.fields().length == 6, "Delta schemaString consumer");
        check(delta.fields()[2].dataType().typeName().equals("timestamp"), "Delta timestamp");
        check(delta.fields()[1].dataType() instanceof org.apache.spark.sql.types.DecimalType decimal && decimal.precision() == 12 && decimal.scale() == 2, "Delta decimal");
        System.out.println("Iceberg 1.9.2, Paimon 1.2.0, Hudi 1.0.2 and Delta 3.3.0 consumer checks passed.");
    }
}
