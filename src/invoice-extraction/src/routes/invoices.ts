import { Hono } from 'hono';
import { ZodError } from 'zod';
import type { Env } from '../lib/env';
import { AppError, formatValidationDetails } from '../lib/http-error';
import { invoiceExtractionRequestSchema } from '../schemas/request';
import { runExtractionPipeline } from '../services/extraction-pipeline';

interface AppBindings {
  Variables: {
    requestId: string;
  };
}

export function createInvoicesRouter(env: Env): Hono<AppBindings> {
  const router = new Hono<AppBindings>();

  router.post('/extract/', async (c) => {
    const contentType = c.req.header('content-type') || '';
    if (!contentType.toLowerCase().includes('application/json')) {
      throw new AppError(415, 'unsupported_media_type', 'Content-Type must be application/json.');
    }

    let payload: unknown;
    try {
      payload = await c.req.json();
    } catch {
      throw new AppError(400, 'invalid_json', 'Request body must be valid JSON.');
    }

    const parsedRequest = invoiceExtractionRequestSchema.safeParse(payload);
    if (!parsedRequest.success) {
      throw new AppError(400, 'invalid_request_body', 'Request body did not match the expected schema.', formatValidationDetails(parsedRequest.error));
    }

    try {
      const extraction = await runExtractionPipeline({
        env,
        requestId: c.get('requestId'),
        request: parsedRequest.data,
      });

      return c.json({
        request_id: c.get('requestId'),
        response: extraction,
      });
    } catch (error) {
      if (error instanceof ZodError) {
        throw new AppError(502, 'invalid_model_output', 'Model output did not match the extraction schema.', formatValidationDetails(error));
      }

      throw error;
    }
  });

  return router;
}
