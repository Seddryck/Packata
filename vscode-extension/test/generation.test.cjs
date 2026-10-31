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
        if (options.cancelSelection) return undefined;
        if (settings.title === 'Generate Schema Code') return options.custom ? items.at(-1) : items[0];
        return items[0];
      },
      showOpenDialog: async settings => { calls.push({ pickerFolder: settings.defaultUri.toString() }); return [template]; },
      showTextDocument: async document => { calls.push({ outputLanguage: document.language }); },
      withProgress: async (_, work) => work({}, token)
    }
  };
  const bundle = esbuild.buildSync({ entryPoints: [path.join(__dirname, '../src/generation.ts')], bundle: true, platform: 'node', format: 'cjs', write: false, external: ['vscode'] });
  const module = { exports: {} };
  vm.runInNewContext(bundle.outputFiles[0].text, { module, exports: module.exports, require: name => name === 'vscode' ? mock : require(name), console });
  const discovery = { schemaVersion: 1, uri: source.toString(), version: 1, selectionKey: 'key', diagnostics: [],
    catalog: { contextVersion: 1, targets: [{ id: options.arrow ? 'arrow' : 'record', label: options.arrow ? 'Arrow Schema' : 'C# record', profile: 'csharp-record', engine: 'scriban', language: options.language ?? 'csharp' }], profiles: ['csharp-record'], engines: ['scriban', 'hbs'], defaultEngine: 'scriban' },
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
      result: { isSuccessful: !options.failure, content: options.failure ? null : 'generated', language: options.custom ? 'plaintext' : 'csharp', suggestedFilename: 'Orders.cs',
        templateVersion: options.custom ? 7 : 1, diagnostics: options.failure ? [{ severity: 'Error', message: 'Mapping failed' }] : [] } };
  };
  return { api: module.exports, mock, request, errors, opened, calls, run: entry => module.exports.generateSchemaCode(request, entry, () => session, () => selected) };
}

test('generation opens an editable C# untitled document without writing a descriptor', async () => {
  const h = harness(); await h.run();
  assert.equal(h.opened.length, 1); assert.equal(h.opened[0].content, 'generated');
  assert.ok(h.opened[0].uri.path.endsWith('/Orders.cs'));
  assert.equal(h.calls.at(-1).outputLanguage, 'csharp');
});

for (const language of ['csharp', 'python']) {
  test('Arrow generation routes the catalog target and ' + language + ' pair', async () => {
    const h = harness({ arrow: true, language }); await h.run();
    const call = h.calls.find(c => c.command === 'packata.schema.export');
    assert.equal(call.args.options.target, 'arrow');
    assert.equal(call.args.options.language, language);
    assert.equal(h.opened.length, 1);
  });
}
test('custom generation uses unsaved template text and the descriptor owning workspace folder', async () => {
  const h = harness({ custom: true }); await h.run();
  const call = h.calls.find(c => c.command === 'packata.schema.export');
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
    assert.ok(!h.calls.some(c => c.command === 'packata.schema.export'));
  }
});
test('native and canonical asset nodes preserve server-backed identity and reject obsolete nodes', async () => {
  const h = harness();
  await h.run({ node: { kind: 'asset', sourcePath: '/schema/0' }, contract: { uri: 'file:///workspace/second/contract.yaml', generationKey: 'key' } });
  assert.equal(h.calls.find(c => c.command === 'packata.schema.export').args.options.assetId, 'orders');
  const stale = harness(); await stale.run({ node: { kind: 'asset', sourcePath: '/schema/9' }, contract: { uri: 'file:///workspace/second/contract.yaml' } });
  assert.equal(stale.opened.length, 0);
});
test('missing capabilities fail safely before any generated document opens', async () => {
  const h = harness({ missingCapability: true });
  await h.run(); assert.equal(h.opened.length, 0); assert.ok(h.errors.length);
});
