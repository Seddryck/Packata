import * as vscode from 'vscode';
import { Target, SchemaRoute } from './generation';
export const schemaRoutes: Record<string, SchemaRoute> = {
  'packata.schema.jsonSchema': { operation: 'export', outputFormat: 'json-schema' },
  'packata.schema.record': { operation: 'generate', target: 'record', language: 'csharp' },
  'packata.schema.datatable': { operation: 'generate', target: 'datatable', language: 'csharp' },
  'packata.schema.polars': { operation: 'generate', target: 'polars', language: 'csharp' }
};
export function available(route: SchemaRoute, targets: Target[]): boolean {
  return targets.some(t => (t.operation ?? 'generate') === route.operation && t.id === (route.outputFormat ?? route.target)
    && (route.operation === 'export' || t.language === route.language) && (!route.dialect || t.dialects?.some(d => d.id === route.dialect)));
}
export async function schemaContexts(targets: Target[]): Promise<void> {
  for (const [id, route] of Object.entries(schemaRoutes)) await vscode.commands.executeCommand('setContext', id, available(route, targets));
  for (const op of ['export', 'generate']) await vscode.commands.executeCommand('setContext', 'packata.schema.' + op, targets.some(t => t.operation === op));
  for (const language of ['csharp', 'python', 'sql']) await vscode.commands.executeCommand('setContext', 'packata.schema.' + language, Object.values(schemaRoutes).some(r => r.language === language && available(r, targets)));
}
