import { AppError } from './http-error';

export interface Env {
  port: number;
  host: string;
  tempDir: string;
  llamaBaseUrl: string;
  llamaModel: string;
  allowRemoteUrls: boolean;
  keepDebugFiles: boolean;
  requestTimeoutMs: number;
  remoteFetchTimeoutMs: number;
  maxContentItems: number;
  maxFileBytes: number;
  maxTotalBytes: number;
  maxPdfPages: number;
  maxTotalImages: number;
  logLevel: string;
}

function parseInteger(value: string | undefined, fallback: number, name: string): number {
  if (value === undefined || value === '') {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    throw new AppError(500, 'invalid_environment', `${name} must be a positive integer.`);
  }

  return parsed;
}

function parseBoolean(value: string | undefined, fallback: boolean): boolean {
  if (value === undefined || value === '') {
    return fallback;
  }

  return value.toLowerCase() === 'true';
}

export function loadEnv(source: NodeJS.ProcessEnv = process.env): Env {
  return {
    port: parseInteger(source.PORT, 9998, 'PORT'),
    host: source.HOST || '0.0.0.0',
    tempDir: source.TEMP_DIR || '/tmp/invoice-extraction',
    llamaBaseUrl: (source.LLAMA_BASE_URL || 'http://localhost:8080').replace(/\/+$/, ''),
    llamaModel: source.LLAMA_MODEL || 'GLM-OCR',
    allowRemoteUrls: parseBoolean(source.ALLOW_REMOTE_URLS, true),
    keepDebugFiles: parseBoolean(source.KEEP_DEBUG_FILES, true),
    requestTimeoutMs: parseInteger(source.REQUEST_TIMEOUT_MS, 120000, 'REQUEST_TIMEOUT_MS'),
    remoteFetchTimeoutMs: parseInteger(source.REMOTE_FETCH_TIMEOUT_MS, 30000, 'REMOTE_FETCH_TIMEOUT_MS'),
    maxContentItems: parseInteger(source.MAX_CONTENT_ITEMS, 10, 'MAX_CONTENT_ITEMS'),
    maxFileBytes: parseInteger(source.MAX_FILE_BYTES, 10 * 1024 * 1024, 'MAX_FILE_BYTES'),
    maxTotalBytes: parseInteger(source.MAX_TOTAL_BYTES, 50 * 1024 * 1024, 'MAX_TOTAL_BYTES'),
    maxPdfPages: parseInteger(source.MAX_PDF_PAGES, 20, 'MAX_PDF_PAGES'),
    maxTotalImages: parseInteger(source.MAX_TOTAL_IMAGES, 40, 'MAX_TOTAL_IMAGES'),
    logLevel: source.LOG_LEVEL || 'info',
  };
}
