const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const esbuild = require('esbuild');

function load(file, mocks = {}) {
  const result = esbuild.buildSync({ entryPoints: [path.join(__dirname, '../src', file)], bundle: true,
    platform: 'node', format: 'cjs', write: false, external: Object.keys(mocks) });
  const module = { exports: {} };
  vm.runInNewContext(result.outputFiles[0].text, { module, exports: module.exports,
    require: name => mocks[name] ?? require(name), process, console, setTimeout, clearTimeout });
  return module.exports;
}
const protocol = load('protocol.ts');
const inspection = (version = 1) => ({ schemaVersion: 1, uri: 'file:///contract.yaml', version,
  baseUri: 'file:///', recognized: true, format: 'odcs', diagnostics: [], tree: {
    id: 'file:///contract.yaml#/', label: 'Customers', kind: 'contract', logicalPath: '/', description: 'ODCS', children: []
  } });
const discovery = (...contracts) => ({ schemaVersion: 1, contracts });

test('packaging resolves an absolute SDK executable without searching PATH', async () => {
  const { resolveDotnet } = await import('../scripts/package.mjs');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'packata sdk '));
  try {
    const executable = path.join(root, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
    fs.writeFileSync(executable, 'fixture');
    assert.equal(resolveDotnet({ DOTNET_ROOT: root, PATH: '/untrusted' }), fs.realpathSync(executable));
    assert.throws(() => resolveDotnet({ DOTNET_ROOT: 'relative-sdk' }));
    assert.throws(() => resolveDotnet({ DOTNET_ROOT: path.join(root, 'missing') }));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('packaging accepts executable apphosts while rejecting group/other write permissions', async () => {
  const { validateExecutableMode } = await import('../scripts/package.mjs');
  validateExecutableMode(0o755);
  validateExecutableMode(0o500);
  for (const mode of [0o644, 0o777, 0o775, 0o757]) assert.throws(() => validateExecutableMode(mode));
});

test('requires supported Packata capability and payload versions', () => {
  assert.throws(() => protocol.requireCapabilities(['packata.contract.inspect']));
  protocol.requireCapabilities(['packata.protocol.v1', 'packata.contract.inspect', 'packata.contract.validate', 'packata.workspace.discover']);
  assert.throws(() => protocol.requireSchema({ schemaVersion: 2 }));
  assert.throws(() => protocol.requireDiscovery({ schemaVersion: 1, contracts: [{}] }));
  protocol.requireDiscovery(discovery(inspection()));
});
test('rejects superseded and unsaved-version mismatched responses', () => {
  const state = new protocol.ContractState();
  const old = state.begin();
  const current = state.begin();
  assert.equal(state.apply(old, discovery(inspection()), new Map()), false);
  assert.equal(state.apply(current, discovery(inspection(1)), new Map([['file:///contract.yaml', 2]])), false);
  assert.equal(state.apply(current, discovery(inspection(2)), new Map([['file:///contract.yaml', 2]])), true);
  assert.equal(state.contracts[0].version, 2);
});
test('clear invalidates outstanding requests across disconnect and restart', () => {
  const state = new protocol.ContractState(); const old = state.begin();
  state.clear();
  assert.equal(state.apply(old, discovery(inspection()), new Map()), false);
  assert.equal(state.contracts.length, 0);
  assert.equal(state.apply(state.begin(), discovery(inspection()), new Map()), true);
});
test('keeps multiple workspace contracts and permits disk-only document versions', () => {
  const a = inspection(null), b = { ...inspection(null), uri: 'file:///other/contract.yaml' };
  const state = new protocol.ContractState();
  assert.equal(state.apply(state.begin(), discovery(a, b), new Map()), true);
  assert.equal(state.contracts.length, 2);
});
test('executable resolution supports bundled and explicit absolute paths containing spaces', () => {
  const { resolveServer } = load('serverPath.ts');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'packata client '));
  try {
    const server = path.join(root, 'server'); fs.mkdirSync(server);
    fs.writeFileSync(path.join(server, 'Packata.LanguageServer.exe'), 'fixture');
    assert.equal(resolveServer(root, '', 'win32'), path.join(server, 'Packata.LanguageServer.exe'));
    assert.equal(resolveServer(root, path.join(server, 'Packata.LanguageServer.exe')), path.join(server, 'Packata.LanguageServer.exe'));
    assert.throws(() => resolveServer(root, 'relative.exe'));
    assert.throws(() => resolveServer(root, path.join(root, 'missing')));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

const vscode = {
  EventEmitter: class { event() {} fire() {} dispose() {} },
  TreeItem: class { constructor(label, collapsibleState) { this.label = label; this.collapsibleState = collapsibleState; } },
  ThemeIcon: class { constructor(id) { this.id = id; } },
  TreeItemCollapsibleState: { None: 0, Collapsed: 1 },
  Uri: { parse: value => ({ path: new URL(value).pathname, fsPath: new URL(value).pathname }) }
};
test('tree inspection is not marked as validated, and versions invalidate explicit status', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(inspection()), new Map());
  const tree = new ContractTree(state); tree.refresh();
  let root = tree.getChildren()[0];
  assert.match(tree.getTreeItem(root).description, /Not validated/);
  tree.validation.set(root.contract.uri, { version: 1, valid: true });
  assert.match(tree.getTreeItem(root).description, /Validated/);
  assert.equal(tree.getTreeItem(root).iconPath.id, 'pass');
  state.apply(state.begin(), discovery(inspection(2)), new Map()); tree.refresh();
  root = tree.getChildren()[0];
  assert.match(tree.getTreeItem(root).description, /Not validated/);
});
test('only syntax-backed concrete nodes reveal source; groups keep native expansion', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const item = inspection();
  const location = { uri: item.uri, range: { start: { line: 0, character: 0 }, end: { line: 0, character: 5 } } };
  item.tree.children = [{ id: 'columns', label: 'Columns', kind: 'group', logicalPath: '/columns', location, children: [
    { id: 'id', label: 'id', kind: 'field', logicalPath: '/columns/id', location, children: [] },
    { id: 'unmapped', label: 'unmapped', kind: 'field', logicalPath: '/columns/unmapped', children: [] }
  ] }];
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(item), new Map());
  const tree = new ContractTree(state); tree.refresh();
  const group = tree.getChildren(tree.getChildren()[0])[0];
  assert.equal(tree.getTreeItem(group).command, undefined);
  const fields = tree.getChildren(group);
  assert.equal(tree.getTreeItem(fields[0]).command.command, 'packata.revealSource');
  assert.equal(tree.getTreeItem(fields[1]).command, undefined);
  assert.equal(tree.getParent(fields[0]), group);
  tree.refresh();
  assert.equal(tree.getTreeItem(tree.getChildren()[0]).id, item.tree.id);
});
test('invalid/partial descriptors stay visible and mapping warnings remain explicit', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const broken = inspection(); broken.tree = null; broken.diagnostics = [{ severity: 'Error', message: 'Invalid descriptor' }];
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(broken), new Map());
  const tree = new ContractTree(state); tree.refresh();
  const root = tree.getTreeItem(tree.getChildren()[0]);
  assert.equal(root.iconPath.id, 'error'); assert.match(root.tooltip, /Invalid descriptor/);
});
test('manifest contributes native navigation/context menus without unsupported preview/execution commands', () => {
  const manifest = require('../package.json');
  assert.equal(manifest.contributes.views.packata[0].id, 'packata.contracts');
  assert.equal(manifest.contributes.viewsContainers.activitybar[0].id, 'packata');
  assert.equal(manifest.capabilities.untrustedWorkspaces.supported, false);
  assert.ok(manifest.contributes.menus['view/item/context'].some(item => item.command === 'packata.validate'));
  assert.ok(!manifest.contributes.commands.some(item => /preview|executeQuality/.test(item.command)));
});
