import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { isIP } from 'node:net';
import { AppError } from '../lib/http-error';
import { reserveUniqueFileName, sanitizeFileName } from '../lib/file-names';
import type { Env } from '../lib/env';
import type { InvoiceContentItem, SupportedContentType, SupportedImageContentType } from '../schemas/request';

export interface LoadedSourceFile {
  originalName: string;
  storedName: string;
  filePath: string;
  contentType: SupportedContentType;
  byteLength: number;
}

export interface PreparedImageFile {
  filePath: string;
  fileName: string;
  contentType: SupportedImageContentType | 'image/jpeg';
}

function isRemoteUrl(value: string): boolean {
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:';
  } catch {
    return false;
  }
}

function isBlockedIpAddress(hostname: string): boolean {
  const lower = hostname.toLowerCase();

  if (lower === 'localhost' || lower.endsWith('.local')) {
    return true;
  }

  if (!isIP(lower)) {
    return false;
  }

  if (lower.includes(':')) {
    return lower === '::1' || lower.startsWith('fc') || lower.startsWith('fd') || lower.startsWith('fe8') || lower.startsWith('fe9') || lower.startsWith('fea') || lower.startsWith('feb');
  }

  const octets = lower.split('.').map((part) => Number.parseInt(part, 10));
  const first = octets[0] ?? -1;
  const second = octets[1] ?? -1;

  return (
    first === 0 ||
    first === 10 ||
    first === 127 ||
    (first === 169 && second === 254) ||
    (first === 172 && second >= 16 && second <= 31) ||
    (first === 192 && second === 168)
  );
}

function validateRemoteUrl(url: URL): void {
  if (!(url.protocol === 'http:' || url.protocol === 'https:')) {
    throw new AppError(400, 'invalid_remote_url', 'Remote file_url must use http or https.');
  }

  if (url.username || url.password) {
    throw new AppError(400, 'invalid_remote_url', 'Remote file_url must not include credentials.');
  }

  if (isBlockedIpAddress(url.hostname)) {
    throw new AppError(400, 'blocked_remote_url', 'Remote file_url points to a blocked host.');
  }
}

function decodeBase64(input: string): Buffer {
  const normalized = input.replace(/^data:[^;]+;base64,/i, '').replace(/\s+/g, '');
  if (!normalized || !/^[A-Za-z0-9+/=]+$/.test(normalized)) {
    throw new AppError(400, 'invalid_base64', 'file_url must be raw base64 bytes or an http(s) URL.');
  }

  return Buffer.from(normalized, 'base64');
}

async function fetchRemoteBytes(item: InvoiceContentItem, env: Env): Promise<Buffer> {
  if (!env.allowRemoteUrls) {
    throw new AppError(400, 'remote_urls_disabled', 'Remote file_url values are disabled.');
  }

  const url = new URL(item.file_url);
  validateRemoteUrl(url);

  const response = await fetch(url, {
    method: 'GET',
    redirect: 'follow',
    signal: AbortSignal.timeout(env.remoteFetchTimeoutMs),
  }).catch((error: unknown) => {
    throw new AppError(502, 'remote_fetch_failed', `Unable to download ${item.name}.`, { cause: String(error) });
  });

  if (!response.ok) {
    throw new AppError(502, 'remote_fetch_failed', `Unable to download ${item.name}.`, {
      status: response.status,
    });
  }

  const contentLength = response.headers.get('content-length');
  if (contentLength) {
    const byteLength = Number.parseInt(contentLength, 10);
    if (Number.isFinite(byteLength) && byteLength > env.maxFileBytes) {
      throw new AppError(413, 'payload_too_large', `${item.name} exceeds MAX_FILE_BYTES.`);
    }
  }

  const arrayBuffer = await response.arrayBuffer();
  return Buffer.from(arrayBuffer);
}

async function resolveItemBytes(item: InvoiceContentItem, env: Env): Promise<Buffer> {
  return isRemoteUrl(item.file_url) ? fetchRemoteBytes(item, env) : decodeBase64(item.file_url);
}

export async function loadInputFiles(params: {
  env: Env;
  requestDir: string;
  items: InvoiceContentItem[];
}): Promise<LoadedSourceFile[]> {
  const { env, requestDir, items } = params;

  if (items.length > env.maxContentItems) {
    throw new AppError(413, 'payload_too_large', `content may not contain more than ${env.maxContentItems} item(s).`);
  }

  const seenNames = new Map<string, number>();
  const loadedFiles: LoadedSourceFile[] = [];
  let totalBytes = 0;

  for (const item of items) {
    const storedName = reserveUniqueFileName(sanitizeFileName(item.name), seenNames);
    const filePath = path.join(requestDir, storedName);
    const bytes = await resolveItemBytes(item, env);

    if (bytes.byteLength === 0) {
      throw new AppError(400, 'empty_file', `${item.name} did not contain any file bytes.`);
    }

    if (bytes.byteLength > env.maxFileBytes) {
      throw new AppError(413, 'payload_too_large', `${item.name} exceeds MAX_FILE_BYTES.`);
    }

    totalBytes += bytes.byteLength;
    if (totalBytes > env.maxTotalBytes) {
      throw new AppError(413, 'payload_too_large', 'Request exceeds MAX_TOTAL_BYTES.');
    }

    await writeFile(filePath, bytes);
    loadedFiles.push({
      originalName: item.name,
      storedName,
      filePath,
      contentType: item.content_type,
      byteLength: bytes.byteLength,
    });
  }

  return loadedFiles;
}
