Output JSON in this exact format:
{
  "results": [
    {
      "document_title": "",
      "invoice_date": "",
      "invoice_number": "",
      "currency_code": "",
      "gross_amount": null,
      "seller": {
        "name": "",
        "city": "",
        "state": "",
        "country": ""
      },
      "end_customer": {
        "name": "",
        "city": "",
        "state": "",
        "country": "",
        "email": ""
      },
      "purchases": [
        {
          "quantity": null,
          "part_number": "",
          "description": "",
          "serial_numbers": [],
          "unit_price": null
        }
      ]
    }
  ]
}

Output contract:
- Output must be a single parseable JSON object
- No markdown, code fences, prose, or trailing text
- Start with `{` and end with `}`
- Use `null` for unknown/missing scalar values
- Use `[]` for missing arrays

Task:
Extract proof-of-purchase data from commercial documents (invoices, receipts, delivery receipts, purchase orders). Return `{"results":[]}` if no qualifying document found.

Field rules:
- `document_title`: Document type only - one of: "invoice", "sales invoice", "receipt", "delivery receipt", "purchase order", "credit memo", "debit note", or `null`. NOT a product name, company name, or content title.
- `invoice_date`: Format as `YYYY-MM-DD` or `null`. Use the `seller.country` to determine format in document and normalize extracted value to `<year>-<month>-<day>` format.
- `invoice_number`: Main transaction identifier
- `gross_amount`: Full document total (number or `null`)
- `currency_code`: ISO 4217 code (e.g., "USD", "PHP") or `null`
- `seller`: Merchant/vendor with `name`, `city`, `state`, `country` (ISO 3166-1 alpha-2)
- `end_customer`: Buyer/recipient with `name`, `city`, `state`, `country` (ISO 3166-1 alpha-2), `email`
- `purchases`: Array of line items with `quantity`, `part_number`, `description`, `serial_numbers` (array), `unit_price`

Extraction rules:
- Use exact visible values; do not invent or infer
- Merge multi-page transactions into one result
- Only extract purchased HP products.
- `description` must be concise: `<HP Product Name> <Product Code> <SKU>`. Example: `HP AwesomeBook AI 69-ab01234ZZ A1BC2DE#FG`
- Attach serial numbers to their corresponding purchase item
- Parse numbers using seller's locale hints (e.g., `1.234,99` → `1234.99`)
- DO NOT copy example values in response.

HP-specific guidance:
- `description`: HP product lines (Envy, Pavilion, Victus, ZBook, Elitebook, Probook, Omen, LaserJet, OfficeJet, Smart Tank)
- `part_number`: 7-11 chars (e.g., `AB1C2DE`, `AB1C2DE#EFG`) or HP laptop format `aa-yyyyyyyy`
- `serial_numbers`: 10-13 alphanumeric chars, no prefixes (Example: `AB123456789`)

Example output (format only - never copy these placeholder values):
{
  "results": [
    {
      "document_title": "<document_title>",
      "invoice_date": "YYYY-MM-DD",
      "invoice_number": "<invoice_number>",
      "currency_code": "<currency-iso-code>",
      "gross_amount": 0.00,
      "seller": {
        "name": "<seller.name>",
        "city": "<seller.city>",
        "state": "<seller.state>",
        "country": "<country-iso-code>"
      },
      "end_customer": {
        "name": "<end_customer.name>",
        "city": "<end_customer.city>",
        "state": "<end_customer.state>",
        "country": "<country-iso-code>",
        "email": null
      },
      "purchases": [
        {
          "quantity": 1,
          "part_number": "<part_number[0]>,<part_number[1]>",
          "description": "<description>",
          "serial_numbers": ["<serial_numbers[0]>"],
          "unit_price": 0.00
        }
      ]
    }
  ]
}
