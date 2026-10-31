const { test } = require('node:test');
const assert = require('node:assert/strict');
const esbuild = require('esbuild');
const vm = require('node:vm');
const manifest = require('../package.json').contributes;
function routes() {
  const output = esbuild.buildSync({ entryPoints: [require('node:path').join(__dirname, '../src/schemaRoutes.ts')], bundle: true, platform: 'node', format: 'cjs', write: false, external: ['vscode'] });
  const module = { exports: {} }, contexts = {};
  vm.runInNewContext(output.outputFiles[0].text, { module, exports: module.exports, require: () => ({ commands: { executeCommand: async (_, key, value) => { contexts[key] = value; } } }) });
  return { ...module.exports, contexts };
}
test('all bundled routes have declarative menu leaves and catalog-driven visibility', async () => {
  const api = routes();
  const catalog = Object.values(api.schemaRoutes).map(r => ({ id: r.target ?? r.outputFormat, operation: r.operation, language: r.language, dialects: r.dialect ? [{ id: r.dialect }] : undefined }));
  for (const [id, route] of Object.entries(api.schemaRoutes)) {
    assert.ok(manifest.commands.some(c => c.command === id));
    assert.ok(Object.values(manifest.menus).flat().some(m => m.command === id && m.when === id));
    assert.equal(api.available(route, catalog), true);
    assert.equal(api.available(route, []), false);
  }
  await api.schemaContexts(catalog);
  assert.equal(api.contexts['packata.schema.csharp'], true);
  await api.schemaContexts([]);
  assert.ok(Object.values(api.contexts).every(v => v === false));
});
test('generation has declarative intermediate language submenus', () => {
  for (const lang of ['csharp', 'python', 'sql']) {
    assert.ok(manifest.submenus.some(s => s.id === 'packata.schema.' + lang));
    assert.ok(manifest.menus['packata.schema.generate'].some(m => m.submenu === 'packata.schema.' + lang));
  }
});
