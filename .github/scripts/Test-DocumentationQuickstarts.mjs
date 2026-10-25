// Validate the upcoming source version, not an older published NuGet/tool release.
// Prerequisite: build src/Packata.Cli for the selected framework (default net10.0).
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';

const root = process.cwd();
const framework = process.argv[2] ?? 'net10.0';
assert(/^net(8|9|10)\.0$/.test(framework), 'Choose net8.0, net9.0 or net10.0');
const scratch = path.join(root, 'artifacts', 'docs-quickstart-' + randomUUID());
fs.mkdirSync(scratch, { recursive: true });
for (const name of ['datapackage.json', 'customers.csv']) fs.copyFileSync(path.join(root, 'docs/samples/quickstart', name), path.join(scratch, name));
const dll = path.join(root, 'src/Packata.Cli/bin/Debug', framework, 'packata.dll');
const run = (args, expected = 0) => {
  const result = spawnSync('dotnet', args, { cwd: scratch, encoding: 'utf8', windowsHide: true, env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', PACKATA_CONFIG_HOME: path.join(scratch, 'config') } });
  assert.equal(result.status, expected, `${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
};
const cli = (...args) => run([dll, ...args]);
const digest = name => createHash('sha256').update(fs.readFileSync(path.join(scratch, name))).digest('hex');
const before = ['datapackage.json', 'customers.csv'].map(digest);
assert.match(cli('contract', 'validate', 'datapackage.json'), /Contract is valid/);
const inspection = JSON.parse(cli('contract', 'inspect', 'datapackage.json', '--asset', 'customers', '--output', 'json'));
assert.equal(inspection.contract.assets[0].id, 'customers');
assert.equal(inspection.contract.endpoints[0].id, 'customers-endpoint');
const sample = JSON.parse(cli('data', 'preview', 'datapackage.json', '--asset', 'customers', '--limit', '2', '--output', 'json'));
assert.deepEqual(sample, { schemaVersion: 1, columns: ['id', 'name'], rows: [[1, 'Alice'], [2, 'Bob']] });
assert.match(cli('data', 'preview', 'datapackage.json', '--asset', 'customers', '--columns', 'name,id', '--limit', '2', '--output', 'csv'), /name,id\r?\nAlice,1/);
const schema = JSON.parse(cli('export', 'datapackage.json', '--format', 'json-schema', '--dataset', 'customers'));
assert.equal(schema.properties.id.type, 'integer');
assert.equal(schema.properties.name.type, 'string');
const dialect = JSON.parse(cli('dialect', 'export', 'datapackage.json', '--endpoint', 'customers-endpoint'));
assert.equal(dialect.delimiter, ',');
assert.equal(dialect.header, true);
assert.equal(dialect.lineTerminator, '\n');
const diff = JSON.parse(cli('contract', 'diff', 'datapackage.json', 'datapackage.json', '--output', 'json'));
assert.equal(diff.hasChanges, false);
assert.deepEqual(diff.changes, []);
assert.match(cli('schema', 'infer', 'datapackage.json', '--asset', 'customers', '--header', 'present', '--maximum-records', '100'), /Inferred asset: customers/);
assert.match(cli('infer', 'datapackage.json', '--asset', 'customers', '--header', 'present', '--output', 'enriched.json'), /Written:/);
assert(fs.existsSync(path.join(scratch, 'enriched.json')));
run([dll, 'data', 'preview', 'datapackage.json', '--asset', 'customers', '--limit', '0'], 2);
assert.deepEqual(['datapackage.json', 'customers.csv'].map(digest), before, 'Read-only examples changed source inputs');

// Compile and execute the actual C# block in the published Quickstart. Source
// references allow validating unreleased APIs without changing the user's tools.
const markdown = fs.readFileSync(path.join(root, 'docs/quickstart-dotnet.md'), 'utf8');
const code = markdown.match(/```csharp\r?\n([\s\S]*?)```/)[1];
fs.writeFileSync(path.join(scratch, 'Program.cs'), code);
const xml = s => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
fs.writeFileSync(path.join(scratch, 'Quickstart.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>${framework}</TargetFramework><TargetFrameworks></TargetFrameworks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>${['Packata.DataPackage', 'Packata.ResourceReaders'].map(n => `<ProjectReference Include="${xml(path.join(root, 'src', n, n + '.csproj'))}" />`).join('')}</ItemGroup></Project>`);
run(['build', 'Quickstart.csproj', '--verbosity', 'quiet']);
assert.equal(run([path.join(scratch, 'bin/Debug', framework, 'Quickstart.dll')]).trim().replace(/\r\n/g, '\n'), '1: Alice\n2: Bob');
console.log(`CLI Quickstart, task examples, infer alias, failure exit code, source preservation and extracted .NET Quickstart passed (${framework}). Scratch: ${path.relative(root, scratch)}`);
