import { z } from 'zod';

export const supportedContentTypes = [
  'image/jpeg',
  'image/png',
  'image/gif',
  'application/pdf',
] as const;

export const supportedImageContentTypes = [
  'image/jpeg',
  'image/png',
  'image/gif',
] as const;

export const invoiceContentItemSchema = z.object({
  file_url: z.string().min(1, 'file_url is required.'),
  name: z.string().min(1, 'name is required.').max(255, 'name must be 255 characters or fewer.'),
  content_type: z.enum(supportedContentTypes),
});

export const invoiceExtractionRequestSchema = z.object({
  content: z.array(invoiceContentItemSchema).min(1, 'content must include at least one item.'),
});

export type SupportedContentType = (typeof supportedContentTypes)[number];
export type SupportedImageContentType = (typeof supportedImageContentTypes)[number];
export type InvoiceExtractionRequest = z.infer<typeof invoiceExtractionRequestSchema>;
export type InvoiceContentItem = z.infer<typeof invoiceContentItemSchema>;
