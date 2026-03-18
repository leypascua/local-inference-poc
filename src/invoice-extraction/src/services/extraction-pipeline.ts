import { extractionResponseSchema, normalizeExtractionCandidate, type ExtractionResponse } from '../schemas/extraction';
import type { InvoiceExtractionRequest } from '../schemas/request';
import { createRequestTempDir, removeRequestTempDir } from '../lib/temp-files';
import { AppError } from '../lib/http-error';
import type { Env } from '../lib/env';
import { loadInputFiles, type PreparedImageFile } from './file-loader';
import { rasterizePdfToImages } from './pdf-rasterizer';
import { extractWithLlama } from './llama-client';

export async function runExtractionPipeline(params: {
  env: Env;
  requestId: string;
  request: InvoiceExtractionRequest;
}): Promise<ExtractionResponse> {
  const { env, requestId, request } = params;
  const requestDir = await createRequestTempDir(env.tempDir, requestId);

  try {
    const loadedFiles = await loadInputFiles({
      env,
      requestDir,
      items: request.content,
    });

    const images: PreparedImageFile[] = [];
    for (const file of loadedFiles) {
      if (file.contentType === 'application/pdf') {
        const rasterizedImages = await rasterizePdfToImages({ env, requestDir, sourceFile: file });
        images.push(...rasterizedImages);
      } else {
        images.push({
          filePath: file.filePath,
          fileName: file.storedName,
          contentType: file.contentType,
        });
      }

      if (images.length > env.maxTotalImages) {
        throw new AppError(413, 'payload_too_large', 'Request exceeds MAX_TOTAL_IMAGES after PDF rasterization.');
      }
    }

    const parsedModelOutput = await extractWithLlama({ env, images });
    return extractionResponseSchema.parse(normalizeExtractionCandidate(parsedModelOutput));
  } finally {
    if (!env.keepDebugFiles) {
      await removeRequestTempDir(requestDir);
    }
  }
}
