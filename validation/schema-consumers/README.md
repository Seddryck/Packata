# Native consumer checks

Requires Java 17+ and Maven 3.9+. Dependencies are test-only and never enter Packata Core.
Generate artifacts from the checked-in ODCS fixture with a built Packata CLI, then run:

```powershell
$cli = 'src/Packata.Cli/bin/Debug/net10.0/packata.dll'
$source = 'examples/lakehouse/contract.odcs.yaml'
New-Item -ItemType Directory -Force .artifacts/lakehouse | Out-Null
dotnet $cli schema export $source --output-format iceberg-json -o .artifacts/lakehouse/orders.iceberg.schema.json
dotnet $cli schema export $source --output-format paimon-json -o .artifacts/lakehouse/orders.paimon.schema.json
dotnet $cli schema export $source --output-format avro --profile hudi -o .artifacts/lakehouse/orders.hudi.avsc
dotnet $cli schema export $source --output-format spark-json --profile delta -o .artifacts/lakehouse/orders.delta.schema.json
dotnet $cli schema export examples/lakehouse/temporal.odcs.yaml --output-format iceberg-json -o .artifacts/lakehouse/temporal.iceberg.schema.json
dotnet $cli schema export examples/lakehouse/temporal.odcs.yaml --output-format paimon-json -o .artifacts/lakehouse/temporal.paimon.schema.json
mvn -f validation/schema-consumers/pom.xml compile exec:java -Dexec.mainClass=SchemaConsumers "-Dexec.args=$((Resolve-Path .artifacts/lakehouse).Path)"
```

The smoke check parses CLI output with the pinned native APIs, including Hudi's Avro-to-Spark conversion and Delta's schema collision validator. It starts no Spark context, reads no rows, and creates no tables. NUnit covers rejected domains and ID/key validation separately.
