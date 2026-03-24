# TASK
Extract proof-of-purchase data from commercial documents (invoices, receipts, POs). 
Focus ONLY on documents containing Hewlett-Packard (HP) products.
If no HP products are found, return: {"results": []}

# OUTPUT FORMAT (STRICT)
1. Output MUST be valid JSON only.
2. NO markdown code blocks (no ```json).
3. NO introductory or concluding text.
4. Start with { and end with }.
5. Use null for missing values. Use [] for empty arrays.

# SCHEMA
{
  "results": [
    {
      "document_title": "invoice" | "receipt" | "purchase order" | null,
      "invoice_date": "YYYY-MM-DD" | null,
      "invoice_number": "string" | null,
      "currency_code": "USD" | "EUR" | null,
      "gross_amount": 100.00,
      "seller": {"name": "string", "city": "string", "state": "string", "country": "US"},
      "end_customer": {"name": "string", "city": "string", "state": "string", "country": "US", "email": "string"},
      "purchases": [
        {
          "quantity": 1,
          "product_numbers": ["string"],
          "description": "string",
          "serial_numbers": ["string"],
          "unit_price": 100.00
        }
      ]
    }
  ]
}

# EXTRACTION RULES
1. **HP Products Only**: Only extract line items that are HP products (computers, printers, accessories).
2. **Date**: Normalize ALL dates to YYYY-MM-DD. Do not rely on country to guess format.
3. **Country**: Normalize country names to ISO 3166-1 alpha-2 (e.g., "United States" -> "US").
4. **Currency**: Normalize to ISO 4217 (e.g., "$" -> "USD").
5. **Fidelity**: Copy text verbatim for descriptions. Do not add words like "HP" if not visible.
6. **Missing Data**: If a field is not visible, use null (scalars) or [] (arrays).

# HP IDENTIFICATION PATTERNS
Extract values matching these patterns into `product_numbers`:
- **Laptop Model**: 2 digits, dash, 8 alphanumeric (e.g., "13-fa5yx4au", "42-HK281JUS")
- **SKU**: 7 alphanumeric (e.g., "AB1C2DE") OR 7 alphanumeric + # + 3 alphanumeric (e.g., "AB1C2DE#EFG")

Extract values matching these patterns into `serial_numbers`:
- **Serial**: 10-13 alphanumeric characters, NO spaces/symbols (e.g., "8CF9873K24", "PH382F31US")
- **Note**: Do not include the label "SN:" in the value.

# NEGATIVE CONSTRAINTS
- Do NOT extract serial numbers into product_numbers.
- Do NOT extract product numbers into serial_numbers.
- Do NOT infer hidden text.
- If document has no HP products, output {"results": []}.

# EXAMPLES
## Input: Invoice with HP Laptop
## Output:
{"results": [{"document_title": "invoice", "invoice_date": "2023-10-05", "invoice_number": "INV-123", "currency_code": "USD", "gross_amount": 1200.00, "seller": {"name": "TechStore", "city": "Austin", "state": "TX", "country": "US"}, "end_customer": {"name": "John Doe", "city": "Austin", "state": "TX", "country": "US", "email": "john@example.com"}, "purchases": [{"quantity": 1, "product_numbers": ["13-fa5yx4au"], "description": "HP EliteBook 13-fa5yx4au", "serial_numbers": ["8CF9873K24"], "unit_price": 1200.00}]}]}

## Input: Multi-page document with related documents for same purchase (page 1 Sales Invoice, page 2 Delivery Receipt)
## Output: 
{"results": [{"document_title": "sales invoice", "invoice_date": "2025-10-05", "invoice_number": "103912", "currency_code": "PHP", "gross_amount": 123456.78, "seller": {"name": "Decagon Valley Systems", "city": "Manila", "state": null, "country": "PH"}, "end_customer": {"name": "Juan dela Cruz", "city": "Cebu", "state": null, "country": "PH", "email": "juan@delacruz.ph"}, "purchases": [{"quantity": 1, "product_numbers": ["AB1C2DE#EFG"], "description": "HP EliteBook 835 G11 AB1C2DE#EFG", "serial_numbers": ["CNF3453H33"], "unit_price": 123456.78}]}]}

## Input: Invoice with Non-HP Products Only
## Output:
{"results": []}

## Input