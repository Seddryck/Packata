import type { PreviewTarget } from './preview';
export interface Position { line: number; character: number }
export interface Location { uri: string; range: { start: Position; end: Position } }
export interface ContractNode {
  id: string; label: string; kind: string; logicalPath: string; description?: string;
  location?: Location; sourcePath?: string; children: ContractNode[];
}
export interface Inspection {
  schemaVersion: number; uri: string; version: number | null; baseUri: string; recognized: boolean;
  format?: string; contract?: unknown; diagnostics: { code?: string; severity: string; sourcePath?: string; message: string }[]; tree?: ContractNode; nativeTree?: ContractNode;
  previewTargets?: PreviewTarget[];
}
export interface Discovery { schemaVersion: number; contracts: Inspection[] }
export interface Validation { schemaVersion: number; uri: string; version: number | null; valid: boolean; diagnostics: unknown[] }

export function requireSchema(value: unknown): asserts value is { schemaVersion: number } {
  if (!value || typeof value !== 'object' || !('schemaVersion' in value) || value.schemaVersion !== 1) {
    throw new Error('Unsupported Packata response. Update the extension and language server together.');
  }
}
export function requireCapabilities(commands: readonly string[] | undefined): void {
  if (!['packata.protocol.v1', 'packata.contract.inspect', 'packata.contract.validate', 'packata.workspace.discover'].every(command => commands?.includes(command))) {
    throw new Error('The server does not support Packata inspection, validation and discovery. Update the server.');
  }
}
function node(value: unknown): value is ContractNode {
  if (!value || typeof value !== 'object') return false;
  const item = value as ContractNode;
  return typeof item.id === 'string' && typeof item.label === 'string' && typeof item.kind === 'string'
    && typeof item.logicalPath === 'string' && Array.isArray(item.children) && item.children.every(node);
}
export function requireDiscovery(value: unknown): asserts value is Discovery {
  requireSchema(value);
  const result = value as Discovery;
  if (!Array.isArray(result.contracts) || result.contracts.some(item => item.schemaVersion !== 1
    || typeof item.uri !== 'string' || (item.version !== null && !Number.isInteger(item.version))
    || !Array.isArray(item.diagnostics) || (item.tree != null && !node(item.tree))
    || (item.nativeTree != null && !node(item.nativeTree)))) {
    throw new Error('Malformed Packata discovery response. Restart or update the language server.');
  }
}

/** Epochs invalidate cancelled work even when a transport finishes after cancellation. */
export class ContractState {
  private epoch = 0;
  contracts: Inspection[] = [];
  begin(): number { return ++this.epoch; }
  apply(epoch: number, response: Discovery, versions: ReadonlyMap<string, number>): boolean {
    requireDiscovery(response);
    if (epoch !== this.epoch || response.contracts.some(item => versions.has(item.uri) && versions.get(item.uri) !== item.version)) return false;
    this.contracts = response.contracts;
    return true;
  }
  clear(): void { this.begin(); this.contracts = []; }
}
