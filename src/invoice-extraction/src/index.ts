import { serve } from '@hono/node-server';
import { createApp } from './app';
import { loadEnv } from './lib/env';
import { ensureTempRoot } from './lib/temp-files';

async function main(): Promise<void> {
  const env = loadEnv();
  await ensureTempRoot(env.tempDir);

  const app = createApp(env);
  const server = serve({
    fetch: app.fetch,
    port: env.port,
    hostname: env.host,
  });

  console.info(`Invoice extraction API listening on http://${env.host}:${env.port}`);

  const shutdown = (signal: string) => {
    console.info(`Received ${signal}, shutting down.`);
    server.close();
  };

  process.on('SIGINT', () => shutdown('SIGINT'));
  process.on('SIGTERM', () => shutdown('SIGTERM'));
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
