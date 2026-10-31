import * as path from 'node:path';
import * as vscode from 'vscode';
import { requireSchema } from './protocol';
import { TreeEntry } from './tree';

interface Format { id: string; label: string; representation: 'text' | 'binary'; extension: string; profiles: string[] }
interface Discovery {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string; formats: Format[];
  assets: { id: string; name: string; sourcePath: string; eligible: boolean }[];
  diagnostics?: { severity: string; message: string }[];
}
interface Result {
  schemaVersion: number; uri: string; version: number | null; selectionKey: string;
  result: { isSuccessful: boolean; representation: string; bytes?: string; content?: string; suggestedFilename?: string;
    diagnostics: { severity: string; message: string }[] };
}
type Request = <T>(command: string, args: object, token?: vscode.CancellationToken) => Promise<T>;

export function artifactBytes(value: string): Uint8Array {
  if (!value.length || value.length > 32 * 1024 * 1024 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)) throw new Error('Invalid schema artifact.');
  const bytes = Buffer.from(value, 'base64');
  if (bytes.toString('base64') !== value) throw new Error('Invalid schema artifact.');
  return bytes;
}

export async function exportSchemaDocument(request: Request, entry?: TreeEntry, preferredFormat?: string,
  session: () => number = () => 0, selection: () => string | undefined = () => undefined): Promise<void> {
  try {
    if (!vscode.workspace.isTrusted) throw new Error('A trusted workspace is required.');
    const identity = entry?.contract.uri ?? vscode.window.activeTextEditor?.document.uri.toString();
    if (!identity) return;
    const uri = vscode.Uri.parse(identity);
    if (uri.scheme !== 'file') return;
    const source = await vscode.workspace.openTextDocument(uri);
    const epoch = session(), selected = selection();
    const discovery = await request<Discovery>('packata.schema.document.options', { uri: identity });
    requireSchema(discovery);
    if (entry?.contract.generationKey && entry.contract.generationKey !== discovery.selectionKey) return;
    const formats = discovery.formats;
    const format = preferredFormat ? formats.find(f => f.id === preferredFormat)
      : (await vscode.window.showQuickPick(formats.map(f => ({ label: f.label, description: f.representation, format: f })), { title: 'Export Schema' }))?.format;
    if (!format) return;
    const assets = discovery.assets.filter(a => a.eligible && (entry?.node.kind !== 'asset' || a.sourcePath === entry.node.sourcePath));
    if (!assets.length) {
      void vscode.window.showErrorMessage(discovery.diagnostics?.find(d => d.severity === 'Error')?.message ?? 'No eligible declared schema. Inspect the contract mapping diagnostics.'); return;
    }
    const asset = assets.length === 1 ? assets[0]
      : (await vscode.window.showQuickPick(assets.map(a => ({ label: a.name, description: a.id, asset: a })), { title: 'Select an asset' }))?.asset;
    if (!asset) return;
    const version = source.version;
    const current = () => source.version === version && discovery.version === version && epoch === session() && selected === selection();
    await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Exporting schema…', cancellable: true }, async (_progress, token) => {
      const response = await request<Result>('packata.schema.document.export', { uri: identity, version: discovery.version,
        selectionKey: discovery.selectionKey, sourcePath: asset.sourcePath, options: { outputFormat: format.id, assetId: asset.id } }, token);
      requireSchema(response);
      if (token.isCancellationRequested || !current() || response.uri !== identity || response.selectionKey !== discovery.selectionKey || response.version !== version) return;
      if (!response.result.isSuccessful) {
        void vscode.window.showErrorMessage(response.result.diagnostics.find(d => d.severity === 'Error')?.message ?? 'Schema export failed.'); return;
      }
      if (response.result.representation !== format.representation) throw new Error('Unexpected schema representation.');
      if (format.representation === 'text') {
        if (typeof response.result.content !== 'string') throw new Error('Missing schema content.');
        const preview = await vscode.workspace.openTextDocument({ content: response.result.content, language: 'json' });
        if (token.isCancellationRequested || !current()) return;
        await vscode.window.showTextDocument(preview, { preview: false });
        return;
      }
      const suggested = response.result.suggestedFilename ?? 'schema' + format.extension;
      if (suggested.includes('/') || suggested.includes('\\') || suggested === '..') throw new Error('Invalid artifact filename.');
      const output = await vscode.window.showSaveDialog({ title: 'Save ' + format.label + ' schema', defaultUri: uri.with({ path: path.posix.join(path.posix.dirname(uri.path), suggested) }) });
      if (!output || output.scheme !== 'file' || token.isCancellationRequested || !current()) return;
      if (output.toString() === uri.toString()) throw new Error('Choose a new output file.');
      const bytes = artifactBytes(response.result.bytes ?? '');
      const temporary = output.with({ path: output.path + '.packata-' + Date.now() + '-' + Math.random().toString(16).slice(2) + '.tmp' });
      try {
        await vscode.workspace.fs.writeFile(temporary, bytes);
        if (token.isCancellationRequested || !current()) return;
        await vscode.workspace.fs.rename(temporary, output, { overwrite: false });
      } finally { try { await vscode.workspace.fs.delete(temporary); } catch { /* Already moved, or no file was created. */ } }
      if (response.result.diagnostics.some(d => d.severity === 'Warning')) void vscode.window.showWarningMessage('Schema saved with representation warnings. Review contract constraints separately.');
      else void vscode.window.showInformationMessage('Schema saved.');
    });
  } catch { void vscode.window.showErrorMessage('Unable to export schema. Choose a new output file and check source/server compatibility and permissions.'); }
}
