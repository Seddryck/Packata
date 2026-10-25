// Validate the upcoming source version, not an older published NuGet/tool release.
// Prerequisite: build src/Packata.Cli for the selected framework (default net10.0).
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { dotnetExecutable } from './Documentation-Tools.mjs';

const root = process.cwd();
const framework = process.argv[2] ?? 'net10.0';
assert(/^net(8|9|10)\.0$/.test(framework), 'Choose net8.0, net9.0 or net10.0');
const scratch = path.join(root, 'artifacts', 'docs-quickstart-' + randomUUID());
fs.mkdirSync(scratch, { recursive: true });
for (const name of ['datapackage.json', 'customers.csv']) fs.copyFileSync(path.join(root, 'docs/samples/quickstart', name), path.join(scratch, name));
const dll = path.join(root, 'src/Packata.Cli/bin/Debug', framework, 'packata.dll');
const host = dotnetExecutable();
const run = (args, expected = 0) => {
  const result = spawnSync(host, args, { cwd: scratch, encoding: 'utf8', windowsHide: true, env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', PACKATA_CONFIG_HOME: path.join(scratch, 'config') } });
  assert.equal(result.status, expected, `${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
};
const cli = (...args) => run([dll, ...args]);
const digest = name => createHash('sha256').update(fs.readFileSync(path.join(scratch, name))).digest('hex');
const before = ['datapackage.json', 'customers.csv'].map(digest);
assert.match(cli('contract', 'validate', 'datapackage.json'), /Contract is valid/);
const inspection = JSON.parse(cli('contract', 'inspect', 'datapackage.json', '--asset', 'customers', '--output-format', 'json'));
assert.equal(inspection.contract.assets[0].id, 'customers');
assert.equal(inspection.contract.endpoints[0].id, 'customers-endpoint');
const sample = JSON.parse(cli('data', 'preview', 'datapackage.json', '--asset', 'customers', '--limit', '2', '--output-format', 'json'));
assert.deepEqual(sample, { schemaVersion: 1, columns: ['id', 'name'], rows: [[1, 'Alice'], [2, 'Bob']] });
assert.match(cli('data', 'preview', 'datapackage.json', '--asset', 'customers', '--columns', 'name,id', '--limit', '2', '--output-format', 'csv'), /name,id\r?\nAlice,1/);
const schema = JSON.parse(cli('schema', 'export', 'datapackage.json', '--output-format', 'json-schema', '--asset', 'customers'));
assert.equal(schema.properties.id.type, 'integer');
assert.equal(schema.properties.name.type, 'string');
assert.deepEqual(JSON.parse(cli('export', 'datapackage.json', '-f', 'json-schema', '--asset', 'customers')), schema);
const dialect = JSON.parse(cli('dialect', 'export', 'datapackage.json', '--endpoint', 'customers-endpoint'));
assert.equal(dialect.delimiter, ',');
assert.equal(dialect.header, true);
assert.equal(dialect.lineTerminator, '\n');
const diff = JSON.parse(cli('contract', 'diff', 'datapackage.json', 'datapackage.json', '--output-format', 'json'));
assert.equal(diff.hasChanges, false);
assert.deepEqual(diff.changes, []);
const compatibility = JSON.parse(cli('contract', 'compare', 'datapackage.json', 'datapackage.json', '--output-format', 'json', '--fail-on', 'none'));
assert.equal(compatibility.completed, true);
assert.equal(compatibility.direction, 'baseline-to-candidate');
assert.equal(compatibility.policy, 'baseline-consumers-v1');
assert.equal(compatibility.gatePassed, true);
assert.equal(compatibility.hasChanges, false);
const candidate = JSON.parse(fs.readFileSync(path.join(scratch, 'datapackage.json'), 'utf8'));
candidate.resources[0].schema.fields.pop();
fs.writeFileSync(path.join(scratch, 'candidate.json'), JSON.stringify(candidate));
run([dll, 'contract', 'compare', 'datapackage.json', 'candidate.json', '--output-format', 'json', '--fail-on', 'breaking', '-o', 'gate.json'], 4);
const gate = JSON.parse(fs.readFileSync(path.join(scratch, 'gate.json'), 'utf8'));
assert.equal(gate.completed, true);
assert.equal(gate.gatePassed, false);
assert.equal(gate.exitCode, 4);
cli('contract', 'compare', 'datapackage.json', 'candidate.json', '--fail-on', 'none', '--quiet', '-o', 'quiet.json');
assert(!fs.existsSync(path.join(scratch, 'quiet.json')));
assert.equal(cli('contract', 'inspect', 'datapackage.json', '--input-format', 'datapackage', '--output-format', 'json', '-o', 'inspection.json'), '');
assert.deepEqual(JSON.parse(fs.readFileSync(path.join(scratch, 'inspection.json'), 'utf8')).contract, inspection.contract);
cli('config', 'list', '--output-format', 'json', '-o', 'config.json');
assert(Array.isArray(JSON.parse(fs.readFileSync(path.join(scratch, 'config.json'), 'utf8'))));
cli('connector', 'list', '--output-format', 'json', '-o', 'connectors.json');
assert(Array.isArray(JSON.parse(fs.readFileSync(path.join(scratch, 'connectors.json'), 'utf8'))));
assert.match(cli('schema', 'infer', 'datapackage.json', '--asset', 'customers', '--header', 'present', '--maximum-records', '100'), /Inferred asset: customers/);
fs.copyFileSync(path.join(scratch, 'datapackage.json'), path.join(scratch, 'descriptor'));
assert.match(cli('schema', 'infer', 'descriptor', '--input-format', 'datapackage', '--asset', 'customers'), /Inferred asset: customers/);
assert.match(cli('infer', 'datapackage.json', '--asset', 'customers', '--header', 'present', '--output', 'enriched.json'), /Written:/);
assert(fs.existsSync(path.join(scratch, 'enriched.json')));
run([dll, 'data', 'preview', 'datapackage.json', '--asset', 'customers', '--limit', '0'], 2);
run([dll, 'contract', 'validate', 'datapackage.json', '--input-format', 'unsupported'], 2);
run([dll, 'schema', 'export', 'datapackage.json', '--output-format', 'unsupported', '--asset', 'customers'], 2);
assert.deepEqual(['datapackage.json', 'customers.csv'].map(digest), before, 'Read-only examples changed source inputs');

// Compile and execute the actual C# block in the published Quickstart. Source
// references allow validating unreleased APIs without changing the user's tools.
const markdown = fs.readFileSync(path.join(root, 'docs/quickstart-dotnet.md'), 'utf8');
const code = markdown.match(/```csharp\r?\n([\s\S]*?)```/)[1];
fs.writeFileSync(path.join(scratch, 'Program.cs'), code);
const xml = s => s.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
const references = ['Packata.DataPackage', 'Packata.ResourceReaders']
  .map(n => `<ProjectReference Include="${xml(path.join(root, 'src', n, n + '.csproj'))}" />`).join('');
fs.writeFileSync(path.join(scratch, 'Quickstart.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>${framework}</TargetFramework><TargetFrameworks></TargetFrameworks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>${references}</ItemGroup></Project>`);
run(['build', 'Quickstart.csproj', '--verbosity', 'quiet']);
assert.equal(run([path.join(scratch, 'bin/Debug', framework, 'Quickstart.dll')]).trim().replaceAll('\r\n', '\n'), '1: Alice\n2: Bob');
console.log(`CLI Quickstart, task examples, infer alias, failure exit code, source preservation and extracted .NET Quickstart passed (${framework}). Scratch: ${path.relative(root, scratch)}`);
