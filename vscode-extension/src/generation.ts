import * as path from 'node:path';
import * as vscode from 'vscode';
import { requireSchema } from './protocol';
import { TreeEntry } from './tree';

interface Finding { code: string; severity: string; sourcePath: string; message: string }
interface Target { id: string; label: string; profile: string; engine: string; language: string; supportsCSharpOptions: boolean; limitation: string }
export interface GenerationDiscovery {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string;
  catalog: { contextVersion: number; targets: Target[]; profiles: string[]; engines: string[]; defaultEngine: string; limitations: string[] };
  assets: { id: string; name: string; sourcePath: string; eligible: boolean; reason?: string }[]; diagnostics: Finding[];
}
interface Options { assetId?: string; target?: string; language?: string; engine?: string; profile?: string; namespace?: string; typeName?: string; defines?: string[] }
interface TargetSelection { target: string; language: string }
interface TemplateSnapshot { uri: string; text: string; version: number }
export interface Generated {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string;
  result: { isSuccessful: boolean; content?: string; language?: string; suggestedFilename?: string; templateVersion?: number | null; diagnostics: Finding[] };
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

async function selectTemplate(discovery: GenerationDiscovery, uri: vscode.Uri, options: Options, preset?: TargetSelection): Promise<TemplateSnapshot | null | undefined> {
  if (preset) {
    const target = discovery.catalog.targets.find(t => t.id === preset.target && t.language === preset.language);
    if (!target) throw new Error('This target/language pair is unavailable.');
    options.target = target.id; options.language = target.language; return null;
  }
  const choices = discovery.catalog.targets.map(target => ({ label: target.label, description: target.limitation, target }));
  const selected = await vscode.window.showQuickPick([...choices, { label: 'Custom template…', description: 'Select a Didot template', target: undefined }], { title: 'Generate Schema Code' });
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
  uri: string; sourceUri: vscode.Uri; discovery: GenerationDiscovery; sourcePath: string;
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

async function prepare(request: Request, entry: TreeEntry | undefined, epoch: number, selectedNode?: string, preset?: TargetSelection): Promise<PreparedGeneration | undefined> {
  const source = sourceUri(entry);
  if (!source) return undefined;
  const uri = source.toString();
  await vscode.workspace.openTextDocument(source);
  const discovery = await request<GenerationDiscovery>('packata.schema.export.options', { uri, selectionKey: entry?.contract.generationKey });
  requireGenerationOptions(discovery);
  const asset = await chooseAsset(discovery, entry);
  if (!asset) return undefined;
  const options: Options = { assetId: asset.id };
  const template = await selectTemplate(discovery, source, options, preset);
  if (template === undefined) return undefined;
  if (!await customize(options, template !== null)) return undefined;
  return { uri, sourceUri: source, discovery, sourcePath: asset.sourcePath, options, template, epoch, selectedNode };
}

const bufferVersion = (uri: string): number | null => vscode.workspace.textDocuments.find(d => d.uri.toString() === uri)?.version ?? null;

async function render(request: Request, prepared: PreparedGeneration, token: vscode.CancellationToken,
  session: () => number, selection: () => string | undefined): Promise<void> {
  const { uri, discovery, options, template, sourcePath } = prepared;
  const result = await request<Generated>('packata.schema.export', { uri, version: discovery.version,
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
  if (!result.result.isSuccessful || typeof result.result.content !== 'string') {
    const error = result.result.diagnostics.find(d => d.severity === 'Error');
    void vscode.window.showErrorMessage(error?.message ?? 'Schema generation failed.', 'View findings').then(choice => choice && showFindings(result)); return;
  }
  if (result.result.diagnostics.some(d => d.severity === 'Warning'))
    void vscode.window.showWarningMessage('Schema code generated with representation warnings.', 'View findings').then(choice => choice && showFindings(result));
  await openGenerated(result, prepared.sourceUri, template);
}

export async function generateSchemaCode(request: Request, entry?: TreeEntry, session: () => number = () => 0,
  selection: () => string | undefined = () => undefined, preset?: TargetSelection): Promise<void> {
  try {
    const prepared = await prepare(request, entry, session(), selection(), preset);
    if (!prepared) return;
    await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Generating schema code…', cancellable: true }, async (_progress, token) => {
      try { await render(request, prepared, token, session, selection); }
      catch { if (!token.isCancellationRequested) void vscode.window.showErrorMessage('Unable to generate schema code. Check source/template access and server capabilities.'); }
    });
  } catch {
    void vscode.window.showErrorMessage('Schema generation is unavailable. Check the descriptor and template, or update the server for generation capabilities.');
  }
}
