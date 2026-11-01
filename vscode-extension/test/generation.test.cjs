const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const path = require('node:path');
const esbuild = require('esbuild');

function harness(options = {}) {
  const errors = [], opened = [], calls = [], docs = [];
  let session = 1, selected = 'asset';
  class Uri {
    constructor(value) { const url = new URL(value); this.scheme = url.protocol.slice(0, -1); this.path = url.pathname; }
    toString() { return this.scheme + '://' + this.path; }
    with(change) { return new Uri((change.scheme ?? this.scheme) + '://' + (change.path ?? this.path)); }
    static parse(value) { return new Uri(value); }
  }
  const source = Uri.parse('file:///workspace/second/contract.yaml');
  const template = Uri.parse('file:///workspace/second/bar.template.cs');
  const token = { isCancellationRequested: false };
  const mock = {
    Uri, Position: class {}, Range: class {}, WorkspaceEdit: class { insert(uri, position, content) { this.uri = uri; this.content = content; } },
    ProgressLocation: { Notification: 1 },
    workspace: {
      isTrusted: options.trusted !== false, textDocuments: docs,
      fs: { stat: async () => { if (!options.existingDestination) throw new Error('absent'); return {}; }, writeFile: async (uri, bytes) => { calls.push({ savedBinary: uri.toString(), bytes: [...bytes] }); if (options.cancelAfterWrite) token.isCancellationRequested = true; }, rename: async (from, to, options) => calls.push({ renamedBinary: to.toString(), options }), delete: async () => {} },
      getWorkspaceFolder: uri => ({ uri: Uri.parse('file:///workspace/second') }),
      openTextDocument: async uri => {
        let doc = docs.find(d => d.uri.toString() === uri.toString());
        if (!doc) { doc = { uri, version: uri.path.endsWith('bar.template.cs') ? 7 : 1,
          getText: () => uri.path.endsWith('bar.template.cs') ? 'unsaved template' : 'descriptor' }; docs.push(doc); }
        return doc;
      },
      applyEdit: async edit => { assert.equal(edit.uri.scheme, 'untitled'); opened.push(edit); return true; }
    },
    languages: { setTextDocumentLanguage: async (document, language) => { document.language = language; return document; } },
    window: {
      activeTextEditor: { document: { uri: source } },
      showErrorMessage: async message => { errors.push(message); }, showWarningMessage: async message => { errors.push(message); },
      showQuickPick: async (items, settings) => {
        calls.push({ prompt: settings.title });
        if (options.cancelSelection) return undefined;
        if (settings.title === 'Generate Schema Code') return options.custom ? items.at(-1) : items[0];
        if (settings.title === 'Compatibility profile' && options.profile) return items.find(i => i === options.profile);
        return items[0];
      },
      showSaveDialog: async settings => { calls.push({ saveDialog: true }); return options.cancelSave ? undefined : Uri.parse('file:///workspace/schema.ipc'); },
      showOpenDialog: async settings => { calls.push({ pickerFolder: settings.defaultUri.toString() }); return [template]; },
      showTextDocument: async document => { calls.push({ outputLanguage: document.language }); },
      withProgress: async (_, work) => work({}, token)
    }
  };
  const bundle = esbuild.buildSync({ entryPoints: [path.join(__dirname, '../src/generation.ts')], bundle: true, platform: 'node', format: 'cjs', write: false, external: ['vscode'] });
  const module = { exports: {} };
  vm.runInNewContext(bundle.outputFiles[0].text, { module, exports: module.exports, require: name => name === 'vscode' ? mock : require(name), console, Buffer });
  const discovery = { schemaVersion: 1, uri: source.toString(), version: 1, selectionKey: 'key', diagnostics: [],
    catalog: { contextVersion: 1, targets: options.targets ?? [{ id: options.arrow ? 'arrow' : 'record', label: options.arrow ? 'Arrow Schema' : 'C# record', profile: 'csharp-record', engine: 'scriban', language: options.language ?? 'csharp' }], profiles: ['csharp-record'], engines: ['scriban', 'hbs'], defaultEngine: 'scriban' },
    assets: options.assets ?? [{ id: 'orders', name: 'orders', sourcePath: '/schema/0', eligible: true }] };
  const request = async (command, args) => {
    calls.push({ command, args });
    if (options.missingCapability) throw new Error('unsupported operation');
    if (command.endsWith('.options')) return discovery;
    if (options.cancelRequest) token.isCancellationRequested = true;
    if (options.staleSource) docs[0].version++;
    if (options.staleTemplate) docs.find(d => d.uri.toString() === template.toString()).version++;
    if (options.restart) session++;
    if (options.selectionChange) selected = 'other';
    return { schemaVersion: 1, uri: source.toString(), version: 1, selectionKey: 'key', findings: [],
      result: { representation: options.binary ? 'binary' : 'text', binaryContent: options.binary ? 'AAEC/w==' : undefined, isSuccessful: !options.failure, content: options.failure ? null : 'generated', language: options.custom ? 'plaintext' : 'csharp', suggestedFilename: 'Orders.cs',
        templateVersion: options.custom ? 7 : 1, diagnostics: options.failure ? [{ severity: 'Error', message: 'Mapping failed' }] : (options.diagnostics ?? []) } };
  };
  return { api: module.exports, mock, request, errors, opened, calls, run: (entry, route) => module.exports.generateSchemaCode(request, entry, () => session, () => selected, route) };
}

test('generation opens an editable C# untitled document without writing a descriptor', async () => {
  const h = harness(); await h.run();
  assert.equal(h.opened.length, 1); assert.equal(h.opened[0].content, 'generated');
  assert.ok(h.opened[0].uri.path.endsWith('/Orders.cs'));
  assert.equal(h.calls.at(-1).outputLanguage, 'csharp');
});

for (const [target, language] of [['class', 'csharp'], ['class', 'python'], ['dataclass', 'python'], ['pydantic', 'python']]) {
  test('model menu routes exact pair and offers only applicable options: ' + target + '/' + language, async () => {
    const h = harness({ targets: [{ id: target, language, operation: 'generate', options: language === 'python' ? ['asset', 'output', 'type-name'] : ['asset', 'output', 'namespace', 'type-name'], supportsCSharpOptions: language === 'csharp' }] });
    const original = h.mock.window.showQuickPick;
    h.mock.window.showQuickPick = async (items, settings) => {
      if (settings.title === 'Generation options') assert.equal(items.includes('Namespace…'), language === 'csharp');
      return original(items, settings);
    };
    await h.run(undefined, { operation: 'generate', target, language });
    const call = h.calls.find(c => c.command === 'packata.schema.generate');
    assert.equal(call.args.options.target, target);
    assert.equal(call.args.options.language, language);
    assert.equal(call.args.options.assetId, 'orders');
    assert.ok(!h.calls.some(c => c.prompt === 'Generate Schema Code'));
    assert.ok(h.calls.some(c => c.prompt === 'Generation options'));
    assert.equal(h.opened.length, 1);
  });
}

for (const language of ['csharp', 'python']) {
  test('Arrow generation routes the catalog target and ' + language + ' pair', async () => {
    const h = harness({ arrow: true, language }); await h.run();
    const call = h.calls.find(c => c.command === 'packata.schema.generate');
    assert.equal(call.args.options.target, 'arrow');
    assert.equal(call.args.options.language, language);
    assert.equal(h.opened.length, 1);
  });
}
test('custom generation uses unsaved template text and the descriptor owning workspace folder', async () => {
  const h = harness({ custom: true }); await h.run();
  const call = h.calls.find(c => c.command === 'packata.schema.generate');
  assert.equal(call.args.template.text, 'unsaved template'); assert.equal(call.args.template.version, 7);
  assert.equal(h.calls.find(c => c.pickerFolder).pickerFolder, 'file:///workspace/second');
  assert.equal(h.opened.length, 1);
});
for (const condition of ['staleSource', 'restart', 'selectionChange', 'cancelRequest', 'failure']) {
  test('generation discards ' + condition + ' without opening output', async () => {
    const h = harness({ [condition]: true }); await h.run(); assert.equal(h.opened.length, 0);
  });
}
test('generation discards changed unsaved template snapshots', async () => {
  const h = harness({ custom: true, staleTemplate: true }); await h.run(); assert.equal(h.opened.length, 0);
});
test('untrusted workspaces and cancelled target choices do not generate', async () => {
  for (const options of [{ trusted: false }, { cancelSelection: true }]) {
    const h = harness(options); await h.run(); assert.equal(h.opened.length, 0);
    assert.ok(!h.calls.some(c => c.command === 'packata.schema.generate'));
  }
});
test('native and canonical asset nodes preserve server-backed identity and reject obsolete nodes', async () => {
  const h = harness();
  await h.run({ node: { kind: 'asset', sourcePath: '/schema/0' }, contract: { uri: 'file:///workspace/second/contract.yaml', generationKey: 'key' } });
  assert.equal(h.calls.find(c => c.command === 'packata.schema.generate').args.options.assetId, 'orders');
  const stale = harness(); await stale.run({ node: { kind: 'asset', sourcePath: '/schema/9' }, contract: { uri: 'file:///workspace/second/contract.yaml' } });
  assert.equal(stale.opened.length, 0);
});
test('missing capabilities fail safely before any generated document opens', async () => {
  const h = harness({ missingCapability: true });
  await h.run(); assert.equal(h.opened.length, 0); assert.ok(h.errors.length);
});

for (const dialect of ['postgresql', 'sqlite', 'sqlserver', 'duckdb']) test('direct SQL leaf propagates ' + dialect + ' without dialect or asset prompt for whole contract', async () => {
  const h = harness({ targets: [{ id: 'ddl', label: 'SQL DDL', operation: 'generate', language: 'sql', dialects: [{ id: dialect, label: dialect }], wholeContract: true }], assets: [{ id: 'a', eligible: true }, { id: 'b', eligible: true }] });
  await h.run(undefined, { operation: 'generate', target: 'ddl', language: 'sql', dialect });
  const call = h.calls.find(c => c.command === 'packata.schema.generate');
  assert.equal(call.args.options.dialect, dialect); assert.equal(call.args.options.language, 'sql');
  assert.equal(call.args.options.assetId, undefined);
  assert.ok(!h.calls.some(c => c.prompt === 'SQL dialect' || c.prompt === 'Select an asset'));
  assert.equal(h.opened.length, 1);
});
test('generic DDL picker asks for its missing dialect once', async () => {
  const h = harness({ targets: [{ id: 'ddl', label: 'SQL DDL', operation: 'generate', language: 'sql', dialects: [{ id: 'duckdb', label: 'DuckDB' }], wholeContract: true }] });
  await h.run();
  assert.equal(h.calls.filter(c => c.prompt === 'SQL dialect').length, 1);
  assert.equal(h.calls.find(c => c.command === 'packata.schema.generate').args.options.dialect, 'duckdb');
});
test('direct unavailable dialect fails before rendering', async () => {
  const h = harness({ targets: [{ id: 'ddl', label: 'SQL DDL', operation: 'generate', language: 'sql', dialects: [{ id: 'sqlite', label: 'SQLite' }], wholeContract: true }] });
  await h.run(undefined, { operation: 'generate', target: 'ddl', language: 'sql', dialect: 'absent' });
  assert.equal(h.opened.length, 0); assert.ok(!h.calls.some(c => c.command === 'packata.schema.generate'));
});

for (const format of ['avro', 'spark-json']) test('export leaf routes selected asset and generic profile for ' + format, async () => {
  const h = harness({ targets: [{ id: format, label: format, operation: 'export', language: 'json', compatibilityProfiles: [] }] });
  await h.run({ node: { kind: 'asset', sourcePath: '/schema/0' }, contract: { uri: 'file:///workspace/second/contract.yaml' } }, { operation: 'export', outputFormat: format });
  const call = h.calls.find(c => c.command === 'packata.schema.export');
  assert.equal(call.args.options.outputFormat, format); assert.equal(call.args.options.assetId, 'orders');
  assert.equal(call.args.options.compatibilityProfile, undefined); assert.equal(h.opened.length, 1);
  assert.ok(!h.calls.some(c => c.prompt === 'Compatibility profile'));
});
test('only advertised compatible profiles are offered after preselecting a format', async () => {
  const h = harness({ targets: [{ id: 'avro', label: 'Avro', operation: 'export', language: 'json', compatibilityProfiles: ['hudi'] }], profile: 'hudi' });
  await h.run(undefined, { operation: 'export', outputFormat: 'avro' });
  assert.equal(h.calls.find(c => c.command === 'packata.schema.export').args.options.compatibilityProfile, 'hudi');
});
test('binary results require explicit save and never open as text', async () => {
  const h = harness({ binary: true }); await h.run();
  assert.equal(h.opened.length, 0); assert.equal(h.calls.filter(c => c.saveDialog).length, 1);
  assert.deepEqual(h.calls.find(c => c.savedBinary).bytes, [0, 1, 2, 255]);
});
for (const opt of ['cancelSave', 'existingDestination']) test('binary artifacts protect ' + opt, async () => {
  const h = harness({ binary: true, [opt]: true }); await h.run();
  assert.equal(h.opened.length, 0); assert.ok(!h.calls.some(c => c.savedBinary));
});

test('informational format limits open the preview without a warning notification', async () => {
  const h = harness({ targets: [{ id: 'avro', label: 'Avro', operation: 'export', language: 'json' }], diagnostics: [{ code: 'SCHEMA_RELATIONAL', severity: 'Information', message: 'Keys are descriptive metadata.' }] });
  await h.run(undefined, { operation: 'export', outputFormat: 'avro' });
  assert.equal(h.opened.length, 1);
  assert.deepEqual(h.errors, []);
});
test('implementation gaps remain visible as warnings while keeping a successful preview', async () => {
  const h = harness({ targets: [{ id: 'avro', label: 'Avro', operation: 'export', language: 'json' }], diagnostics: [{ code: 'SCHEMA_IMPLEMENTATION_GAP', severity: 'Warning', message: 'Explicit defaults not implemented.' }] });
  await h.run(undefined, { operation: 'export', outputFormat: 'avro' });
  assert.equal(h.opened.length, 1);
  assert.ok(h.errors.some(m => m.includes('warnings')));
});

for (const condition of ['cancelAfterWrite', 'staleSource', 'restart', 'selectionChange']) test('unified IPC export discards ' + condition, async () => {
  const h = harness({ binary: true, [condition]: true, targets: [{ id: 'arrow-ipc', label: 'Arrow IPC', operation: 'export', language: 'binary' }] });
  await h.run(undefined, { operation: 'export', outputFormat: 'arrow-ipc' });
  assert.ok(!h.calls.some(c => c.renamedBinary));
  assert.equal(h.opened.length, 0);
});
test('unified IPC export saves exact bytes through a protected atomic rename', async () => {
  const h = harness({ binary: true, targets: [{ id: 'arrow-ipc', label: 'Arrow IPC', operation: 'export', language: 'binary' }] });
  await h.run(undefined, { operation: 'export', outputFormat: 'arrow-ipc' });
  assert.equal(h.calls.find(c => c.command === 'packata.schema.export').args.options.outputFormat, 'arrow-ipc');
  assert.deepEqual(h.calls.find(c => c.savedBinary).bytes, [0, 1, 2, 255]);
  assert.equal(h.calls.find(c => c.renamedBinary).options.overwrite, false);
  assert.equal(h.opened.length, 0);
});
