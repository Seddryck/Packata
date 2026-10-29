import * as vscode from 'vscode';
import { ContractNode, ContractState, Inspection } from './protocol';
import { ViewMode } from './viewMode';

export interface TreeEntry { node: ContractNode; contract: Inspection; parent?: TreeEntry }

/** Restore independent expansions before revealing the tree's single selection. */
export async function restoreViewState(tree: Pick<ContractTree, 'corresponding'>,
  view: Pick<vscode.TreeView<TreeEntry>, 'reveal'>, expansion: readonly TreeEntry[], selection?: TreeEntry): Promise<void> {
  await Promise.all(expansion.map(previous => {
    const next = tree.corresponding(previous);
    return next ? view.reveal(next, { expand: true, focus: false, select: false }) : undefined;
  }));
  const selected = selection && tree.corresponding(selection);
  if (selected) await view.reveal(selected, { select: true, focus: false });
}

export class ContractTree implements vscode.TreeDataProvider<TreeEntry> {
  private readonly changed = new vscode.EventEmitter<TreeEntry | undefined>();
  readonly onDidChangeTreeData = this.changed.event;
  private roots: TreeEntry[] = [];
  private readonly children = new Map<string, TreeEntry[]>();
  readonly validation = new Map<string, { version: number | null; valid: boolean }>();
  mode: ViewMode = 'canonical';
  private readonly entries: TreeEntry[] = [];
  constructor(readonly state: ContractState) {}
  refresh(): void {
    this.children.clear();
    this.entries.length = 0;
    const collect = (node: ContractNode, contract: Inspection, parent?: TreeEntry): TreeEntry => {
      const entry = { node, contract, parent };
      this.entries.push(entry);
      this.children.set(node.id, node.children.map(child => collect(child, contract, entry)));
      return entry;
    };
    this.roots = this.state.contracts.map(contract => collect(this.projection(contract) ?? {
      id: contract.uri + '#/', label: vscode.Uri.parse(contract.uri).path.split('/').pop() ?? 'Invalid contract',
      kind: 'contract', logicalPath: '/', description: 'Invalid or unsupported descriptor — open source', children: []
    }, contract));
    this.changed.fire(undefined);
  }
  getChildren(entry?: TreeEntry): TreeEntry[] { return entry ? this.children.get(entry.node.id) ?? [] : this.roots; }
  getParent(entry: TreeEntry): TreeEntry | undefined { return entry.parent; }
  root(uri: string): TreeEntry | undefined { return this.roots.find(item => item.contract.uri === uri); }
  private projection(contract: Inspection): ContractNode | undefined {
    if (this.mode === 'native') return contract.nativeTree;
    if (contract.tree || !contract.nativeTree) return contract.tree;
    return { ...contract.nativeTree, description: 'Canonical mapping unavailable', children: [{
      id: contract.uri + '#/source', label: 'Source properties (including unmapped)', kind: 'group',
      logicalPath: '/source', children: contract.nativeTree.children
    }] };
  }
  corresponding(entry: TreeEntry): TreeEntry | undefined {
    const candidates = this.entries.filter(value => value.contract.uri === entry.contract.uri);
    for (let current: TreeEntry | undefined = entry; current; current = current.parent) {
      const target = current.node;
      const match = candidates.find(value => target.sourcePath !== undefined && value.node.sourcePath === target.sourcePath)
        ?? candidates.find(value => value.node.id === target.id);
      if (match) return match;
    }
    return this.root(entry.contract.uri);
  }
  getTreeItem(entry: TreeEntry): vscode.TreeItem {
    const { node, contract } = entry;
    const item = new vscode.TreeItem(node.label, node.children.length ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
    item.id = node.id; item.description = node.description; item.contextValue = node.kind;
    item.tooltip = `${node.label}\n${node.description ?? ''}\n${vscode.Uri.parse(contract.uri).fsPath}\n${node.logicalPath}\nSource: ${node.sourcePath ?? 'unmapped'}`;
    const icons: Record<string, string> = { contract: 'file-code', asset: 'database', field: 'symbol-field', constraint: 'symbol-ruler', quality: 'checklist', endpoint: 'plug', sla: 'clock', governance: 'organization', group: 'folder' };
    item.iconPath = new vscode.ThemeIcon(icons[node.kind] ?? 'symbol-property');
    if (node.kind === 'contract') this.decorateContract(item, entry);
    // Group nodes expand on click; only syntax-backed concrete nodes navigate.
    if (node.kind !== 'group' && node.location) item.command = { command: 'packata.revealSource', title: 'Reveal Source', arguments: [entry] };
    return item;
  }
  private decorateContract(item: vscode.TreeItem, entry: TreeEntry): void {
    const { node, contract } = entry;
    const validation = this.validation.get(contract.uri);
    const current = validation?.version === contract.version ? validation : undefined;
    item.description = [node.description, this.validationLabel(current)].filter(Boolean).join(' · ');
    item.iconPath = new vscode.ThemeIcon(this.contractIcon(contract, current));
    if (contract.diagnostics.length) item.tooltip += '\n' + contract.diagnostics.map(value => value.message).join('\n');
  }
  private validationLabel(validation?: { valid: boolean }): string {
    if (!validation) return 'Not validated';
    return validation.valid ? 'Validated' : 'Invalid';
  }
  private contractIcon(contract: Inspection, validation?: { valid: boolean }): string {
    if (contract.diagnostics.some(value => value.severity === 'Error') || validation?.valid === false) return 'error';
    if (contract.diagnostics.some(value => value.severity === 'Warning')) return 'warning';
    if (validation?.valid) return 'pass';
    return 'file-code';
  }
  dispose(): void { this.changed.dispose(); }
}
