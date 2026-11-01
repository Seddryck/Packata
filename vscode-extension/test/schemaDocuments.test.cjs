const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const path = require('node:path');
const esbuild = require('esbuild');

function harness(options = {}) {
  const errors = [], calls = [], files = new Map(), previews = [];
  const payload = Buffer.from([255, 255, 255, 255, 128, 0, 0, 0]);
  class Uri {
    constructor(value) { const url = new URL(value); this.scheme = url.protocol.slice(0, -1); this.path = url.pathname; }
    toString() { return this.scheme + '://' + this.path; }
    with(change) { return new Uri((change.scheme ?? this.scheme) + '://' + (change.path ?? this.path)); }
    static parse(value) { return new Uri(value); }
  }
  const source = Uri.parse('file:///workspace/contract.yaml'), output = Uri.parse('file:///workspace/orders.schema.ipc');
  const document = { uri: source, version: 4 };
  if (options.existing) files.set(output.toString(), Buffer.from('existing'));
  const token = { isCancellationRequested: false };
  const mock = {
    Uri, ProgressLocation: { Notification: 1 },
    workspace: {
      isTrusted: true,
      openTextDocument: async value => {
        if (value.content !== undefined) { previews.push(value); return value; }
        return document;
      },
      fs: {
        writeFile: async (uri, bytes) => { files.set(uri.toString(), Buffer.from(bytes)); if (options.cancelAfterWrite) token.isCancellationRequested = true; },
        rename: async (from, to, settings) => {
          assert.equal(settings.overwrite, false);
          if (files.has(to.toString())) throw new Error('exists');
          files.set(to.toString(), files.get(from.toString())); files.delete(from.toString());
        },
        delete: async uri => { files.delete(uri.toString()); }
      }
    },
    window: {
      activeTextEditor: { document },
      showQuickPick: async items => items[0],
      showSaveDialog: async () => options.cancelSave ? undefined : output,
      showTextDocument: async value => { previews.push(value); },
      showErrorMessage: async message => { errors.push(message); },
      showWarningMessage: async () => {}, showInformationMessage: async () => {},
      withProgress: async (_, work) => work({}, token)
    }
  };
  const bundle = esbuild.buildSync({ entryPoints: [path.join(__dirname, '../src/schemaDocuments.ts')], bundle: true,
    platform: 'node', format: 'cjs', write: false, external: ['vscode'] });
  const module = { exports: {} };
  vm.runInNewContext(bundle.outputFiles[0].text, { module, exports: module.exports, Buffer, require: name => name === 'vscode' ? mock : require(name) });
  const request = async (command, args) => {
    calls.push({ command, args });
    if (options.missingCapability) throw new Error('unsupported');
    const common = { schemaVersion: 1, uri: source.toString(), version: 4, selectionKey: 'key' };
    if (command.endsWith('.options')) return { ...common,
      formats: options.disabled ? [] : [{ id: options.text ? 'json-schema' : 'arrow-ipc', label: 'Arrow IPC', representation: options.text ? 'text' : 'binary', extension: '.schema.ipc', profiles: [] }],
      assets: [{ id: 'orders', name: 'orders', sourcePath: '/resources/0', eligible: true }] };
    if (options.stale) document.version++;
    if (options.cancelRequest) token.isCancellationRequested = true;
    return { ...common, result: { isSuccessful: !options.failure, representation: options.text ? 'text' : 'binary', content: options.text ? '{}' : null,
      bytes: options.corrupt ? 'not base64' : payload.toString('base64'), suggestedFilename: 'orders.schema.ipc', diagnostics: [] } };
  };
  return { api: module.exports, errors, calls, files, previews, payload, output,
    run: entry => module.exports.exportSchemaDocument(request, entry, options.text ? 'json-schema' : 'arrow-ipc') };
}

test('binary schema artifacts save exact bytes and never open in an editable text document', async () => {
  const h = harness(); await h.run();
  assert.deepEqual(h.files.get(h.output.toString()), h.payload);
  assert.equal(h.files.size, 1); assert.equal(h.previews.length, 0);
  assert.equal(h.calls[1].args.options.outputFormat, 'arrow-ipc');
  assert.equal(h.calls[1].args.options.assetId, 'orders');
});

for (const condition of ['cancelSave', 'stale', 'cancelRequest', 'missingCapability', 'disabled', 'corrupt', 'failure', 'cancelAfterWrite']) {
  test('schema artifact ' + condition + ' leaves no output or temporary file', async () => {
    const h = harness({ [condition]: true }); await h.run();
    assert.equal(h.files.size, 0); assert.equal(h.previews.length, 0);
  });
}

test('existing output remains intact', async () => {
  const h = harness({ existing: true }); await h.run();
  assert.equal(h.files.get(h.output.toString()).toString(), 'existing');
  assert.equal(h.files.size, 1); assert.equal(h.errors.length, 1);
});

test('clicked asset scope and snapshot identity are retained', async () => {
  const h = harness(); await h.run({ node: { kind: 'asset', sourcePath: '/resources/0' }, contract: { uri: 'file:///workspace/contract.yaml', generationKey: 'key' } });
  assert.equal(h.calls[1].args.sourcePath, '/resources/0');
  assert.equal(h.calls[1].args.version, 4);
  const stale = harness(); await stale.run({ node: { kind: 'asset', sourcePath: '/resources/0' }, contract: { uri: 'file:///workspace/contract.yaml', generationKey: 'old' } });
  assert.equal(stale.files.size, 0); assert.equal(stale.calls.length, 1);
});

test('text schema export opens an untitled preview and does not write an artifact', async () => {
  const h = harness({ text: true }); await h.run();
  assert.equal(h.files.size, 0); assert.equal(h.previews[0].content, '{}');
});

test('manifest declares Arrow IPC only under the schema-document operation', () => {
  const manifest = require('../package.json');
  const entries = manifest.contributes.menus['packata.schema.export'];
  assert.ok(entries.some(e => e.command === 'packata.schema.arrowIpc' && e.when === 'packata.schema.arrowIpc'));
  assert.ok(manifest.contributes.submenus.some(s => s.id === 'packata.schema.export'));
  assert.ok(manifest.contributes.menus['packata.schema.csharp'].some(e => e.command === 'packata.schema.arrowCsharp' && e.when === 'packata.schema.arrowCsharp'));
  assert.ok(manifest.contributes.menus['packata.schema.python'].some(e => e.command === 'packata.schema.arrowPython' && e.when === 'packata.schema.arrowPython'));
});
