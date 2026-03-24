TASK:  
Extract proof-of-purchase data from commercial documents (invoices, receipts, delivery receipts, purchase orders). Return `{"results":[]}` if no qualifying document found.

OUTPUT CONTRACT:  
- Output must be a single parseable JSON object
- No markdown, code fences, prose, or trailing text
- Start with `{` and end with `}`
- Use `null` for unknown/missing scalar values
- Use `[]` for missing arrays

FIELD RULES:  
- `document_title`: Document type only - one of: "invoice", "sales invoice", "receipt", "delivery receipt", "purchase order", "credit memo", "debit note", or `null`. NOT a product name, company name, or content title.
- `invoice_date`: Format as `YYYY-MM-DD` or `null`. Use the `seller.country` to determine format in document and normalize extracted value to `<year>-<month>-<day>`.
- `invoice_number`: Main transaction identifier
- `gross_amount`: Full document total (number or `null`)
- `currency_code`: ISO 4217 code (e.g., "USD", "PHP") or `null`
- `seller`: Merchant/vendor with `name`, `city`, `state`, `country`
- `end_customer`: Buyer/recipient with `name`, `city`, `state`, `country`, `email`
- `purchases`: Array of line items with `quantity`, `product_numbers` (array), `description`, `serial_numbers` (array), `unit_price`
- `description`: Must describe an eligible HP product (e.g,. computers, printers, print supplies). Examples: "Victus 16-bs0253mf", "HP DesignJet T6996 MFP XY9P8WL#JXL", "HP 69X Gold Original LaserJet Toner Cartridge"

EXTRACTION RULES:  
1. **HP Products Only**: Only extract line items that are known HP Products.
2. **Date**: Normalize ALL dates to YYYY-MM-DD. Do not rely on country to guess format.
3. **Country**: Normalize country names to ISO 3166-1 alpha-2 (e.g., "United States" -> "US").
4. **Currency**: Normalize to ISO 4217 (e.g., "$" -> "USD").
5. **Fidelity**: Copy text verbatim for `description`. Do not add words like "HP" if not visible.
6. **Numeric amounts**: `gross_amount`, `unit_price` and `quantity` as JSON numbers using visible document locale cues
7. **Multi-page transactions**: Merge into one result
9. **Related documents**: Skip documents that supplement an already-extracted purchase (example: Invoice on page 1, delivery receipt on page 2- Only extract invoice)
10. **DO NOT COPY**: Sample values in response.

PURCHASES EXTRACTION RULES: 
1. Required fields: `description`, `unit_price`. 
2. Prioritize line-items that satisfy HP PRODUCT IDENTIFICATION LOGIC
3. Use MATCHING CRITERIA for `product_numbers` and `serial_numbers`, set to "[]" if no matches can be found.

HP PRODUCT IDENTIFICATION LOGIC (CRITICAL):  
- Look for text in the "Description" or "Item" column.
- Locate known Hewlett-Packard (HP) Product Lines (e.g., notebook, desktop, printer, ink cartridge, toner) in `description`
- Also search and extract from the "Product Number" column when one exists (alternate column headers: "SKU", "Part No.", "Article Nr.")
- HP Laptop Model Code: Matches pattern "<2-digits>-<8-alphanumeric>" (e.g., "13-fa5yx4au"). Extract this into `product_numbers`.
- If a visible line-item description contains an HP Laptop Model Code, include that code verbatim in `description`. Do not omit it, rewrite it, or expand it with words like "HP" or "Laptop" unless visibly present.

PRODUCT NUMBER MATCHING CRITERIA:  
Extract strings from line-item table into `product_numbers` that match ONE of these patterns

1. SKU  
- Syntax: <7-alphanumeric>
- Example: "AB1C2DE", "ZX9C8VB"  
- Ignore: "1234567" (all numbers), "ABCDEFG" (all letters), "A321JF" (too short), "8ZZY987H54" (too long)

2. SKU with suffix  
- Syntax: <7-alphanumeric>#<3-alphanumeric> 
- Example: "AB1C2DE#EFG", "ZX9C8VB#MSP", "C5GQ2YK#YYD"
- Ignore: "38123991321" (all numbers), "8WZY987H54" (too short, missing '#'), "213AB3X/442" (Invalid characters)

3. HP Laptop model 
- Syntax: <2-digits>-<8-alphanumeric>
- Example: "15-tf392kph", "42-HK281JUS" "14-WL1144TR"
- Ignore: "i7-13800HX" (processor model), "13/TG123AU" (Invalid separator)  

SERIAL NUMBER MATCHING CRITERIA:  
Extract strings from line-item table into `serial_numbers` that match these criteria:  
- Syntax: <10 alphanumeric>, no spaces, no symbols, no prefixes in the value.
- Example: "8CF9873K24", "5CG538240Z", "PH382F31US"
- Ignore: "12345-6789" (Wrong format, invalid symbols), "3901938237" (all numbers)
- Hint: Usually found beside known labels (e.g., "SN:", "Serial:", "SNo.", "SN#")

EXAMPLE OUTPUT:  
{
  "results": [
    {
      "document_title": "string | null",
      "invoice_date": "YYYY-MM-DD | null",
      "invoice_number": "string | null",
      "currency_code": "string | null",
      "gross_amount": 0.00,
      "seller": {
        "name": "string | null",
        "city": "string | null",
        "state": "string | null",
        "country": "string | null"
      },
      "end_customer": {
        "name": "string | null",
        "city": "string | null",
        "state": "string | null",
        "country": "string | null",
        "email": null
      },
      "purchases": [
        {"quantity": 1,"product_numbers": [],"description": "string","serial_numbers": [],"unit_price": 0.00}
      ]
    }
  ]
}
