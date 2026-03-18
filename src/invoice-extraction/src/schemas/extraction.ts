import { z } from 'zod';
import extractionOutputJsonSchema from '../../assets/invoice-extraction-output-schema.json';
import extractionPrompt from '../../assets/invoice-extraction-prompt.md?raw';

type JsonRecord = Record<string, unknown>;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function asNullableString(value: unknown): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

function asNullableNumber(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === 'string') {
    const normalized = value.replace(/,/g, '').trim();
    if (normalized === '') {
      return null;
    }

    const parsed = Number(normalized);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function normalizeParty(value: unknown, includeEmail = false): JsonRecord {
  const record = isRecord(value) ? value : {};
  const normalized: JsonRecord = {
    name: asNullableString(record.name),
    city: asNullableString(record.city),
    state: asNullableString(record.state),
    country: asNullableString(record.country),
  };

  if (includeEmail) {
    normalized.email = asNullableString(record.email);
  }

  return normalized;
}

function normalizePurchase(value: unknown): JsonRecord {
  const record = isRecord(value) ? value : {};
  const serialNumbersValue = record.serial_numbers;

  return {
    quantity: asNullableNumber(record.quantity),
    part_number: asNullableString(record.part_number),
    description: asNullableString(record.description ?? record.product_name),
    serial_numbers: Array.isArray(serialNumbersValue)
      ? serialNumbersValue.flatMap((item) => {
          const normalized = asNullableString(item);
          return normalized === null ? [] : [normalized];
        })
      : [],
    unit_price: asNullableNumber(record.unit_price ?? record.amount),
  };
}

function looksLikePurchaseRow(value: unknown): value is JsonRecord {
  if (!isRecord(value)) {
    return false;
  }

  return (
    'product_name' in value ||
    'amount' in value ||
    ('quantity' in value && ('unit_price' in value || 'part_number' in value))
  );
}

export function normalizeExtractionCandidate(value: unknown): unknown {
  if (!isRecord(value) || !Array.isArray(value.results)) {
    return value;
  }

  if (value.results.length === 0 || !value.results.every(looksLikePurchaseRow)) {
    return value;
  }

  return {
    results: [
      {
        document_title: asNullableString(value.document_title),
        invoice_date: asNullableString(value.invoice_date),
        invoice_number: asNullableString(value.invoice_number),
        currency_code: asNullableString(value.currency_code),
        gross_amount: asNullableNumber(value.gross_amount ?? value.total_amount),
        seller: normalizeParty(value.seller),
        end_customer: normalizeParty(value.end_customer, true),
        purchases: value.results.map((item) => normalizePurchase(item)),
      },
    ],
  };
}

function nullableStringField() {
  return z.preprocess((value) => {
    if (value === undefined || value === null) {
      return null;
    }
    if (typeof value !== 'string') {
      return value;
    }

    const trimmed = value.trim();
    return trimmed === '' ? null : trimmed;
  }, z.string().nullable());
}

function nullableNumberField() {
  return z.preprocess((value) => {
    if (value === undefined || value === null || value === '') {
      return null;
    }

    if (typeof value === 'string') {
      const normalized = value.replace(/,/g, '').trim();
      if (normalized === '') {
        return null;
      }

      const parsed = Number(normalized);
      return Number.isFinite(parsed) ? parsed : value;
    }

    return value;
  }, z.number().finite().nullable());
}

const partySchema = z.object({
  name: nullableStringField(),
  city: nullableStringField(),
  state: nullableStringField(),
  country: nullableStringField(),
}).strict();

const purchaseSchema = z.object({
  quantity: nullableNumberField(),
  part_number: nullableStringField(),
  description: nullableStringField(),
  serial_numbers: z.array(z.string().trim()).catch([]),
  unit_price: nullableNumberField(),
}).strict();

const documentSchema = z.object({
  document_title: nullableStringField(),
  invoice_date: nullableStringField(),
  invoice_number: nullableStringField(),
  currency_code: nullableStringField(),
  gross_amount: nullableNumberField(),
  seller: partySchema,
  end_customer: partySchema.extend({
    email: nullableStringField(),
  }).strict(),
  purchases: z.array(purchaseSchema),
}).strict();

export const extractionResponseSchema = z.object({
  results: z.array(documentSchema),
}).strict();

export type ExtractionResponse = z.infer<typeof extractionResponseSchema>;
export const extractionSchemaAsset = extractionOutputJsonSchema;
export const extractionPromptAsset = extractionPrompt.trim();
