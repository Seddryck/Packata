import * as vscode from 'vscode';
import { TreeEntry, ContractTree } from './tree';
import { requireSchema } from './protocol';
import { ValidationPreference, ValidationOptions, ValidationReport, ValidatorOption, available,
  initialSelection, scopeLabel, selectionAvailable, validationSummary, checkLabel } from './validation';

type Target = TreeEntry | vscode.Uri | undefined;
type Request = <T>(command: string, args: object, token?: vscode.CancellationToken) => Promise<T>;

/** Explicit validation owns a scope; background syntax diagnostics never expand it. */
export class ValidationUi implements vscode.Disposable {
  private readonly preference: ValidationPreference;
  private readonly summary = vscode.window.createOutputChannel('Packata Validation');
  private readonly action = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 50);
  private readonly epochs = new Map<string, number>();
  private readonly options = new Map<string, ValidationOptions>();
  private readonly pending = new Map<string, vscode.CancellationTokenSource>();
  private readonly loadingOptions = new Set<string>();
  private readonly subscriptions: vscode.Disposable[] = [];

  constructor(context: vscode.ExtensionContext, private readonly request: Request, private readonly tree: ContractTree) {
    this.preference = new ValidationPreference(context.workspaceState);
    this.action.command = 'packata.runLastValidation';
    this.subscriptions.push(vscode.commands.registerCommand('packata.validate', (target?: Target) => this.run(target, true)),
      vscode.commands.registerCommand('packata.runLastValidation', (target?: Target) => this.run(target, false)),
      vscode.window.onDidChangeActiveTextEditor(() => this.updateAction()));
    this.updateAction();
  }

  private target(target: Target): vscode.Uri | undefined {
    if (target instanceof vscode.Uri) return target;
    return target ? vscode.Uri.parse(target.contract.uri) : vscode.window.activeTextEditor?.document.uri;
  }

  async run(target: Target, choose: boolean): Promise<void> {
    const uri = this.target(target);
    if (uri?.scheme !== 'file') return;
    const key = uri.toString();
    const epoch = (this.epochs.get(key) ?? 0) + 1;
    this.epochs.set(key, epoch);
    this.pending.get(key)?.cancel();
    const cancellation = new vscode.CancellationTokenSource();
    this.pending.set(key, cancellation);
    try {
      const prepared = await this.prepare(uri, choose);
      if (!prepared || this.epochs.get(key) !== epoch) return;
      await this.preference.set(prepared.options.format, prepared.selected);
      this.updateAction();
      await this.execute(uri, prepared.document, prepared.options, prepared.selected, epoch, cancellation);
    } catch {
      if (!cancellation.token.isCancellationRequested) this.executionError(uri);
    } finally {
      if (this.pending.get(key) === cancellation) this.pending.delete(key);
      cancellation.dispose();
    }
  }

  private async prepare(uri: vscode.Uri, choose: boolean): Promise<{
    document: vscode.TextDocument; options: ValidationOptions; selected: string[];
  } | undefined> {
      const key = uri.toString();
      const document = await vscode.workspace.openTextDocument(uri);
      const options = await this.request<ValidationOptions>('packata.validation.options', { uri: key });
      requireSchema(options);
      this.options.set(key, options);
      const saved = this.preference.get(options.format);
      if (saved && !selectionAvailable(saved, options)) {
        await vscode.window.showWarningMessage('Some remembered validation checks are unavailable for this format. Confirm an available selection.');
        choose = true;
      }
      const selected = choose || !saved ? await this.pick(uri, options, saved) : saved;
      if (!selected) { this.summary.appendLine(`${uri.fsPath}\nCancelled · no checks run; remembered scope unchanged.`); return; }
      if (!selectionAvailable(selected, options)) throw new Error('Invalid validation scope.');
      return { document, options, selected };
  }

  private async execute(uri: vscode.Uri, document: vscode.TextDocument, options: ValidationOptions,
    selected: string[], epoch: number, cancellation: vscode.CancellationTokenSource): Promise<void> {
      const key = uri.toString();
      const version = document.version;
      const result = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
        title: `Validate ${vscode.workspace.asRelativePath(uri)}: ${scopeLabel(options, selected)}`, cancellable: true },
      async (_, progressToken) => {
        const subscription = progressToken.onCancellationRequested(() => cancellation.cancel());
        try { return await this.perform(key, selected, cancellation.token); }
        finally { subscription.dispose(); }
      });
      if (!result) {
        if (this.epochs.get(key) !== epoch) return;
        this.tree.validation.set(key, { version, valid: false, summary: 'Cancelled · selected checks incomplete' });
        this.tree.refresh();
        this.summary.appendLine(`${uri.fsPath}\nCancelled · validation incomplete.`); return;
      }
      requireSchema(result);
      if (!Array.isArray(result.checks)) throw new Error('The server does not report validation scopes.');
      if (this.epochs.get(key) !== epoch || document.version !== version || result.version !== version) {
        this.markOutdated(uri); return;
      }
      const message = validationSummary(result);
      this.tree.validation.set(key, { version: result.version, valid: result.valid, summary: message });
      this.tree.refresh();
      this.summary.clear(); this.summary.appendLine(uri.fsPath);
      for (const check of result.checks) this.summary.appendLine(`${check.label.padEnd(16)}${checkLabel(check)}${check.reason ? ' · ' + check.reason : ''}`);
      this.summary.show(true);
      void vscode.window.showInformationMessage(`${vscode.workspace.asRelativePath(uri)} · ${message}`);
      this.updateAction();
  }

  private async perform(uri: string, selected: string[], token: vscode.CancellationToken): Promise<ValidationReport | undefined> {
    try {
      const result = await this.request<ValidationReport>('packata.contract.validate', { uri, checks: selected }, token);
      return token.isCancellationRequested ? undefined : result;
    } catch (error) {
      if (token.isCancellationRequested) return undefined;
      throw error;
    }
  }

  private executionError(uri: vscode.Uri): void {
      const key = uri.toString();
      const version = vscode.workspace.textDocuments.find(document => document.uri.toString() === key)?.version ?? null;
      this.tree.validation.set(key, { version, valid: false, summary: 'Execution error · validation incomplete' });
      this.tree.refresh();
      this.summary.appendLine(`${uri.fsPath}\nExecution error · check the language server connection and retry.`);
      void vscode.window.showErrorMessage('Packata validation could not complete. Check the language server connection and retry.');
  }

  private pick(uri: vscode.Uri, options: ValidationOptions, saved?: string[]): Promise<string[] | undefined> {
    const selected = initialSelection(options, saved);
    const picker = vscode.window.createQuickPick<vscode.QuickPickItem & { validator: ValidatorOption }>();
    picker.canSelectMany = true;
    picker.title = `Validate ${vscode.workspace.asRelativePath(uri)} · ${options.format === 'odcs' ? 'ODCS' : 'Data Package'}`;
    picker.placeholder = 'Select checks to run · Enter to validate · Escape to cancel';
    picker.items = available(options).map(validator => ({ label: validator.label, detail: validator.description, validator }));
    picker.selectedItems = picker.items.filter(item => selected.includes(item.validator.id));
    return new Promise(resolve => {
      let confirmed = false;
      const accept = picker.onDidAccept(() => {
        if (!picker.selectedItems.length) { picker.title = 'Select at least one validation check'; return; }
        confirmed = true;
        const ids = picker.selectedItems.map(item => item.validator.id);
        resolve(ids); picker.hide();
      });
      const hide = picker.onDidHide(() => {
        if (!confirmed) resolve(undefined);
        accept.dispose(); hide.dispose(); picker.dispose();
      });
      picker.show();
    });
  }

  markOutdated(uri: vscode.Uri): void {
    const key = uri.toString();
    this.epochs.set(key, (this.epochs.get(key) ?? 0) + 1);
    this.pending.get(key)?.cancel();
    const previous = this.tree.validation.get(key);
    if (previous) {
      previous.outdated = true;
      this.summary.appendLine(`${uri.fsPath}\nOutdated after edits · run validation again.`);
      this.tree.refresh();
    }
    this.updateAction();
  }

  private updateAction(): void {
    const document = vscode.window.activeTextEditor?.document;
    if (document?.uri.scheme !== 'file' || !/\.(json|ya?ml)$/i.test(document.uri.path)) { this.action.hide(); return; }
    const key = document.uri.toString();
    const options = this.options.get(key);
    if (!options && !this.loadingOptions.has(key)) this.loadActionOptions(key);
    const saved = options && this.preference.get(options.format);
    const scope = options && selectionAvailable(saved, options) ? scopeLabel(options, saved) : 'Select checks…';
    const outdated = this.tree.validation.get(key)?.outdated ? ' · Outdated' : '';
    this.action.text = `$(checklist) Validate: ${scope}${outdated}`;
    this.action.tooltip = `Validate: ${scope}${outdated}\nValidate this descriptor, not dataset records.`;
    this.action.show();
  }

  refreshAction(): void { this.updateAction(); }

  private loadActionOptions(uri: string): void {
    this.loadingOptions.add(uri);
    void this.request<ValidationOptions>('packata.validation.options', { uri }).then(options => {
      requireSchema(options);
      this.options.set(uri, options);
      this.updateAction();
    }).catch(() => undefined).finally(() => this.loadingOptions.delete(uri));
  }

  dispose(): void {
    for (const request of this.pending.values()) request.cancel();
    for (const subscription of this.subscriptions) subscription.dispose();
    this.action.dispose(); this.summary.dispose();
  }
}
