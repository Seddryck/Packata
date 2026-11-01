import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import { artifactBytes } from './schemaDocuments';
import * as vscode from 'vscode';
import { requireSchema } from './protocol';
import { TreeEntry } from './tree';

interface Finding { code: string; severity: string; sourcePath?: string; message: string }
export interface Target { options?: string[]; operation?: string; category?: string; representation?: string; extension?: string; compatibilityProfiles?: string[]; dialects?: { id: string; label: string }[]; wholeContract?: boolean; id: string; label: string; profile: string; engine: string; language: string; supportsCSharpOptions: boolean; limitation: string }
export interface GenerationDiscovery {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string;
  catalog: { contextVersion: number; targets: Target[]; profiles: string[]; engines: string[]; defaultEngine: string; limitations: string[] };
  assets: { id: string; name: string; sourcePath: string; eligible: boolean; reason?: string }[]; diagnostics: Finding[];
}
export interface SchemaRoute { operation: string; target?: string; language?: string; outputFormat?: string; dialect?: string; custom?: boolean }
interface Options { documentOptions?: { name?: string; namespace?: string }; ddlOptions?: { constraints: number; descriptions: boolean }; operation?: string; language?: string; outputFormat?: string; dialect?: string; compatibilityProfile?: string; assetId?: string; target?: string; engine?: string; profile?: string; namespace?: string; typeName?: string; defines?: string[] }
interface TemplateSnapshot { uri: string; text: string; version: number }
export interface Generated {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string;
  result: { isSuccessful: boolean; content?: string; binaryContent?: string; representation?: string; mediaType?: string; language?: string; suggestedFilename?: string; templateVersion?: number | null; diagnostics: Finding[] };
  findings: { diagnostic: Finding; location?: { uri: string; range: { start: { line: number; character: number }; end: { line: number; character: number } } } }[];
}
type Request = <T>(command: string, args: object, token?: vscode.CancellationToken) => Promise<T>;

export function requireGenerationOptions(value: unknown): asserts value is GenerationDiscovery {
  requireSchema(value);
  const item = value as GenerationDiscovery;
  if (!item.catalog || item.catalog.contextVersion !== 1 || !Array.isArray(item.catalog.targets)
    || !Array.isArray(item.catalog.profiles) || !Array.isArray(item.catalog.engines)
    || !Array.isArray(item.assets) || typeof item.selectionKey !== 'string') throw new Error('Unsupported generation catalog. Update the server.');
}

export function generationCurrent(result: Generated, sourceVersion: number | null, liveSourceVersion: number | null,
  templateVersion: number | null, liveTemplateVersion: number | null, sessionSame: boolean, selectionSame: boolean): boolean {
  return sessionSame && selectionSame && result.version === sourceVersion && sourceVersion === liveSourceVersion
    && templateVersion === liveTemplateVersion && (templateVersion === null || (result.result.templateVersion ?? null) === templateVersion);
}

async function customize(options: Options, custom: boolean): Promise<boolean> {
  while (true) {
    const choices = ['Generate', ...(options.language === 'python' ? [] : ['Namespace…']), 'Type name…', ...(custom ? ['User variable…'] : [])];
    const choice = await vscode.window.showQuickPick(choices, { title: 'Generation options', placeHolder: 'Generate with defaults, or customize an option' });
    if (choice === undefined) return false;
    if (choice === 'Generate') return true;
    const value = await vscode.window.showInputBox({ title: choice, prompt: choice === 'User variable…' ? 'key=value; repeat to add more variables' : 'Leave empty for the default' });
    if (value === undefined) continue;
    if (choice === 'Namespace…') options.namespace = value || undefined;
    else if (choice === 'Type name…') options.typeName = value || undefined;
    else (options.defines ??= []).push(value);
  }
}

async function documentOptions(options: Options): Promise<boolean> {
  while (true) {
    const sql = options.target === 'ddl';
    const choice = await vscode.window.showQuickPick(sql ? ['Generate', 'Constraints…', 'Descriptions…'] : ['Export', 'Schema name…', 'Schema namespace…'], { title: sql ? 'SQL options' : 'Avro options' });
    if (!choice) return false;
    if (choice === 'Generate' || choice === 'Export') return true;
    if (sql) await editSqlOptions(options, choice);
    else await editAvroOptions(options, choice);
  }
}

async function editSqlOptions(options: Options, choice: string): Promise<void> {
  options.ddlOptions ??= { constraints: 31, descriptions: true };
  const value = await vscode.window.showQuickPick(choice === 'Constraints…' ? ['All', 'None', 'Primary key and required'] : ['Include descriptions', 'Omit descriptions'], { title: choice });
  if (!value) return;
  if (choice === 'Constraints…') options.ddlOptions.constraints = value === 'All' ? 31 : value === 'None' ? 0 : 3;
  else options.ddlOptions.descriptions = value === 'Include descriptions';

}

async function editAvroOptions(options: Options, choice: string): Promise<void> {
  const value = await vscode.window.showInputBox({ title: choice, prompt: 'Leave empty for the catalog default' });
  if (value === undefined) return;
  options.documentOptions ??= {};
  if (choice === 'Schema name…') options.documentOptions.name = value || undefined;
  else options.documentOptions.namespace = value || undefined;

}

async function selectTemplate(discovery: GenerationDiscovery, uri: vscode.Uri, options: Options, customOnly = false): Promise<TemplateSnapshot | null | undefined> {
  const choices = discovery.catalog.targets.filter(target => (target.operation ?? 'generate') === 'generate').map(target => ({ label: target.label, description: target.limitation, target }));
  const custom = { label: 'Custom template…', description: 'Select a Didot template', target: undefined };
  const selected = customOnly ? custom : await vscode.window.showQuickPick([...choices, custom], { title: 'Generate Schema Code' });
  if (!selected) return undefined;
  if (selected.target) { options.target = selected.target.id; options.language = selected.target.language; return null; }
  const folder = vscode.workspace.getWorkspaceFolder(uri)?.uri ?? uri.with({ path: path.posix.dirname(uri.path) });
  const files = await vscode.window.showOpenDialog({ title: 'Select Didot template', canSelectMany: false, defaultUri: folder });
  if (!files?.length || files[0].scheme !== 'file') return undefined;
  const document = await vscode.workspace.openTextDocument(files[0]);
  const engine = await vscode.window.showQuickPick(['Automatic (suffix or ' + discovery.catalog.defaultEngine + ')', ...discovery.catalog.engines], { title: 'Template engine' });
  if (!engine) return undefined;
  if (discovery.catalog.engines.includes(engine)) options.engine = engine;
  const profile = await vscode.window.showQuickPick(['Canonical context only', ...discovery.catalog.profiles], { title: 'Optional type-mapping profile' });
  if (!profile) return undefined;
  if (discovery.catalog.profiles.includes(profile)) options.profile = profile;
  return { uri: document.uri.toString(), text: document.getText(), version: document.version };
}

async function showFindings(result: Generated): Promise<void> {
  const selected = await vscode.window.showQuickPick(result.findings.map(finding => ({ label: `${finding.diagnostic.severity} ${finding.diagnostic.code}`,
    description: finding.diagnostic.message, finding })), { title: 'Schema generation findings' });
  const location = selected?.finding.location;
  if (!location) return;
  const range = new vscode.Range(location.range.start.line, location.range.start.character, location.range.end.line, location.range.end.character);
  await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.parse(location.uri)), { selection: range });
}

async function openGenerated(result: Generated, source: vscode.Uri, template?: TemplateSnapshot | null): Promise<void> {
  const suggested = result.result.suggestedFilename ?? 'Generated.txt';
  if (suggested.includes('/') || suggested.includes('\\') || suggested === '..') throw new Error('Invalid generated filename.');
  const parent = path.posix.dirname(source.path);
  let name = suggested;
  let suffix = 2;
  const candidate = () => source.with({ scheme: 'untitled', path: path.posix.join(parent, name), query: '', fragment: '' });
  while (vscode.workspace.textDocuments.some(d => d.uri.toString() === candidate().toString())
    || path.posix.join(parent, name) === source.path || (template && path.posix.join(parent, name) === vscode.Uri.parse(template.uri).path)) {
    name = path.posix.parse(suggested).name + '.generated' + suffix++ + path.posix.extname(suggested);
  }
  const document = await vscode.workspace.openTextDocument(candidate());
  const edit = new vscode.WorkspaceEdit();
  edit.insert(document.uri, new vscode.Position(0, 0), result.result.content!);
  if (!await vscode.workspace.applyEdit(edit)) throw new Error('Unable to open generated output.');
  const typed = await vscode.languages.setTextDocumentLanguage(document, result.result.language ?? 'plaintext');
  await vscode.window.showTextDocument(typed, { preview: false });
}

interface PreparedGeneration {
  uri: string; sourceUri: vscode.Uri; discovery: GenerationDiscovery; sourcePath?: string;
  options: Options; template: TemplateSnapshot | null; epoch: number; selectedNode?: string;
}

function sourceUri(entry?: TreeEntry): vscode.Uri | undefined {
  if (vscode.workspace.isTrusted === false) {
    void vscode.window.showErrorMessage('Schema generation requires a trusted workspace.'); return undefined;
  }
  const uri = entry?.contract.uri ?? vscode.window.activeTextEditor?.document.uri.toString();
  if (!uri || vscode.Uri.parse(uri).scheme !== 'file') {
    void vscode.window.showErrorMessage('Open or select a local contract first.'); return undefined;
  }
  return vscode.Uri.parse(uri);
}

async function chooseAsset(discovery: GenerationDiscovery, entry?: TreeEntry): Promise<GenerationDiscovery['assets'][number] | undefined> {
  const assets = discovery.assets.filter(a => a.eligible && (entry?.node.kind !== 'asset' || a.sourcePath === entry.node.sourcePath));
  const error = discovery.diagnostics.find(d => d.severity === 'Error');
  if (!assets.length || error) {
    void vscode.window.showErrorMessage(error?.message ?? discovery.assets[0]?.reason ?? 'No eligible canonical schema. Refresh the selected contract.'); return undefined;
  }
  if (assets.length === 1) return assets[0];
  return (await vscode.window.showQuickPick(assets.map(a => ({ label: a.name, description: a.id, asset: a })), { title: 'Select an asset' }))?.asset;
}

async function chooseTarget(discovery: GenerationDiscovery, source: vscode.Uri, options: Options, custom?: boolean): Promise<TemplateSnapshot | null | undefined> {
  let template: TemplateSnapshot | null | undefined = null;
  if (options.operation === 'export') {
    if (!options.outputFormat) {
      const selected = await vscode.window.showQuickPick(discovery.catalog.targets.filter(t => t.operation === 'export').map(target => ({ label: target.label, target })), { title: 'Export Schema' });
      if (!selected) return undefined;
      options.outputFormat = selected.target.id;
    }
  } else if (!options.target) {
    template = await selectTemplate(discovery, source, options, custom);
    if (template === undefined) return undefined;
  }
  return template;
}

async function chooseTargetOptions(options: Options, target?: Target): Promise<boolean> {
  if (target?.dialects?.length) {
    if (!options.dialect) {
      const selected = await vscode.window.showQuickPick(target.dialects.map(d => ({ label: d.label, id: d.id })), { title: 'SQL dialect' });
      if (!selected) return false;
      options.dialect = selected.id;
    }
    if (!target.dialects.some(d => d.id === options.dialect)) throw new Error('Selected dialect is unavailable.');
  }
  if (target?.compatibilityProfiles?.length) {
    const profile = await vscode.window.showQuickPick(['Generic', ...target.compatibilityProfiles], { title: 'Compatibility profile' });
    if (!profile) return false;
    if (profile !== 'Generic') options.compatibilityProfile = profile;
  }
  if (target?.options?.some(o => o === 'schema-name' || o === 'constraints') && !await documentOptions(options)) return false;
  return true;
}

function findTarget(discovery: GenerationDiscovery, options: Options): Target | undefined {
  return discovery.catalog.targets.find(t => (t.operation ?? 'generate') === options.operation
    && t.id === (options.outputFormat ?? options.target) && (options.operation === 'export' || t.language === options.language));
}

async function customizeSource(options: Options, target: Target | undefined, custom: boolean): Promise<boolean> {
  if (options.operation !== 'generate') return true;
  if (!custom && !target?.supportsCSharpOptions && !target?.options?.includes('type-name')) return true;
  return customize(options, custom);
}

async function prepare(request: Request, entry: TreeEntry | undefined, epoch: number, selectedNode?: string, route?: SchemaRoute): Promise<PreparedGeneration | undefined> {
  const source = sourceUri(entry);
  if (!source) return undefined;
  const uri = source.toString();
  await vscode.workspace.openTextDocument(source);
  const discovery = await request<GenerationDiscovery>('packata.schema.options', { uri, selectionKey: entry?.contract.generationKey });
  requireGenerationOptions(discovery);
  const { custom, ...selectors } = route ?? { operation: 'generate' };
  const options: Options = selectors;
  const template = await chooseTarget(discovery, source, options, custom);
  if (template === undefined) return undefined;
  const target = findTarget(discovery, options);
  if (template === null && !target) throw new Error('The selected capability is unavailable. Refresh the server catalog.');
  if (!await chooseTargetOptions(options, target)) return undefined;
  const whole = target?.wholeContract && entry?.node.kind !== 'asset';
  const asset = whole ? undefined : await chooseAsset(discovery, entry);
  if (!whole && !asset) return undefined;
  options.assetId = asset?.id;
  if (!await customizeSource(options, target, template !== null)) return undefined;
  return { uri, sourceUri: source, discovery, sourcePath: asset?.sourcePath, options, template, epoch, selectedNode };
}

const bufferVersion = (uri: string): number | null => vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.version ?? null;

async function saveBinary(result: Generated, prepared: PreparedGeneration, template: TemplateSnapshot | null, current: () => boolean): Promise<void> {
  if (typeof result.result.binaryContent !== 'string') throw new Error('Missing binary artifact.');
  const filename = result.result.suggestedFilename ?? 'schema.bin';
  if (filename.includes('/') || filename.includes('\\') || filename === '..') throw new Error('Invalid artifact filename.');
  const destination = await vscode.window.showSaveDialog({ title: 'Save schema artifact', defaultUri: prepared.sourceUri.with({ path: path.posix.join(path.posix.dirname(prepared.sourceUri.path), filename) }) });
  if (!destination || destination.scheme !== 'file' || !current()) return;
  if (destination.toString() === prepared.uri || destination.toString() === template?.uri) throw new Error('The source and template are protected.');
  let exists = false;
  try { await vscode.workspace.fs.stat(destination); exists = true; } catch { /* A new destination is required. */ }
  if (exists) throw new Error('Choose a new artifact filename.');
  const bytes = artifactBytes(result.result.binaryContent);
  const temporary = destination.with({ path: destination.path + '.packata-' + randomUUID() + '.tmp' });
  try {
    await vscode.workspace.fs.writeFile(temporary, bytes);
    if (!current()) return;
    await vscode.workspace.fs.rename(temporary, destination, { overwrite: false });
  } finally { try { await vscode.workspace.fs.delete(temporary); } catch { /* Already moved or absent. */ } }
}

async function render(request: Request, prepared: PreparedGeneration, token: vscode.CancellationToken,
  session: () => number, selection: () => string | undefined): Promise<void> {
  const { uri, discovery, options, template, sourcePath } = prepared;
  const result = await request<Generated>(options.operation === 'export' ? 'packata.schema.export' : 'packata.schema.generate', { uri, version: discovery.version,
    selectionKey: discovery.selectionKey, sourcePath, options, template }, token);
  if (token.isCancellationRequested) return;
  requireSchema(result);
  if (result.uri !== uri || result.selectionKey !== discovery.selectionKey) {
    void vscode.window.showWarningMessage('The generation response belongs to an obsolete source snapshot. Generate again.'); return;
  }
  if (!generationCurrent(result, discovery.version, bufferVersion(uri), template?.version ?? null,
    template ? bufferVersion(template.uri) : null, prepared.epoch === session(), prepared.selectedNode === selection())) {
    void vscode.window.showWarningMessage('The source, template, selection, or server changed. Generate again.'); return;
  }
  if (!result.result.isSuccessful || (result.result.representation !== 'binary' && typeof result.result.content !== 'string')) {
    const error = result.result.diagnostics.find(d => d.severity === 'Error');
    void vscode.window.showErrorMessage(error?.message ?? 'Schema generation failed.', 'View findings').then(choice => choice && showFindings(result)); return;
  }
  if (result.result.diagnostics.some(d => d.severity === 'Warning'))
    void vscode.window.showWarningMessage('Schema code generated with representation warnings.', 'View findings').then(choice => choice && showFindings(result));
  if (result.result.representation === 'binary') {
    await saveBinary(result, prepared, template, () => !token.isCancellationRequested &&
      generationCurrent(result, discovery.version, bufferVersion(uri), template?.version ?? null, template ? bufferVersion(template.uri) : null,
        prepared.epoch === session(), prepared.selectedNode === selection()));
  } else await openGenerated(result, prepared.sourceUri, template);
}

export async function generateSchemaCode(request: Request, entry?: TreeEntry, session: () => number = () => 0,
  selection: () => string | undefined = () => undefined, route?: SchemaRoute): Promise<void> {
  try {
    const prepared = await prepare(request, entry, session(), selection(), route);
    if (!prepared) return;
    await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Generating schema code…', cancellable: true }, async (_progress, token) => {
      try { await render(request, prepared, token, session, selection); }
      catch { if (!token.isCancellationRequested) void vscode.window.showErrorMessage('Unable to generate schema code. Check source/template access and server capabilities.'); }
    });
  } catch {
    void vscode.window.showErrorMessage('Schema generation is unavailable. Check the descriptor and template, or update the server for generation capabilities.');
  }
}
