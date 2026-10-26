import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

// Use an absolute SDK host rather than searching PATH or the working directory.
export function dotnetExecutable() {
  let defaultRoot = '/usr/share/dotnet';
  if (process.platform === 'win32') defaultRoot = path.join(process.env.ProgramFiles ?? 'C:/Program Files', 'dotnet');
  if (process.platform === 'darwin') defaultRoot = '/usr/local/share/dotnet';
  const host = process.env.DOTNET_HOST_PATH ?? path.join(process.env.DOTNET_ROOT ?? defaultRoot, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
  assert(path.isAbsolute(host) && fs.existsSync(host), 'Set DOTNET_HOST_PATH to the absolute path of your installed dotnet executable');
  return host;
}
