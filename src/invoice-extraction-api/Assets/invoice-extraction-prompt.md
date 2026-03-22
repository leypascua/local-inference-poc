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
          "product_numbers": [],
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
- `invoice_date`: Format as `YYYY-MM-DD` or `null`. Use the `seller.country` to determine format in document and normalize extracted value to `<year>-<month>-<day>`.
- `invoice_number`: Main transaction identifier
- `gross_amount`: Full document total (number or `null`)
- `currency_code`: ISO 4217 code (e.g., "USD", "PHP") or `null`
- `seller`: Merchant/vendor with `name`, `city`, `state`, `country`
- `end_customer`: Buyer/recipient with `name`, `city`, `state`, `country`, `email`
- `purchases`: Array of line items with `quantity`, `product_numbers` (array), `description`, `serial_numbers` (array), `unit_price`

Extraction rules:
- Use exact visible values; do not invent or infer
- Merge multi-page transactions into one result
- Skip delivery receipts that duplicate products from an already-extracted sales invoice
- Use standard ISO codes 
  - `invoice_date`: ISO 8601 (e.g., `2026-03-16`, `1959-12-13`)
  - `country`: ISO 3166-1 alpha-2 (e.g., `US`, `DE`, `CN`, `JP`)
  - `currency_code`: ISO 4217 (e.g., `USD`, `EUR`, `CNY`, `JPY`)
- Only extract purchased HP products.
- `description` must be concise: `<HP Product Name> <Product Code> <SKU>`. Examples: `HP AwesomeBook AI 69-ab01234ZZ A1BC2DE#FG`, `HP SmartTank 6996z Ink Tank MFP ZY1F9GH`, `HP Dragonfly G4 13.5" FHD Laptop`, `HP 14-ab1cd2ef`
- Attach serial numbers to their corresponding purchase item
- When a value matches both formats, classify 10-13 char alphanumeric strings as serial_numbers, NOT product_numbers
- Parse numbers using seller's locale hints (e.g., `1.234,99` → `1234.99`)
- DO NOT copy example values in response.

HP-specific guidance:
- `description`: HP product lines (Envy, Pavilion, Victus, Omen, ZBook, Elitebook, Probook, Dragonfly, LaserJet, OfficeJet, Smart Tank)
- Always extract `HP Laptop` model codes into `description`.
- `product_numbers`: Array of HP product numbers.
  - Contiguous strings with valid characters (case-insensitive):
    - numbers 0-9
    - letters a-z 
    - symbols dash ('-') and pound ('#') only
  - There are 2 types of product numbers: 
    - SKU: EXACTLY 7 chars (e.g., `AB1C2DE`) OR 11 chars with `#` in position 8 (e.g., `AB1C2DE#EFG`). Values of 10-13 chars without `#` are serial_numbers, NOT product_numbers.  
    - HP Laptop Model: `<2-digits>-<8-alphanumeric>`. Examples: `13-fa5yx4au`, `15-tf392kph`
- `serial_numbers`: 10-13 alphanumeric chars, no prefixes. Examples: `AB123456789`, `PH382F31US`, `5CG538240Z`. Usually preceded by labels: `SN#`, `Serial:`, `SNo:`, `SN:`. These are NOT product_numbers.

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
          "product_numbers": ["<product_numbers[0]>", "<product_numbers[1]>"],
          "description": "<description>",
          "serial_numbers": ["<serial_numbers[0]>"],
          "unit_price": 0.00
        }
      ]
    }
  ]
}
