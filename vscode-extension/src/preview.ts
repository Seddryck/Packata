import * as vscode from 'vscode';
import { Inspection, requireSchema } from './protocol';
import { TreeEntry } from './tree';

export interface PreviewTarget { sourcePath: string; bindingIndex?: number | null; label: string; supported: boolean; reason?: string; selectionKey?: string }
export interface PreviewResult {
  schemaVersion: number; uri: string; version: number | null; limit: number;
  columns: { name: string; type: string }[]; rows: unknown[][]; diagnostic?: { message: string };
}
export function rowLimitError(value: string): string | undefined {
  return /^[1-9]\d*$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= 2147483647
    ? undefined : 'Enter a positive whole number (maximum 2147483647).';
}
const escape = (value: unknown): string => String(value ?? '').replace(/[&<>"']/g, character =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!);
export function previewHtml(result: PreviewResult): string {
  return `<!DOCTYPE html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
  <style>body{font-family:var(--vscode-font-family);color:var(--vscode-editor-foreground);background:var(--vscode-editor-background)}
  table{border-collapse:collapse}th,td{padding:6px 12px;border:1px solid var(--vscode-panel-border);text-align:left;white-space:pre-wrap}small{display:block;font-weight:normal}</style>
  </head><body><h1>Resource preview</h1><p>${result.rows.length} data rows · limit ${result.limit} · headers excluded</p>
  <table><thead><tr>${result.columns.map(column => `<th>${escape(column.name)}<small>${escape(column.type)}</small></th>`).join('')}</tr></thead>
  <tbody>${result.rows.map(row => `<tr>${row.map(value => `<td>${escape(typeof value === 'object' && value !== null ? JSON.stringify(value) : value)}</td>`).join('')}</tr>`).join('')}</tbody></table>
  ${result.rows.length === 0 ? '<p>No data rows.</p>' : ''}</body></html>`;
}

export async function previewResource(request: <T>(command: string, args: object, token?: vscode.CancellationToken) => Promise<T>,
  entry?: TreeEntry): Promise<void> {
  const uri = entry?.contract.uri ?? vscode.window.activeTextEditor?.document.uri.toString();
  if (!uri) { void vscode.window.showErrorMessage('Open or select a contract first.'); return; }
  const inspection = await request<Inspection>('packata.contract.inspect', { uri });
  requireSchema(inspection);
  const targets = (inspection.previewTargets ?? []).filter(target => !entry || entry.node.kind !== 'asset' || target.sourcePath === entry.node.sourcePath);
  const supported = targets.filter(target => target.supported);
  if (!supported.length) {
    void vscode.window.showErrorMessage(targets[0]?.reason ?? 'No supported file resources are available for preview.'); return;
  }
  const selected = supported.length === 1 ? supported[0] : (await vscode.window.showQuickPick(
    supported.map(target => ({ label: target.label, description: `${target.sourcePath} · binding ${(target.bindingIndex ?? 0) + 1}`, target })),
    { title: 'Select a resource binding to preview' }))?.target;
  if (!selected) return;
  const input = await vscode.window.showInputBox({ title: 'Maximum data rows to read', value: '20', validateInput: rowLimitError,
    prompt: 'Headers do not count. Some formats require staging or metadata reads.' });
  if (input === undefined) return;
  if (rowLimitError(input)) { void vscode.window.showErrorMessage(rowLimitError(input)!); return; }
  await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Loading resource preview…', cancellable: true },
    async (_progress, token) => {
      try {
        const result = await request<PreviewResult>('packata.resource.preview', {
          uri, sourcePath: selected.sourcePath, bindingIndex: selected.bindingIndex, selectionKey: selected.selectionKey, limit: Number(input)
        }, token);
        if (token.isCancellationRequested) return;
        requireSchema(result);
        if (result.diagnostic) { void vscode.window.showErrorMessage(result.diagnostic.message); return; }
        const current = vscode.workspace.textDocuments.find(document => document.uri.toString() === uri);
        if (current && current.version !== result.version) {
          void vscode.window.showWarningMessage('The descriptor changed during preview. Run preview again.'); return;
        }
        const panel = vscode.window.createWebviewPanel('packata.preview', 'Packata Resource Preview', vscode.ViewColumn.Beside, { enableScripts: false });
        panel.webview.html = previewHtml(result);
      } catch {
        if (!token.isCancellationRequested) void vscode.window.showErrorMessage('Unable to load the resource preview. Check access, format and server compatibility.');
      }
    });
}
