import * as vscode from 'vscode';
import { ContractNode, ContractState, Inspection } from './protocol';

export interface TreeEntry { node: ContractNode; contract: Inspection; parent?: TreeEntry }
export class ContractTree implements vscode.TreeDataProvider<TreeEntry> {
  private readonly changed = new vscode.EventEmitter<TreeEntry | undefined>();
  readonly onDidChangeTreeData = this.changed.event;
  private roots: TreeEntry[] = [];
  private readonly children = new Map<string, TreeEntry[]>();
  readonly validation = new Map<string, { version: number | null; valid: boolean }>();
  constructor(readonly state: ContractState) {}
  refresh(): void {
    this.children.clear();
    const collect = (node: ContractNode, contract: Inspection, parent?: TreeEntry): TreeEntry => {
      const entry = { node, contract, parent };
      this.children.set(node.id, node.children.map(child => collect(child, contract, entry)));
      return entry;
    };
    this.roots = this.state.contracts.map(contract => collect(contract.tree ?? {
      id: contract.uri + '#/', label: vscode.Uri.parse(contract.uri).path.split('/').pop() ?? 'Invalid contract',
      kind: 'contract', logicalPath: '/', description: 'Invalid or unsupported descriptor — open source', children: []
    }, contract));
    this.changed.fire(undefined);
  }
  getChildren(entry?: TreeEntry): TreeEntry[] { return entry ? this.children.get(entry.node.id) ?? [] : this.roots; }
  getParent(entry: TreeEntry): TreeEntry | undefined { return entry.parent; }
  root(uri: string): TreeEntry | undefined { return this.roots.find(item => item.contract.uri === uri); }
  getTreeItem(entry: TreeEntry): vscode.TreeItem {
    const { node, contract } = entry;
    const item = new vscode.TreeItem(node.label, node.children.length ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
    item.id = node.id; item.description = node.description; item.contextValue = node.kind;
    item.tooltip = `${node.label}\n${node.description ?? ''}\n${vscode.Uri.parse(contract.uri).fsPath}\n${node.logicalPath}`;
    const icons: Record<string, string> = { contract: 'file-code', asset: 'database', field: 'symbol-field', constraint: 'symbol-ruler', quality: 'checklist', endpoint: 'plug', sla: 'clock', governance: 'organization', group: 'folder' };
    item.iconPath = new vscode.ThemeIcon(icons[node.kind] ?? 'symbol-property');
    if (node.kind === 'contract') {
      const validation = this.validation.get(contract.uri);
      const errors = contract.diagnostics.some(value => value.severity === 'Error');
      const warnings = contract.diagnostics.some(value => value.severity === 'Warning');
      item.description = [node.description, validation?.version === contract.version ? validation.valid ? 'Validated' : 'Invalid' : 'Not validated'].filter(Boolean).join(' · ');
      if (errors || (validation?.version === contract.version && !validation.valid)) item.iconPath = new vscode.ThemeIcon('error');
      else if (warnings) item.iconPath = new vscode.ThemeIcon('warning');
      else if (validation?.version === contract.version && validation.valid) item.iconPath = new vscode.ThemeIcon('pass');
      if (contract.diagnostics.length) item.tooltip += '\n' + contract.diagnostics.map(value => value.message).join('\n');
    }
    // Group nodes expand on click; only syntax-backed concrete nodes navigate.
    if (node.kind !== 'group' && node.location) item.command = { command: 'packata.revealSource', title: 'Reveal Source', arguments: [entry] };
    return item;
  }
  dispose(): void { this.changed.dispose(); }
}
