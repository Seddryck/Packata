import * as vscode from 'vscode';
import { Target, SchemaRoute } from './generation';
export const schemaRoutes: Record<string, SchemaRoute> = {
  'packata.schema.pysparkPython': { operation: 'generate', target: 'pyspark', language: 'python' },
  'packata.schema.dataframeCsharp': { operation: 'generate', target: 'dotnet-dataframe', language: 'csharp' },
  'packata.schema.idataviewCsharp': { operation: 'generate', target: 'idataview', language: 'csharp' },
  'packata.schema.pydanticPython': { operation: 'generate', target: 'pydantic', language: 'python' },
  'packata.schema.dataclassPython': { operation: 'generate', target: 'dataclass', language: 'python' },
  'packata.schema.classPython': { operation: 'generate', target: 'class', language: 'python' },
  'packata.schema.classCsharp': { operation: 'generate', target: 'class', language: 'csharp' },
  'packata.schema.sql.ansi': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'ansi' },
  'packata.schema.sql.access': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'access' },
  'packata.schema.sql.db2': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'db2' },
  'packata.schema.sql.duckdb': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'duckdb' },
  'packata.schema.sql.firebird': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'firebird' },
  'packata.schema.sql.mysql': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'mysql' },
  'packata.schema.sql.singlestore': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'singlestore' },
  'packata.schema.sql.oracle': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'oracle' },
  'packata.schema.sql.cockroachdb': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'cockroachdb' },
  'packata.schema.sql.cratedb': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'cratedb' },
  'packata.schema.sql.postgresql': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'postgresql' },
  'packata.schema.sql.snowflake': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'snowflake' },
  'packata.schema.sql.sqlite': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'sqlite' },
  'packata.schema.sql.sqlserver': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'sqlserver' },
  'packata.schema.sql.teradata': { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'teradata' },
  'packata.schema.arrowIpc': { operation: 'export', outputFormat: 'arrow-ipc' },
  'packata.schema.avroIdl': { operation: 'export', outputFormat: 'avro-idl' },
  'packata.schema.avro': { operation: 'export', outputFormat: 'avro' },
  'packata.schema.sparkJson': { operation: 'export', outputFormat: 'spark-json' },
  'packata.schema.jsonSchema': { operation: 'export', outputFormat: 'json-schema' },
  'packata.schema.record': { operation: 'generate', target: 'record', language: 'csharp' },
  'packata.schema.datatable': { operation: 'generate', target: 'datatable', language: 'csharp' },
  'packata.schema.arrowCsharp': { operation: 'generate', target: 'arrow', language: 'csharp' },
  'packata.schema.arrowPython': { operation: 'generate', target: 'arrow', language: 'python' },
  'packata.schema.polars': { operation: 'generate', target: 'polars', language: 'csharp' }
};
export function available(route: SchemaRoute, targets: Target[]): boolean {
  return targets.some(t => (t.operation ?? 'generate') === route.operation && t.id === (route.outputFormat ?? route.target)
    && (route.operation === 'export' || t.language === route.language) && (!route.dialect || t.dialects?.some(d => d.id === route.dialect)));
}
export async function schemaContexts(targets: Target[]): Promise<void> {
  for (const [id, route] of Object.entries(schemaRoutes)) await vscode.commands.executeCommand('setContext', id, available(route, targets));
  for (const op of ['export', 'generate']) {
    const extra = targets.some(t => t.operation === op && (t.dialects?.length
      ? t.dialects.some(d => !Object.values(schemaRoutes).some(r => r.target === t.id && r.language === t.language && r.dialect === d.id))
      : !Object.values(schemaRoutes).some(r => r.operation === op && (r.outputFormat ?? r.target) === t.id && (op === 'export' || r.language === t.language))));
    await vscode.commands.executeCommand('setContext', 'packata.schema.other.' + op, extra);
  }
  for (const op of ['export', 'generate']) await vscode.commands.executeCommand('setContext', 'packata.schema.' + op, targets.some(t => t.operation === op));
  for (const language of ['csharp', 'python', 'sql']) await vscode.commands.executeCommand('setContext', 'packata.schema.' + language, Object.values(schemaRoutes).some(r => r.language === language && available(r, targets)));
}
