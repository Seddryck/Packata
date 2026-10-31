import * as vscode from 'vscode';
import { LanguageClient, State, TransportKind } from 'vscode-languageclient/node';
import { ContractNode, ContractState, Discovery, Inspection, requireCapabilities, requireDiscovery, requireSchema } from './protocol';
import { resolveServer } from './serverPath';
import { ContractTree, TreeEntry, restoreViewState } from './tree';
import { ViewMode, ViewPreference } from './viewMode';
import { ValidationUi } from './validationUi';
import { previewResource } from './preview';
import { generateSchemaCode, GenerationDiscovery } from './generation';
import { exportSchemaDocument } from './schemaDocuments';

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  const state = new ContractState();
  const tree = new ContractTree(state);
  const preference = new ViewPreference(context.workspaceState);
  tree.mode = preference.mode;
  const view = vscode.window.createTreeView('packata.contracts', { treeDataProvider: tree, showCollapseAll: true });
  const expanded = new Map<string, TreeEntry>();
  const describeMode = () => { view.description = `View as: ${tree.mode === 'native' ? 'Native' : 'Canonical'}`; };
  describeMode();
  context.subscriptions.push(
    view.onDidExpandElement(event => expanded.set(event.element.node.id, event.element)),
    view.onDidCollapseElement(event => expanded.delete(event.element.node.id)),
    vscode.commands.registerCommand('packata.selectView', async (requested?: ViewMode) => {
      const selected = requested ?? await vscode.window.showQuickPick([
        { label: 'Canonical', mode: 'canonical' as const, description: 'Assets, columns and constraints' },
        { label: 'Native', mode: 'native' as const, description: 'Original ODCS or Data Package properties' }
      ], { title: 'View as', placeHolder: `Current: ${tree.mode}` }).then(item => item?.mode);
      if (selected !== 'canonical' && selected !== 'native') return;
      await preference.set(selected);
      const selection = [...view.selection];
      const expansion = [...expanded.values()];
      expanded.clear();
      tree.mode = selected; tree.refresh(); describeMode();
      await restoreViewState(tree, view, expansion, selection[0]);
    })
  );
  const output = vscode.window.createOutputChannel('Packata', { log: true });
  context.subscriptions.push(tree, view, output);
  const config = () => vscode.workspace.getConfiguration('packata');
  const executable = resolveServer(context.extensionPath, config().get<string>('languageServer.path', ''));
  const watcher = vscode.workspace.createFileSystemWatcher('**/*.{json,yaml,yml}');
  context.subscriptions.push(watcher);
  const pendingOpenNotifications = new Set<Promise<void>>();
  client = new LanguageClient('packataLanguageServer', 'Packata Language Server', {
    command: executable, transport: TransportKind.stdio, options: { shell: false }
  }, {
    documentSelector: [{ scheme: 'file', pattern: '**/*.{json,yaml,yml}' }],
    outputChannel: output,
    synchronize: { fileEvents: watcher },
    middleware: {
      didOpen(document, next) {
        const pending = next(document);
        pendingOpenNotifications.add(pending);
        void pending.finally(() => pendingOpenNotifications.delete(pending)).catch(() => undefined);
        return pending;
      },
      async executeCommand(command, args, next) {
        await Promise.all(pendingOpenNotifications);
        return next(command, args);
      }
    },
    initializationOptions: { packata: { schemaVersions: [1] } }
  });
  const connection = client;
  // Negotiate the fixed Packata operations during initialize, before UI commands can run.
  connection.registerFeature({
    fillClientCapabilities(capabilities) {
      if (capabilities.workspace?.executeCommand) capabilities.workspace.executeCommand.dynamicRegistration = false;
      if (capabilities.textDocument?.synchronization) capabilities.textDocument.synchronization.dynamicRegistration = false;
    },
    initialize() {}, clear() {}, getState: () => ({ kind: 'static' })
  });
  context.subscriptions.push(connection);
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let refreshCancellation: vscode.CancellationTokenSource | undefined;
  let generation = 0;
  let documentEpoch = 0;
  let serverSession = 0;
  const reportedDiagnostics = new Map<string, string>();
  const versions = () => new Map(vscode.workspace.textDocuments.map(document => [document.uri.toString(), document.version]));
  const compatible = () => requireCapabilities(connection.initializeResult?.capabilities.executeCommandProvider?.commands);

  async function request<T>(command: string, args: object, outer?: vscode.CancellationToken): Promise<T> {
    compatible();
    if (!connection.initializeResult?.capabilities.executeCommandProvider?.commands.includes(command)) {
      throw new Error('The server does not advertise this Packata operation. Update the extension and server together.');
    }
    // Initial visible-buffer notifications are asynchronous in vscode-languageclient. Await
    // them before custom workspace operations, especially immediately after reconnecting.
    await Promise.all(pendingOpenNotifications);
    const cancellation = new vscode.CancellationTokenSource();
    const subscription = outer?.onCancellationRequested(() => cancellation.cancel());
    if (outer?.isCancellationRequested) cancellation.cancel();
    let timeout: ReturnType<typeof setTimeout> | undefined;
    try {
      const result = await Promise.race([
        connection.sendRequest<T>('workspace/executeCommand', { command, arguments: [{ ...args, schemaVersion: 1 }] }, cancellation.token),
        new Promise<never>((_, reject) => {
          timeout = setTimeout(() => { cancellation.cancel(); reject(new Error('Packata request timed out.')); }, config().get<number>('requestTimeout', 30000));
        })
      ]);
      // Canonicalize .NET file URI spelling (notably Windows drive colons) to VS Code's spelling.
      return JSON.parse(JSON.stringify(result), (key, value) =>
        (key === 'uri' || key === 'baseUri') && typeof value === 'string' ? vscode.Uri.parse(value).toString() : value) as T;
    } finally { clearTimeout(timeout); subscription?.dispose(); cancellation.dispose(); }
  }
  const failure = (message: string) => {
    // Transport errors can contain payloads; never print raw error objects.
    view.message = message;
    output.appendLine(message);
  };
  async function documentCapabilities(uri: string | undefined, token: vscode.CancellationToken): Promise<Record<string, boolean>> {
    const unavailable = { 'packata.schemaDocumentsAvailable': false, 'packata.arrowIpcAvailable': false };
    if (!uri || !connection.initializeResult?.capabilities.executeCommandProvider?.commands.includes('packata.schema.document.options')) return unavailable;
    try {
      const catalog = await request<{ formats: { id: string; representation: string }[] }>('packata.schema.document.options', { uri }, token);
      return { 'packata.schemaDocumentsAvailable': catalog.formats.length > 0,
        'packata.arrowIpcAvailable': catalog.formats.some(f => f.id === 'arrow-ipc' && f.representation === 'binary') };
    } catch { return unavailable; }
  }
  async function generationCapabilities(uri: string | undefined, token: vscode.CancellationToken): Promise<Record<string, boolean>> {
    const unavailable = { 'packata.schemaGenerationAvailable': false, 'packata.arrowCsharpAvailable': false, 'packata.arrowPythonAvailable': false };
    if (!uri || !connection.initializeResult?.capabilities.executeCommandProvider?.commands.includes('packata.schema.export.options')) return unavailable;
    try {
      const discovery = await request<GenerationDiscovery>('packata.schema.export.options', { uri }, token);
      return { 'packata.schemaGenerationAvailable': discovery.catalog.contextVersion === 1,
        'packata.arrowCsharpAvailable': discovery.catalog.targets.some(t => t.id === 'arrow' && t.language === 'csharp'),
        'packata.arrowPythonAvailable': discovery.catalog.targets.some(t => t.id === 'arrow' && t.language === 'python') };
    } catch { return unavailable; }
  }
  async function updateSchemaCapabilities(currentGeneration: number, token: vscode.CancellationToken): Promise<void> {
    const uri = state.contracts[0]?.uri;
    const [documents, code] = await Promise.all([documentCapabilities(uri, token), generationCapabilities(uri, token)]);
    if (token.isCancellationRequested || currentGeneration !== generation) return;
    for (const [key, value] of Object.entries({ ...documents, ...code })) await vscode.commands.executeCommand('setContext', key, value);
  }
  async function refresh(): Promise<void> {
    if (stopped || connection.state !== State.Running) return;
    refreshCancellation?.cancel(); refreshCancellation?.dispose();
    refreshCancellation = new vscode.CancellationTokenSource();
    const cancellation = refreshCancellation;
    const epoch = state.begin();
    const currentGeneration = ++generation;
    try {
      const response = await request<Discovery>('packata.workspace.discover', {
        workspaceFolders: vscode.workspace.workspaceFolders?.map(folder => folder.uri.toString()) ?? []
      }, cancellation.token);
      requireDiscovery(response);
      if (cancellation.token.isCancellationRequested || !state.apply(epoch, response, versions())) return;
      tree.refresh();
      validation.refreshAction();
      await updateSchemaCapabilities(currentGeneration, cancellation.token);
      if (cancellation.token.isCancellationRequested || currentGeneration !== generation) return;
      for (const contract of state.contracts) {
        const signature = JSON.stringify(contract.diagnostics);
        if (reportedDiagnostics.get(contract.uri) !== signature) {
          for (const diagnostic of contract.diagnostics) output.appendLine(`Packata ${diagnostic.code ?? ''} ${diagnostic.severity} ${diagnostic.sourcePath ?? '$'}: ${diagnostic.message}`);
          reportedDiagnostics.set(contract.uri, signature);
        }
      }
      view.message = state.contracts.length ? undefined : 'No supported contracts found. Add an ODCS or Data Package v2 descriptor, then refresh.';
    } catch {
      if (!cancellation.token.isCancellationRequested && currentGeneration === generation) {
        state.clear(); tree.refresh();
        failure('Unable to refresh contracts. Check the server installation/version, then run Packata: Restart Language Server.');
      }
    }
  }
  function schedule(): void {
    ++documentEpoch;
    state.begin(); // Invalidate in-flight replies immediately, before the debounce finishes.
    refreshCancellation?.cancel();
    clearTimeout(timer);
    timer = setTimeout(() => void refresh(), 250);
  }
  function uri(entry?: TreeEntry): string {
    const target = entry?.contract.uri ?? vscode.window.activeTextEditor?.document.uri.toString();
    if (!target) throw new Error('Open or select a contract first.');
    return target;
  }
  function current(result: { uri: string; version: number | null }): boolean {
    const version = versions().get(result.uri);
    return version === undefined ? result.version == null : version === result.version;
  }
  const command = (name: string, handler: (entry?: TreeEntry) => unknown) => context.subscriptions.push(
    vscode.commands.registerCommand(name, async (entry?: TreeEntry) => {
      try { return await handler(entry); }
      catch { void vscode.window.showErrorMessage('Packata operation failed. Check the selected contract and server compatibility, or restart the language server.'); }
    })
  );
  command('packata.refresh', refresh);
  command('packata.preview', entry => previewResource(request, entry));
  command('packata.generateSchemaCode', entry => generateSchemaCode(request, entry, () => serverSession, () => view.selection[0]?.node.id));
  command('packata.generateArrowCsharp', entry => generateSchemaCode(request, entry, () => serverSession, () => view.selection[0]?.node.id, { target: 'arrow', language: 'csharp' }));
  command('packata.generateArrowPython', entry => generateSchemaCode(request, entry, () => serverSession, () => view.selection[0]?.node.id, { target: 'arrow', language: 'python' }));
  command('packata.exportSchemaDocument', entry => exportSchemaDocument(request, entry, undefined, () => serverSession, () => view.selection[0]?.node.id));
  command('packata.exportArrowSchema', entry => exportSchemaDocument(request, entry, 'arrow-ipc', () => serverSession, () => view.selection[0]?.node.id));
  command('packata.restart', async () => { state.clear(); tree.validation.clear(); tree.refresh(); await connection.restart(); await Promise.all(pendingOpenNotifications); compatible(); schedule(); });
  command('packata.copyPath', entry => entry && vscode.env.clipboard.writeText(tree.mode === 'native' ? entry.node.sourcePath ?? entry.node.logicalPath : entry.node.logicalPath));
  command('packata.openSource', async entry => vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.parse(uri(entry)))));
  command('packata.revealSource', async entry => {
    if (!entry?.node.location) return;
    const document = await vscode.workspace.openTextDocument(vscode.Uri.parse(entry.node.location.uri));
    const epoch = documentEpoch;
    // Opening a disk-only document creates a buffer version. Reinspect after didOpen so the
    // revealed range belongs to that buffer, including changes made since the tree loaded.
    const result = await request<Inspection>('packata.contract.inspect', { uri: document.uri.toString() });
    requireSchema(result);
    if (epoch !== documentEpoch || !current(result)) { schedule(); return; }
    const find = (node: ContractNode): ContractNode | undefined => node.id === entry.node.id ? node : node.children.map(find).find(Boolean);
    const location = (result.tree && find(result.tree)?.location) ?? (result.nativeTree && find(result.nativeTree)?.location);
    if (!location) { schedule(); return; }
    const range = new vscode.Range(location.range.start.line, location.range.start.character, location.range.end.line, location.range.end.character);
    const editor = await vscode.window.showTextDocument(document, { selection: range });
    editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
  });
  command('packata.inspect', async entry => {
    const epoch = documentEpoch;
    const result = await request<Inspection>('packata.contract.inspect', { uri: uri(entry) });
    requireSchema(result);
    if (epoch !== documentEpoch || !current(result)) { schedule(); return; }
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ language: 'json', content: JSON.stringify(result, null, 2) }));
  });
  const validation = new ValidationUi(context, request, tree);
  context.subscriptions.push(validation);
  const invalidate = (document: vscode.TextDocument) => { validation.markOutdated(document.uri); schedule(); };
  context.subscriptions.push(
    watcher.onDidCreate(schedule), watcher.onDidDelete(() => { tree.validation.clear(); schedule(); }),
    watcher.onDidChange(uri => {
      if (!vscode.workspace.textDocuments.some(document => document.uri.toString() === uri.toString())) validation.markOutdated(uri);
      schedule();
    }),
    vscode.workspace.onDidChangeWorkspaceFolders(schedule),
    vscode.workspace.onDidOpenTextDocument(schedule), vscode.workspace.onDidCloseTextDocument(schedule),
    vscode.workspace.onDidChangeTextDocument(event => { if (event.contentChanges.length) invalidate(event.document); }),
    vscode.workspace.onDidSaveTextDocument(schedule),
    vscode.window.onDidChangeActiveTextEditor(editor => {
      const entry = editor && tree.root(editor.document.uri.toString());
      if (entry) void view.reveal(entry, { select: true, focus: false, expand: false }).then(undefined, () => undefined);
    }),
    connection.onDidChangeState(event => {
      ++serverSession;
      for (const key of ['packata.schemaDocumentsAvailable', 'packata.arrowIpcAvailable', 'packata.schemaGenerationAvailable', 'packata.arrowCsharpAvailable', 'packata.arrowPythonAvailable'])
        void vscode.commands.executeCommand('setContext', key, false);
      if (event.newState === State.Running) { tree.validation.clear(); schedule(); }
      else { state.clear(); tree.refresh(); failure('Packata language server is disconnected. Restart it to refresh contracts.'); }
    }),
    { dispose: () => { stopped = true; clearTimeout(timer); refreshCancellation?.cancel(); refreshCancellation?.dispose(); } }
  );
  view.message = 'Starting Packata language server…';
  try { await connection.start(); compatible(); schedule(); }
  catch { failure('Cannot start a compatible Packata language server. Install the correct VSIX or check packata.languageServer.path.'); }
}

export async function deactivate(): Promise<void> { await client?.stop(); client = undefined; }
