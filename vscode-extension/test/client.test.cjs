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

function previewHarness(options = {}) {
  const errors = [], panels = [], calls = [];
  const target = { sourcePath: '/resources/0', bindingIndex: 0, label: 'orders', supported: true, selectionKey: 'opaque-key' };
  const inspected = { ...inspection(), previewTargets: options.targets ?? [target] };
  const token = { isCancellationRequested: false };
  const mock = {
    window: {
      activeTextEditor: { document: { uri: { toString: () => inspected.uri } } },
      showErrorMessage: message => errors.push(message), showWarningMessage: message => errors.push(message),
      showQuickPick: async items => options.cancelSelection ? undefined : items.at(-1),
      showInputBox: async prompt => { calls.push({ prompt }); return Object.hasOwn(options, 'input') ? options.input : '20'; },
      withProgress: async (settings, work) => { assert.equal(settings.cancellable, true); return work({}, token); },
      createWebviewPanel: (kind, title, position, settings) => { const panel = { webview: {}, settings }; panels.push(panel); return panel; }
    },
    workspace: { textDocuments: [] }, ProgressLocation: { Notification: 15 }, ViewColumn: { Beside: 2 }
  };
  const preview = load('preview.ts', { vscode: mock });
  const request = async (command, args, cancellation) => {
    calls.push({ command, args, cancellation });
    if (command === 'packata.contract.inspect') return inspected;
    if (options.cancelRequest) { token.isCancellationRequested = true; throw new Error('private-signature'); }
    return { schemaVersion: 1, uri: inspected.uri, version: 1, limit: args.limit, columns: [{ name: 'id', type: 'integer' }],
      rows: [[1]], ...(options.result ?? {}) };
  };
  return { preview, request, calls, errors, panels, target };
}

test('preview validates positive integer limits and escapes all table contents without scripts', () => {
  const { preview } = previewHarness();
  for (const invalid of ['', '0', '-1', '1.5', 'NaN', '1e3', '2147483648']) assert.ok(preview.rowLimitError(invalid));
  for (const valid of ['1', '20', '123']) assert.equal(preview.rowLimitError(valid), undefined);
  const html = preview.previewHtml({ rows: [['<script>alert(1)</script>']], columns: [{ name: '<img>', type: 'string' }], limit: 1 });
  assert.ok(!html.includes('<script>')); assert.ok(!html.includes('<img>'));
  assert.ok(html.includes('&lt;script&gt;')); assert.ok(html.includes("default-src 'none'"));
});

test('preview prompts with default 20 before reading and passes the selected binding and limit', async () => {
  const harness = previewHarness({ input: '3' });
  await harness.preview.previewResource(harness.request);
  assert.equal(harness.calls[1].prompt.value, '20');
  const read = harness.calls[2];
  assert.equal(read.command, 'packata.resource.preview');
  assert.equal(read.args.limit, 3); assert.equal(read.args.bindingIndex, 0);
  assert.equal(read.args.selectionKey, 'opaque-key'); assert.ok(read.cancellation);
  assert.equal(harness.panels.length, 1); assert.equal(harness.panels[0].settings.enableScripts, false);
});

test('preview rejects invalid input, canceled prompts and unsupported direct invocation without resource reads', async () => {
  for (const options of [{ input: '0' }, { input: undefined }, { targets: [{ sourcePath: '/resources/0', supported: false, reason: 'Unsupported resource.' }] }]) {
    const harness = previewHarness(options);
    await harness.preview.previewResource(harness.request);
    assert.ok(!harness.calls.some(call => call.command === 'packata.resource.preview'));
    assert.equal(harness.panels.length, 0);
  }
});

test('preview requires explicit selection between bindings and does not switch a selected unsupported asset', async () => {
  const targets = [0, 1].map(bindingIndex => ({ sourcePath: '/resources/0', bindingIndex, label: 'orders', supported: true }));
  const selected = previewHarness({ targets });
  await selected.preview.previewResource(selected.request);
  assert.equal(selected.calls.find(call => call.command === 'packata.resource.preview').args.bindingIndex, 1);
  const canceled = previewHarness({ targets, cancelSelection: true });
  await canceled.preview.previewResource(canceled.request);
  assert.ok(!canceled.calls.some(call => call.command === 'packata.resource.preview'));
  const other = previewHarness({ targets: [...targets, { sourcePath: '/resources/1', supported: false, reason: 'Unsupported resource.' }] });
  await other.preview.previewResource(other.request, { contract: inspection(), node: { kind: 'asset', sourcePath: '/resources/1' } });
  assert.ok(!other.calls.some(call => call.command === 'packata.resource.preview'));
  assert.equal(other.errors[0], 'Unsupported resource.');
});

test('preview cancellation is quiet and sanitized server failures do not create a table', async () => {
  const canceled = previewHarness({ cancelRequest: true });
  await canceled.preview.previewResource(canceled.request);
  assert.equal(canceled.errors.length, 0); assert.equal(canceled.panels.length, 0);
  const failed = previewHarness({ result: { diagnostic: { message: 'Unable to parse the resource.' } } });
  await failed.preview.previewResource(failed.request);
  assert.equal(failed.errors[0], 'Unable to parse the resource.'); assert.equal(failed.panels.length, 0);
});

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
  assert.match(tree.getTreeItem(root).description, /Outdated/);
});

const validationModel = load('validation.ts');
const validationOptions = (format = 'odcs') => ({ schemaVersion: 1, format, validators: [
  { id: 'schema', label: 'Schema', description: 'Descriptor, not dataset records', formats: ['odcs', 'datapackage'], blockedBySelected: [] },
  { id: 'semantic', label: 'Semantic', description: 'Consistency; blocked if selected schema fails', formats: ['odcs', 'datapackage'], blockedBySelected: ['schema'] }
] });

test('validation choices persist per workspace and format without enabling future validators', async () => {
  const values = new Map();
  const storage = { get: key => values.get(key), update: async (key, value) => values.set(key, value) };
  await new validationModel.ValidationPreference(storage).set('odcs', ['schema']);
  const restored = new validationModel.ValidationPreference(storage);
  assert.deepEqual(Array.from(restored.get('odcs')), ['schema']);
  assert.equal(restored.get('datapackage'), undefined);
  await assert.rejects(() => restored.set('odcs', []));
  const options = validationOptions();
  options.validators.push({ id: 'organization', label: 'Organization policy', description: 'Policy', formats: ['odcs'], blockedBySelected: [] });
  assert.deepEqual(Array.from(validationModel.initialSelection(options)), ['schema', 'semantic']);
  assert.deepEqual(Array.from(validationModel.initialSelection(options, restored.get('odcs'))), ['schema']);
  assert.equal(validationModel.scopeLabel(options, ['organization']), 'Organization policy');
  assert.equal(validationModel.selectionAvailable(['removed'], options), false);
  assert.equal(validationModel.selectionAvailable([], options), false);
  assert.equal(new validationModel.ValidationPreference({ get: () => undefined }).get('odcs'), undefined);
});

test('partial validation success and incomplete states have explicitly scoped summaries', () => {
  const report = { checks: [ { id: 'schema', label: 'Schema', state: 'Passed', errors: 0 },
    { id: 'semantic', label: 'Semantic', state: 'NotSelected', errors: 0 } ] };
  assert.equal(validationModel.validationSummary(report), 'Schema checks passed · Semantic not run');
  for (const state of ['Blocked', 'Cancelled', 'ExecutionError']) {
    report.checks[0].state = state;
    assert.doesNotMatch(validationModel.validationSummary(report), /checks passed|Document valid/);
  }
  assert.equal(validationModel.checkLabel({ state: 'Failed', errors: 2 }), '2 errors');
});

function validationUiFixture(saved, catalog = validationOptions()) {
  class Uri {
    constructor(value) { this.value = value; this.scheme = 'file'; this.path = '/orders.odcs.yaml'; this.fsPath = this.path; }
    static parse(value) { return new Uri(value); }
    toString() { return this.value; }
  }
  const uri = Uri.parse('file:///orders.odcs.yaml');
  const document = { uri, version: 1 };
  const values = new Map(saved ? [['validationChecks.v1.odcs', saved]] : []);
  const picks = [], calls = [], messages = [], summaries = [], commands = new Map();
  const controls = { cancelProgress: false, failValidation: false, profile: undefined };
  const disposable = () => ({ dispose() {} });
  const status = { show() {}, hide() {}, dispose() {} };
  const mock = { Uri, StatusBarAlignment: { Right: 1 }, ProgressLocation: { Notification: 1 },
    CancellationTokenSource: class {
      token = { isCancellationRequested: false, onCancellationRequested: disposable };
      cancel() { this.token.isCancellationRequested = true; }
      dispose() {}
    },
    commands: { registerCommand: (name, handler) => { commands.set(name, handler); return disposable(); } },
    workspace: { textDocuments: [document], openTextDocument: async () => document, asRelativePath: () => uri.path },
    window: { activeTextEditor: { document }, onDidChangeActiveTextEditor: disposable,
      createStatusBarItem: () => status,
      createOutputChannel: () => ({ clear: () => summaries.length = 0, appendLine: text => summaries.push(text), show() {}, dispose() {} }),
      showWarningMessage: async text => messages.push(text), showInformationMessage: async text => messages.push(text), showErrorMessage: async text => messages.push(text),
      withProgress: async (_, work) => work({}, { isCancellationRequested: controls.cancelProgress,
        onCancellationRequested: handler => { if (controls.cancelProgress) handler(); return disposable(); } }),
      createQuickPick: () => {
        const picker = { items: [], selectedItems: [], show() {}, dispose() {},
          onDidAccept(handler) { this.accept = handler; return disposable(); },
          onDidHide(handler) { this.hide = handler; return disposable(); } };
        picks.push(picker); return picker;
      }
    } };
  const tree = { validation: new Map(), refresh() {} };
  const request = async (command, args) => {
    calls.push({ command, args });
    if (command === 'packata.validation.options') return catalog;
    if (controls.failValidation) throw new Error('Sensitive transport payload');
    return { schemaVersion: 1, uri: uri.toString(), version: 1, valid: true, format: 'odcs', diagnostics: [], profile: controls.profile,
      checks: catalog.validators.map(option => ({ id: option.id, label: option.label,
        state: args.checks.includes(option.id) ? 'Passed' : 'NotSelected', errors: 0 })) };
  };
  const { ValidationUi } = load('validationUi.ts', { vscode: mock });
  const ui = new ValidationUi({ workspaceState: { get: key => values.get(key), update: async (key, value) => values.set(key, value) } }, request, tree);
  return { ui, picks, calls, values, messages, summaries, tree, uri, status, document, commands, controls };
}
const tick = () => new Promise(resolve => setImmediate(resolve));

test('validation output identifies the exact profile returned by the server', async () => {
  const f = validationUiFixture(['schema']);
  f.controls.profile = { uri: 'https://example.invalid/profile.json', label: 'Recipe profile', format: 'datapackage' };
  await f.ui.run(undefined, false);
  assert.ok(f.summaries.includes('Profile: Recipe profile (https://example.invalid/profile.json)'));
  f.ui.dispose();
});

test('native picker cancellation and empty acceptance never run or change remembered scope', async () => {
  const f = validationUiFixture(['semantic']);
  const pending = f.ui.run(undefined, true); await tick();
  const picker = f.picks[0];
  assert.equal(picker.canSelectMany, true);
  assert.match(picker.title, /orders.*ODCS/);
  assert.equal(picker.selectedItems[0].validator.id, 'semantic');
  picker.selectedItems = []; picker.accept(); await tick();
  assert.equal(f.calls.filter(call => call.command === 'packata.contract.validate').length, 0);
  assert.match(picker.title, /at least one/);
  picker.hide(); await pending;
  assert.deepEqual(f.values.get('validationChecks.v1.odcs'), ['semantic']);
  assert.match(f.summaries.join('\n'), /Cancelled/);
  f.ui.dispose();
});

test('confirmed schema selection controls execution, summary and direct rerun tooltip', async () => {
  const f = validationUiFixture();
  const pending = f.ui.run(undefined, true); await tick();
  assert.deepEqual(Array.from(f.picks[0].selectedItems, item => item.validator.id), ['schema', 'semantic']);
  f.picks[0].selectedItems = [f.picks[0].items[0]]; f.picks[0].accept(); await pending;
  assert.deepEqual(Array.from(f.calls.find(call => call.command === 'packata.contract.validate').args.checks), ['schema']);
  assert.match(f.messages.join('\n'), /Schema checks passed · Semantic not run/);
  assert.match(f.status.tooltip, /Validate: Schema/);
  await f.commands.get('packata.runLastValidation')();
  assert.equal(f.picks.length, 1);
  f.document.version = 2; f.ui.markOutdated(f.uri);
  assert.equal(f.tree.validation.get(f.uri.toString()).outdated, true);
  assert.match(f.status.tooltip, /Outdated/);
  f.ui.dispose();
});

test('unavailable remembered checks require confirmation rather than silently running extra checks', async () => {
  const f = validationUiFixture(['organization']);
  const pending = f.ui.run(undefined, false); await tick();
  assert.match(f.messages[0], /unavailable/);
  assert.equal(f.picks.length, 1);
  assert.equal(f.calls.filter(call => call.command === 'packata.contract.validate').length, 0);
  f.picks[0].hide(); await pending;
  assert.deepEqual(f.values.get('validationChecks.v1.odcs'), ['organization']);
  f.ui.dispose();
});

test('a third catalog validator gets a checkbox, execution scope and summary without changing UI', async () => {
  const catalog = validationOptions();
  catalog.validators.push({ id: 'organization', label: 'Organization policy', description: 'Organization rules', formats: ['odcs'], blockedBySelected: [] });
  const f = validationUiFixture(['schema'], catalog);
  const pending = f.ui.run(undefined, true); await tick();
  assert.equal(f.picks[0].items.length, 3);
  assert.deepEqual(Array.from(f.picks[0].selectedItems, item => item.validator.id), ['schema']);
  f.picks[0].selectedItems = [f.picks[0].items[2]]; f.picks[0].accept(); await pending;
  assert.deepEqual(Array.from(f.calls.find(call => call.command === 'packata.contract.validate').args.checks), ['organization']);
  assert.match(f.messages.join('\n'), /Organization policy checks passed/);
  assert.match(f.messages.join('\n'), /Schema not run/);
  f.ui.dispose();
});

test('execution cancellation is distinct from transport errors and never exposes payloads', async () => {
  const cancelled = validationUiFixture(['schema']);
  cancelled.controls.cancelProgress = true;
  await cancelled.ui.run(undefined, false);
  assert.match(cancelled.tree.validation.get(cancelled.uri.toString()).summary, /Cancelled/);
  assert.doesNotMatch(cancelled.summaries.join('\n'), /Execution error/);
  cancelled.ui.dispose();
  const failed = validationUiFixture(['semantic']);
  failed.controls.failValidation = true;
  await failed.ui.run(undefined, false);
  assert.match(failed.tree.validation.get(failed.uri.toString()).summary, /Execution error/);
  assert.doesNotMatch(failed.messages.join('\n') + failed.summaries.join('\n'), /Sensitive transport payload/);
  failed.ui.dispose();
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
test('manifest contributes native navigation and gates resource preview to supported assets', () => {
  const manifest = require('../package.json');
  assert.equal(manifest.contributes.views.packata[0].id, 'packata.contracts');
  assert.equal(manifest.contributes.viewsContainers.activitybar[0].id, 'packata');
  assert.equal(manifest.capabilities.untrustedWorkspaces.supported, false);
  assert.ok(manifest.contributes.menus['view/item/context'].some(item => item.command === 'packata.validate'));
  assert.ok(!manifest.contributes.commands.some(item => /executeQuality/.test(item.command)));
  assert.ok(manifest.contributes.commands.some(item => item.command === 'packata.preview'));
  assert.equal(manifest.contributes.menus['view/item/context'].find(item => item.command === 'packata.preview').when,
    'view == packata.contracts && viewItem == previewAsset');
});

test('both tree projections activate preview only for metadata-approved resource assets', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const contract = inspection();
  contract.previewTargets = [{ sourcePath: '/resources/0', supported: true }, { sourcePath: '/resources/1', supported: false }];
  const assets = [0, 1].map(index => ({ id: 'asset-' + index, label: 'asset', kind: 'asset',
    logicalPath: '/assets/' + index, sourcePath: '/resources/' + index, children: [] }));
  contract.tree.children = assets; contract.nativeTree = { ...contract.tree, id: 'native-root', children: assets };
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(contract), new Map());
  const tree = new ContractTree(state);
  for (const mode of ['native', 'canonical']) {
    tree.mode = mode; tree.refresh();
    const children = tree.getChildren(tree.getChildren()[0]);
    assert.equal(tree.getTreeItem(children[0]).contextValue, 'previewAsset');
    assert.equal(tree.getTreeItem(children[1]).contextValue, 'asset');
  }
});

test('workspace preference defaults to canonical and survives recreation without crossing workspaces', async () => {
  const { ViewPreference } = load('viewMode.ts');
  const values = new Map();
  const storage = { get: key => values.get(key), update: async (key, value) => { values.set(key, value); } };
  const preference = new ViewPreference(storage);
  assert.equal(preference.mode, 'canonical');
  await preference.set('native');
  assert.equal(new ViewPreference(storage).mode, 'native');
  assert.equal(new ViewPreference({ get: () => undefined }).mode, 'canonical');
  await preference.set('canonical');
  assert.equal(new ViewPreference(storage).mode, 'canonical');
});

test('switches all mixed-format roots and resolves selection/expansion by source or nearest parent', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const a = inspection(), b = { ...inspection(), uri: 'file:///other/datapackage.json', format: 'datapackage' };
  b.tree = { ...b.tree, id: b.uri + '#/' };
  const location = { uri: a.uri, range: { start: { line: 4, character: 2 }, end: { line: 4, character: 8 } } };
  a.tree.sourcePath = '';
  a.tree.children = [{ id: 'asset', label: 'Customers', kind: 'asset', logicalPath: '/assets/customers', sourcePath: '/schema/0', location, children: [
    { id: 'synthetic', label: 'Constraints', kind: 'group', logicalPath: '/constraints', children: [] }
  ] }];
  a.nativeTree = { ...a.tree, children: [{ id: 'schema', label: 'schema', kind: 'metadata', logicalPath: '/native/schema', sourcePath: '/schema', children: [
    { id: 'native-customer', label: 'Customers', kind: 'metadata', logicalPath: '/native/schema/customers', sourcePath: '/schema/0', location, children: [] }
  ] }] };
  b.nativeTree = { ...b.tree, id: b.uri + '#/', children: [{ id: 'resources', label: 'resources', kind: 'metadata', logicalPath: '/native/resources', sourcePath: '/resources', children: [] }] };
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(a, b), new Map());
  const tree = new ContractTree(state); tree.refresh();
  const asset = tree.getChildren(tree.getChildren()[0])[0];
  const synthetic = tree.getChildren(asset)[0];
  tree.mode = 'native'; tree.refresh();
  assert.equal(tree.getChildren(tree.getChildren()[0])[0].node.label, 'schema');
  assert.equal(tree.getChildren(tree.getChildren()[1])[0].node.label, 'resources');
  const match = tree.corresponding(asset);
  assert.equal(match.node.id, 'native-customer');
  assert.equal(tree.corresponding(synthetic), match);
  assert.equal(tree.getTreeItem(match).command.command, 'packata.revealSource');
  tree.mode = 'canonical'; tree.refresh();
  assert.equal(tree.corresponding(match).node.id, 'asset');
  assert.equal(a.version, 1);
});

test('native partial documents retain diagnostics and malformed native trees are rejected', () => {
  const { ContractTree } = load('tree.ts', { vscode });
  const partial = inspection(); partial.nativeTree = partial.tree; partial.tree = null;
  partial.diagnostics = [{ severity: 'Warning', message: 'Mapping incomplete' }];
  const state = new protocol.ContractState(); state.apply(state.begin(), discovery(partial), new Map());
  const tree = new ContractTree(state); tree.mode = 'native'; tree.refresh();
  assert.match(tree.getTreeItem(tree.getChildren()[0]).tooltip, /Mapping incomplete/);
  tree.mode = 'canonical'; tree.refresh();
  assert.equal(tree.getChildren(tree.getChildren()[0])[0].node.label, 'Source properties (including unmapped)');
  partial.nativeTree = { children: 'invalid' };
  assert.throws(() => protocol.requireDiscovery(discovery(partial)));
});

test('restores independent expansions together before revealing the single selection', async () => {
  const { restoreViewState } = load('tree.ts', { vscode });
  const a = { node: { id: 'a' } }, b = { node: { id: 'b' } }, missing = { node: { id: 'removed' } };
  const targets = new Map([[a, { node: { id: 'native-a' } }], [b, { node: { id: 'native-b' } }]]);
  const calls = [], pending = [];
  const view = { reveal: (entry, options) => {
    calls.push({ id: entry.node.id, options });
    return options.expand ? new Promise(resolve => pending.push(resolve)) : Promise.resolve();
  } };
  const tree = { corresponding: entry => targets.get(entry) };
  const restored = restoreViewState(tree, view, [a, missing, b], b);
  assert.equal(calls.length, 2, 'Both expansions start before either completes; missing nodes are skipped');
  pending[1](); await Promise.resolve();
  assert.equal(calls.length, 2, 'Selection waits for every expansion');
  pending[0](); await restored;
  assert.equal(calls.length, 3);
  assert.equal(calls[2].id, 'native-b');
  assert.equal(calls[2].options.select, true);
  await restoreViewState(tree, view, [], missing);
  await restoreViewState(tree, view, []);
  assert.equal(calls.length, 3);
});
