import path from 'node:path';

const SAFE_FILE_NAME_FALLBACK = 'file';

export function sanitizeFileName(input: string): string {
  const baseName = path.basename(input);
  const cleaned = baseName
    .replace(/[\u0000-\u001f\u007f]+/g, '')
    .replace(/[\\/]+/g, '_')
    .replace(/\s+/g, '_')
    .replace(/[^A-Za-z0-9._-]/g, '_')
    .replace(/_+/g, '_')
    .replace(/^\.+/, '')
    .replace(/^_+|_+$/g, '');

  return cleaned || SAFE_FILE_NAME_FALLBACK;
}

export function reserveUniqueFileName(fileName: string, seen: Map<string, number>): string {
  const parsed = path.parse(fileName);
  const normalizedKey = fileName.toLowerCase();
  const currentCount = seen.get(normalizedKey) ?? 0;

  if (currentCount === 0) {
    seen.set(normalizedKey, 1);
    return fileName;
  }

  let nextCount = currentCount + 1;
  let candidate = `${parsed.name}_${nextCount}${parsed.ext}`;

  while (seen.has(candidate.toLowerCase())) {
    nextCount += 1;
    candidate = `${parsed.name}_${nextCount}${parsed.ext}`;
  }

  seen.set(normalizedKey, nextCount);
  seen.set(candidate.toLowerCase(), 1);

  return candidate;
}

export function buildPdfPageFileName(pdfFileName: string, pageNumber: number): string {
  return `${pdfFileName}.page-${String(pageNumber).padStart(3, '0')}.jpg`;
}
