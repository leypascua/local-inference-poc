import { access, mkdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { constants as fsConstants } from 'node:fs';

export async function ensureTempRoot(tempDir: string): Promise<void> {
  await mkdir(tempDir, { recursive: true });
  await access(tempDir, fsConstants.R_OK | fsConstants.W_OK);
}

export async function createRequestTempDir(tempDir: string, requestId: string): Promise<string> {
  await ensureTempRoot(tempDir);
  const requestDir = path.join(tempDir, requestId);
  await mkdir(requestDir, { recursive: true });
  return requestDir;
}

export async function removeRequestTempDir(requestDir: string): Promise<void> {
  await rm(requestDir, { recursive: true, force: true });
}
