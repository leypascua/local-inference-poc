import { Hono } from 'hono';
import type { Context } from 'hono';
import type { ContentfulStatusCode } from 'hono/utils/http-status';
import type { Env } from './lib/env';
import { ensureTempRoot } from './lib/temp-files';
import { AppError, isAppError } from './lib/http-error';
import { createRequestId } from './lib/request-id';
import { createInvoicesRouter } from './routes/invoices';
import { checkLlamaReadiness } from './services/llama-client';

interface AppBindings {
  Variables: {
    requestId: string;
  };
}

function getRequestId(c: Context<AppBindings>): string {
  return c.get('requestId');
}

function errorResponse(c: Context<AppBindings>, error: AppError) {
  const body: Record<string, unknown> = {
    request_id: getRequestId(c),
    error: {
      code: error.code,
      message: error.message,
    },
  };

  if (error.details !== undefined) {
    (body.error as Record<string, unknown>).details = error.details;
  }

  return c.json(body, error.statusCode as ContentfulStatusCode);
}

export function createApp(env: Env): Hono<AppBindings> {
  const app = new Hono<AppBindings>();

  app.use('*', async (c, next) => {
    const requestId = createRequestId();
    c.set('requestId', requestId);
    c.header('x-request-id', requestId);
    await next();
  });

  app.get('/health/live', (c) => {
    return c.json({ status: 'ok' });
  });

  app.get('/health', async (c) => {
    await ensureTempRoot(env.tempDir);
    const llamaReady = await checkLlamaReadiness(env);

    return c.json({
      status: llamaReady ? 'ready' : 'degraded',
      checks: {
        temp_dir: 'ok',
        llama_cpp: llamaReady ? 'ok' : 'unavailable',
      },
    }, llamaReady ? 200 : 503);
  });

  app.get('/health/ready', async (c) => {
    await ensureTempRoot(env.tempDir);
    const llamaReady = await checkLlamaReadiness(env);

    if (!llamaReady) {
      throw new AppError(503, 'service_unavailable', 'llama.cpp is not ready.');
    }

    return c.json({ status: 'ready' });
  });

  app.route('/invoices', createInvoicesRouter(env));

  app.notFound((c) => {
    return c.json({
      request_id: getRequestId(c),
      error: {
        code: 'not_found',
        message: 'Route not found.',
      },
    }, 404);
  });

  app.onError((error, c) => {
    if (isAppError(error)) {
      return errorResponse(c, error);
    }

    console.error(error);
    return errorResponse(c, new AppError(500, 'internal_error', 'Unexpected server error.'));
  });

  return app;
}
