import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { DOMMatrix, ImageData, Path2D } from '@napi-rs/canvas';
import { type Canvas, createCanvas } from '@napi-rs/canvas/node-canvas';
import { getDocument } from 'pdfjs-dist/legacy/build/pdf.mjs';
import { buildPdfPageFileName } from '../lib/file-names';
import { AppError } from '../lib/http-error';
import type { Env } from '../lib/env';
import type { LoadedSourceFile, PreparedImageFile } from './file-loader';

let pdfJsConfigured = false;

function configurePdfJsForNode(): void {
  if (pdfJsConfigured) {
    return;
  }

  if (!('DOMMatrix' in globalThis)) {
    Object.assign(globalThis, { DOMMatrix });
  }
  if (!('ImageData' in globalThis)) {
    Object.assign(globalThis, { ImageData });
  }
  if (!('Path2D' in globalThis)) {
    Object.assign(globalThis, { Path2D });
  }

  pdfJsConfigured = true;
}

function renderCanvasToJpeg(canvas: Canvas): Buffer {
  const jpegBuffer = canvas.toBuffer('image/jpeg');
  if (!(jpegBuffer instanceof Buffer)) {
    throw new AppError(500, 'pdf_rasterization_failed', 'Unable to encode rasterized PDF page as JPEG.');
  }
  return jpegBuffer;
}

export async function rasterizePdfToImages(params: {
  env: Env;
  requestDir: string;
  sourceFile: LoadedSourceFile;
}): Promise<PreparedImageFile[]> {
  const { env, requestDir, sourceFile } = params;
  configurePdfJsForNode();

  try {
    const pdfBytes = await readFile(sourceFile.filePath);
    const loadingTask = getDocument({
      data: new Uint8Array(pdfBytes),
      useSystemFonts: true,
      isEvalSupported: false,
    });

    const pdfDocument = await loadingTask.promise;
    try {
      if (pdfDocument.numPages > env.maxPdfPages) {
        throw new AppError(413, 'payload_too_large', `${sourceFile.originalName} exceeds MAX_PDF_PAGES.`);
      }

      const images: PreparedImageFile[] = [];

      for (let pageNumber = 1; pageNumber <= pdfDocument.numPages; pageNumber += 1) {
        const page = await pdfDocument.getPage(pageNumber);
        const viewport = page.getViewport({ scale: 2 });
        const canvas = createCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height));
        const context = canvas.getContext('2d');

        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, canvas.width, canvas.height);

        await page.render({
          canvas: null,
          canvasContext: context,
          viewport,
          background: 'rgb(255, 255, 255)',
        }).promise;

        const fileName = buildPdfPageFileName(sourceFile.storedName, pageNumber);
        const filePath = path.join(requestDir, fileName);
        await writeFile(filePath, renderCanvasToJpeg(canvas));

        images.push({
          filePath,
          fileName,
          contentType: 'image/jpeg',
        });

        page.cleanup();
      }

      return images;
    } finally {
      await pdfDocument.destroy();
    }
  } catch (error) {
    if (error instanceof AppError) {
      throw error;
    }

    throw new AppError(422, 'pdf_rasterization_failed', `Unable to rasterize PDF ${sourceFile.originalName}.`, {
      cause: String(error),
    });
  }
}
