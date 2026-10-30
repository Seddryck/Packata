import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { runTests } from '@vscode/test-electron';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = `${process.platform}-${process.arch}`;
const extension = path.join(root, 'artifacts', `staging-${target}`);
if (!fs.existsSync(path.join(extension, 'server'))) throw new Error('Package the extension for this platform before running host tests.');
const workspace = path.join(root, 'artifacts', 'host-workspace');
fs.mkdirSync(workspace, { recursive: true });
for (const folder of ['first', 'second']) fs.mkdirSync(path.join(workspace, folder), { recursive: true });
fs.writeFileSync(path.join(workspace, 'first', 'datapackage.yaml'), 'name: Sales\nresources:\n  - name: orders\n    path: missing.csv\n    schema:\n      fields:\n        - name: id\n          type: integer\n');
fs.writeFileSync(path.join(workspace, 'second', 'contract.yaml'), 'apiVersion: v3.2.0\nkind: DataContract\nid: customers\nname: Customers\nversion: 1.0.0\nstatus: active\nschema:\n  - name: customers\n    properties:\n      - name: id\n        logicalType: integer\n');
fs.writeFileSync(path.join(workspace, 'first', 'unrelated.json'), '{"name":"unrelated"}');
fs.writeFileSync(path.join(workspace, 'second', 'bar.template.cs'), '{{ model.asset.name }}');
const workspaceFile = path.join(workspace, 'smoke.code-workspace');
fs.writeFileSync(workspaceFile, JSON.stringify({ folders: [{ path: 'first' }, { path: 'second' }], settings: { 'security.workspace.trust.enabled': false, 'workbench.startupEditor': 'none' } }));
const profile = fs.mkdtempSync(path.join(root, 'artifacts', 'host-profile-'));
await runTests({
  version: '1.137.0', cachePath: path.join(root, 'artifacts', 'vscode-test'),
  extensionDevelopmentPath: extension, extensionTestsPath: path.join(root, 'test', 'host.cjs'),
  extensionTestsEnv: { PACKATA_SMOKE_WORKSPACE: workspace },
  launchArgs: [workspaceFile, '--disable-gpu', '--disable-workspace-trust', '--no-sandbox', '--skip-welcome',
    '--user-data-dir', profile, '--extensions-dir', path.join(root, 'artifacts', 'host-extensions')]
});
