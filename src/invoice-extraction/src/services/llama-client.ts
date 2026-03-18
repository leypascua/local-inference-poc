import { readFile } from 'node:fs/promises';
import { AppError } from '../lib/http-error';
import type { Env } from '../lib/env';
import { extractionPromptAsset, extractionSchemaAsset } from '../schemas/extraction';
import type { PreparedImageFile } from './file-loader';

interface ChatCompletionChoice {
  message?: {
    content?: string | Array<{ type?: string; text?: string }>;
    reasoning_content?: string;
  };
}

interface ChatCompletionResponse {
  choices?: ChatCompletionChoice[];
  error?: {
    message?: string;
  };
}

interface ChatCompletionRequestBody {
  model: string;
  temperature: number;
  max_tokens: number;
  json_schema?: typeof extractionSchemaAsset;
  messages: Array<{
    role: 'system' | 'user';
    content: string | Array<{ type: string; text?: string; image_url?: { url: string } }>;
  }>;
  response_format?: {
    type: 'json_schema';
    json_schema: {
      name: string;
      strict: true;
      schema: typeof extractionSchemaAsset;
    };
  };
}

type StructuredOutputMode = 'json_schema' | 'response_format' | 'prompt_only';

function buildPrompt(mode: StructuredOutputMode): string {
  if (mode === 'prompt_only') {
    return [
      extractionPromptAsset,
      '',
      'Return a JSON object with top-level key "results" only.',
      'Each result must include document_title, invoice_date, invoice_number, currency_code, gross_amount, seller, end_customer, and purchases.',
      'seller must include name, city, state, country.',
      'end_customer must include name, city, state, country, email.',
      'Each purchase must include quantity, part_number, description, serial_numbers, unit_price.',
    ].join('\n');
  }

  return extractionPromptAsset;
}

function extractMessageContent(payload: ChatCompletionResponse): string {
  if (payload.error?.message) {
    throw new AppError(502, 'llama_upstream_error', payload.error.message);
  }

  const message = payload.choices?.[0]?.message;
  if (!message) {
    throw new AppError(502, 'llama_upstream_error', 'llama.cpp response did not include a message.');
  }

  if (typeof message.content === 'string') {
    return message.content;
  }

  if (Array.isArray(message.content)) {
    return message.content
      .filter((part) => part.type === 'text' && typeof part.text === 'string')
      .map((part) => part.text)
      .join('')
      .trim();
  }

  return message.reasoning_content?.trim() || '';
}

function stripJsonCodeFence(value: string): string {
  const trimmed = value.trim();
  const fencedMatch = trimmed.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
  return (fencedMatch?.[1] ?? trimmed).trim();
}

function extractBalancedJson(value: string): string | null {
  const start = value.search(/[\[{]/);
  if (start === -1) {
    return null;
  }

  const opening = value[start];
  const closing = opening === '{' ? '}' : ']';
  let depth = 0;
  let inString = false;
  let escaping = false;

  for (let index = start; index < value.length; index += 1) {
    const char = value[index];

    if (escaping) {
      escaping = false;
      continue;
    }

    if (char === '\\') {
      escaping = true;
      continue;
    }

    if (char === '"') {
      inString = !inString;
      continue;
    }

    if (inString) {
      continue;
    }

    if (char === opening) {
      depth += 1;
      continue;
    }

    if (char === closing) {
      depth -= 1;
      if (depth === 0) {
        return value.slice(start, index + 1).trim();
      }
    }
  }

  return null;
}

function parseStructuredContent(value: string): unknown {
  const normalized = stripJsonCodeFence(value);
  const candidates = [normalized, extractBalancedJson(normalized)].filter(
    (candidate): candidate is string => Boolean(candidate),
  );

  let lastError: unknown;
  for (const candidate of candidates) {
    try {
      return JSON.parse(candidate);
    } catch (error) {
      lastError = error;
    }
  }

  throw new AppError(502, 'invalid_model_output', 'llama.cpp returned invalid JSON content.', {
    cause: String(lastError),
    content: normalized,
  });
}

async function fileToDataUrl(file: PreparedImageFile): Promise<string> {
  const bytes = await readFile(file.filePath);
  return `data:${file.contentType};base64,${bytes.toString('base64')}`;
}

function buildRequestBody(params: {
  env: Env;
  imageParts: Array<{ type: string; image_url: { url: string } }>;
  mode: StructuredOutputMode;
}): ChatCompletionRequestBody {
  const { env, imageParts, mode } = params;

  return {
    model: env.llamaModel,
    temperature: 0,
    max_tokens: 1024,
    messages: [
      {
        role: 'system',
        content: buildPrompt(mode),
      },
      {
        role: 'user',
        content: [
          {
            type: 'text',
            text: 'Extract the uploaded documents into one JSON object that matches the schema exactly.',
          },
          ...imageParts,
        ],
      },
    ],
    ...(mode === 'json_schema'
      ? {
          json_schema: extractionSchemaAsset,
        }
      : {}),
    ...(mode === 'response_format'
      ? {
          response_format: {
            type: 'json_schema' as const,
            json_schema: {
              name: 'invoice_extraction_response',
              strict: true as const,
              schema: extractionSchemaAsset,
            },
          },
        }
      : {}),
  };
}

async function createChatCompletion(params: {
  env: Env;
  body: ChatCompletionRequestBody;
}): Promise<Response> {
  const { env, body } = params;

  return fetch(`${env.llamaBaseUrl}/v1/chat/completions`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
    },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(env.requestTimeoutMs),
  }).catch((error: unknown) => {
    throw new AppError(502, 'llama_upstream_error', 'Unable to reach llama.cpp.', { cause: String(error) });
  });
}

export async function extractWithLlama(params: {
  env: Env;
  images: PreparedImageFile[];
}): Promise<unknown> {
  const { env, images } = params;

  const imageParts = await Promise.all(
    images.map(async (image) => ({
      type: 'image_url',
      image_url: {
        url: await fileToDataUrl(image),
      },
    })),
  );

  const modes: StructuredOutputMode[] = ['json_schema', 'response_format', 'prompt_only'];
  let lastError: AppError | null = null;

  for (const mode of modes) {
    const response = await createChatCompletion({
      env,
      body: buildRequestBody({ env, imageParts, mode }),
    });

    if (!response.ok) {
      const body = await response.text().catch(() => '');
      if (response.status === 400 && /response_format|json_schema|grammar/i.test(body)) {
        lastError = new AppError(502, 'llama_upstream_error', `llama.cpp rejected ${mode} structured output mode.`, body ? { body, mode } : { mode });
        continue;
      }

      throw new AppError(502, 'llama_upstream_error', `llama.cpp returned HTTP ${response.status}.`, body ? { body, mode } : { mode });
    }

    const payload = (await response.json()) as ChatCompletionResponse;
    const content = stripJsonCodeFence(extractMessageContent(payload));
    if (!content) {
      lastError = new AppError(502, 'invalid_model_output', 'llama.cpp returned empty completion content.', { mode });
      continue;
    }

    try {
      return parseStructuredContent(content);
    } catch (error) {
      if (error instanceof AppError) {
        lastError = new AppError(error.statusCode, error.code, error.message, {
          ...(typeof error.details === 'object' && error.details !== null ? error.details : {}),
          mode,
        });
        continue;
      }

      throw error;
    }
  }

  throw lastError ?? new AppError(502, 'invalid_model_output', 'llama.cpp did not return valid structured JSON.');
}

export async function checkLlamaReadiness(env: Env): Promise<boolean> {
  try {
    const response = await fetch(`${env.llamaBaseUrl}/health`, {
      method: 'GET',
      signal: AbortSignal.timeout(Math.min(env.remoteFetchTimeoutMs, 5000)),
    });

    return response.ok;
  } catch {
    return false;
  }
}
