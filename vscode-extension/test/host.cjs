const vscode = require('vscode');
const assert = require('node:assert/strict');
const path = require('node:path');

exports.run = async function () {
  const extension = vscode.extensions.getExtension('seddryck.packata-language-support');
  assert.ok(extension, 'Packata extension is discoverable');
  await extension.activate();
  const commands = await vscode.commands.getCommands();
  assert.ok(commands.includes('packata.contract.inspect'), 'Server registered its typed inspection operation');
  const folders = vscode.workspace.workspaceFolders.map(folder => folder.uri.toString());
  const discover = await vscode.commands.executeCommand('packata.workspace.discover', { workspaceFolders: folders, schemaVersion: 1 });
  assert.equal(discover.contracts.length, 2, 'Both roots are discovered, unrelated JSON is ignored');
  assert.ok(discover.contracts.every(contract => contract.tree.children.some(node => node.label === 'Assets')));
  assert.ok(discover.contracts.every(contract => contract.nativeTree.children.some(node => node.label === (contract.format === 'odcs' ? 'schema' : 'resources'))));
  await vscode.commands.executeCommand('workbench.view.extension.packata');
  await vscode.commands.executeCommand('packata.refresh');
  await vscode.commands.executeCommand('packata.selectView', 'native');
  const odcs = discover.contracts.find(contract => contract.format === 'odcs');
  const odcsColumn = odcs.tree.children.find(node => node.label === 'Assets').children[0].children.find(node => node.label === 'Columns').children[0];
  await vscode.commands.executeCommand('packata.revealSource', { node: odcsColumn, contract: odcs });
  assert.equal(vscode.window.activeTextEditor.selection.start.line, odcsColumn.location.range.start.line, 'An unopened file can be opened and revealed at a current source range');
  const uri = vscode.Uri.file(path.join(process.env.PACKATA_SMOKE_WORKSPACE, 'first', 'datapackage.yaml'));
  const document = await vscode.workspace.openTextDocument(uri);
  await vscode.window.showTextDocument(document);
  const edit = new vscode.WorkspaceEdit();
  edit.replace(uri, new vscode.Range(0, 0, 0, 'name: Sales'.length), 'name: Unsaved');
  await vscode.workspace.applyEdit(edit);
  await vscode.commands.executeCommand('packata.inspect');
  const inspection = JSON.parse(vscode.window.activeTextEditor.document.getText());
  assert.equal(inspection.contract.identity.name, 'Unsaved');
  assert.equal(inspection.version, document.version);
  const assets = inspection.tree.children.find(node => node.label === 'Assets');
  const column = assets.children[0].children.find(node => node.label === 'Columns').children[0];
  await vscode.commands.executeCommand('packata.revealSource', { node: column, contract: inspection });
  assert.equal(vscode.window.activeTextEditor.document.uri.toString(), uri.toString());
  assert.equal(vscode.window.activeTextEditor.selection.start.line, column.location.range.start.line);
  assert.equal(document.isDirty, true);
  const unchanged = document.getText();
  await vscode.commands.executeCommand('packata.selectView', 'native');
  const nativeName = inspection.nativeTree.children.find(node => node.sourcePath === '/name');
  assert.equal(nativeName.description, 'Unsaved');
  await vscode.commands.executeCommand('packata.revealSource', { node: nativeName, contract: inspection });
  assert.equal(vscode.window.activeTextEditor.selection.start.line, 0);
  await vscode.commands.executeCommand('packata.selectView', 'canonical');
  assert.equal(document.getText(), unchanged, 'Switching projections never rewrites the descriptor');
  const generationOptions = await vscode.commands.executeCommand('packata.schema.options', { uri: uri.toString(), schemaVersion: 1 });
  assert.deepEqual(generationOptions.catalog.targets.filter(t => t.operation === 'generate' && t.language === 'csharp').map(t => t.id).sort(), ['arrow', 'class', 'datatable', 'dotnet-dataframe', 'idataview', 'polars', 'record'], 'Bundled C# targets come from Application');
  assert.deepEqual(generationOptions.catalog.targets.filter(t => t.operation === 'generate' && t.language === 'python').map(t => t.id).sort(), ['arrow', 'class', 'dataclass', 'pydantic', 'pyspark'], 'Bundled Python targets come from Application');
  assert.deepEqual(generationOptions.catalog.targets.filter(t => t.operation === 'export').map(t => t.id).sort(), ['arrow-ipc', 'avro', 'json-schema', 'spark-json'], 'Document formats share the Application catalog');
  assert.ok(generationOptions.catalog.targets.some(t => t.id === 'ddl' && t.dialects.some(d => d.id === 'sqlite')), 'Offline SQL dialects are advertised');
  const generated = await vscode.commands.executeCommand('packata.schema.generate', { uri: uri.toString(), version: document.version,
    selectionKey: generationOptions.selectionKey, sourcePath: '/resources/0', options: { assetId: 'orders', target: 'record', language: 'csharp' }, schemaVersion: 1 });
  assert.equal(generated.result.isSuccessful, true, JSON.stringify(generated.result.diagnostics));
  assert.ok(generated.result.content.includes('record Orders'), 'Bundled Didot record template works in the packaged server');
  await assertModelGeneration(uri, document.version, generationOptions.selectionKey);
  const templateUri = vscode.Uri.file(path.join(process.env.PACKATA_SMOKE_WORKSPACE, 'second', 'bar.template.cs'));
  const template = await vscode.workspace.openTextDocument(templateUri);
  const templateEdit = new vscode.WorkspaceEdit();
  templateEdit.insert(templateUri, new vscode.Position(0, 0), 'unsaved ');
  await vscode.workspace.applyEdit(templateEdit);
  const custom = await vscode.commands.executeCommand('packata.schema.generate', { uri: uri.toString(), version: document.version,
    selectionKey: generationOptions.selectionKey, options: {}, template: { uri: templateUri.toString(), text: template.getText(), version: template.version }, schemaVersion: 1 });
  assert.equal(custom.result.content, 'unsaved orders', 'Custom template uses explicit unsaved text and default Scriban');
  assert.equal(document.getText(), unchanged, 'Generation never changes the source');
  await vscode.commands.executeCommand('packata.restart');
  const restarted = await vscode.commands.executeCommand('packata.contract.inspect', { uri: uri.toString(), schemaVersion: 1 });
  assert.equal(restarted.contract.identity.name, 'Unsaved', `Restart resynchronizes modified buffers (version ${restarted.version}, buffer ${document.getText().split('\n')[0]})`);
  // Do not save modified source fixtures. The isolated test host discards this profile.
  console.log('Packata host smoke: activation, mixed roots, navigation, bundled/custom generation, unsaved templates and restart passed.');
};

async function assertModelGeneration(uri, version, selectionKey) {
  for (const [target, language, declaration] of [
    ['class', 'csharp', 'public class Orders'], ['class', 'python', 'class Orders:'],
    ['dataclass', 'python', '@dataclass(kw_only=True)'], ['pydantic', 'python', 'class Orders(BaseModel):'],
    ['pyspark', 'python', 'S.StructType(['], ['dotnet-dataframe', 'csharp', 'DataFrame Create()'],
    ['idataview', 'csharp', 'DataViewSchema Create()']
  ]) {
    const generated = await vscode.commands.executeCommand('packata.schema.generate', { uri: uri.toString(), version,
      selectionKey, sourcePath: '/resources/0', options: { assetId: 'orders', target, language }, schemaVersion: 1 });
    assert.equal(generated.result.isSuccessful, true, JSON.stringify(generated.result.diagnostics));
    assert.equal(generated.result.language, language);
    assert.ok(generated.result.content.includes(declaration), `${target}/${language} template is bundled in the packaged server`);
  }
}
