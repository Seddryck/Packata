// Run after building the CLI. Exercises the downloadable documentation examples.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { dotnetExecutable } from './Documentation-Tools.mjs';

const framework = process.argv[2] ?? 'net10.0';
assert(/^net(8|9|10)\.0$/.test(framework), 'Choose net8.0, net9.0 or net10.0');
const dll = path.resolve('src/Packata.Cli/bin/Debug', framework, 'packata.dll');
const samples = path.resolve('docs/samples/odcs-validation');
const cases = [
  ['valid', 0],
  ['invalid-enum', 1, 'CLI001'],
  ['missing-status', 1, 'ODCSV001', '$.status', 'A non-empty value is required.'],
  ['unknown-field', 0],
  ['older-version', 1, 'ODCSV003', '$.apiVersion', 'Supported ODCS apiVersion is v3.2.0.'],
  ['missing-schema', 0],
  ['missing-vector-options', 1, 'CLI001'],
];
for (const [name, exit, code, sourcePath, message] of cases) {
  const file = path.join(samples, name + '.yaml');
  const before = fs.readFileSync(file);
  const result = spawnSync(dotnetExecutable(), [dll, 'contract', 'validate', file,
    '--input-format', 'odcs', '--output-format', 'json'], {
    encoding: 'utf8', windowsHide: true,
    env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' },
  });
  assert.equal(result.status, exit, `${name}: ${result.stdout}\n${result.stderr}`);
  const report = JSON.parse(result.stdout);
  assert.equal(report.valid, exit === 0, name);
  if (code) {
    assert.equal(report.diagnostics.length, 1, name);
    assert.equal(report.diagnostics[0].code, code, name);
    if (sourcePath) assert.equal(report.diagnostics[0].sourcePath, sourcePath, name);
    if (message) assert.equal(report.diagnostics[0].message, message, name);
  } else assert.deepEqual(report.diagnostics, [], name);
  assert.deepEqual(fs.readFileSync(file), before, `${name}: source changed`);
}
console.log(`Verified ${cases.length} ODCS validation examples on ${framework}.`);
