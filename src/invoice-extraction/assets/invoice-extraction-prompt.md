Extract proof-of-purchase data from the uploaded document(s) into JSON matching the provided schema.

Only extract sale/commercial documents such as sales (tax) invoice, receipt, delivery receipt, purchase order, order confirmation, or similar buyer-seller transaction records. 

Determine an eligible proof-of-purchase document by using the following criteria: 
- The seller can be properly identified (hints: find business address, tax or business registration numbers.)
- Purchased line items exist.
- Applicable sales tax correctly indicated.

If no qualifying document is present, return {"results":[]}.

If multiple pages/files describe the same transaction (e.g., sales invoice on page 1, delivery receipt on page 2), merge them into one result and do not duplicate it.

Rules:
- Each `results` item is one document or transaction.
- Use exact visible values only. If missing, unreadable, or ambiguous, use `null`.
- Use empty arrays when no repeated values are present.
- Avoid duplicate data extraction on purchased line items. 
- `document_title` must describe the type of commercial document (e.g., sales invoice, delivery receipt, purchase order, etc.)
- `currency_code` must be a valid 3-letter ISO 4217 standard currency code (e.g., `USD`, `EUR`, `JPY`). Use currency symbols and the `country` of the `seller` from the document as a hint.
- `gross_amount` is the full document total, not a line amount.
- `invoice_number` may come from labels like invoice number, receipt number, order number, DR number, or reference number when clearly acting as the main document/transaction ID.
- `invoice_date` is the main document/transaction date. Expected format: `YYYY-MM-DD`
- `seller` is the merchant/store/vendor that sold the goods or services. 
- `end_customer` is the buyer/recipient. Prefer customer, buyer, sold to, or ship to.
- `email` must be a valid e-mail address. Use `null` if none found.
- `country` must be 2-letter ISO 3166-1 alpha-2 country code.
- Put purchased line items only in `purchases`.
- `description` in each `purchase` must describe known product lines from Hewlett-Packard (HP) such as: 
  - Notebooks (Envy, Pavilion, Victus, ZBook, Elitebook, Probook, Omen, Omnibook, Laptop 13-xxxxxxyyy)
  - Printers (Laserjet, Officejet, Smart Tank)
- Put visible SKU/model/part codes in `part_number` that fit the following rules: 
  - A purchased item can have multiple model or part codes. Extract all, separate with a comma.
  - 7 to 11 contiguous characters, usually looks like: `AB1C2DE` or `AB1C2DE#EFG`
  - For HP Laptop: `aa-yyyyyyzz` where `aa` is [13|14|15|16|24], `yyyyyy` is an alphanumeric value, and `zz` is a valid 2-character country code (e.g., `AU`, ``)
- Extract `serial_numbers` that satisfy the following rules: 
  - Contiguous 10-13 character string, only alpha-numeric characters allowed [aA-zZ,0-9], without separator characters like dash, pound, or period
  - Usually prefixed by `SN`, `S/N`, `SN#`, `SN:`, `Serial:`, or something similar. DO NOT include the prefix in extracted values.
  - Examples: CN301234567, 1AB123CDEF, 0AB123456C
  - Always attach to related purchase item. Do not create a separate purchase for serial number text.
- Use the seller's country to correctly parse localized formatting for numbers (e.g., if `DE`, expected format is `1.234,99`, for `FR` it can be `1 234,56` while most of the world uses `1,234.56`)
- Ensure that extracted values exactly matches the original document. DO NOT truncate or replace text with ellipsis.
- Always respond with a valid JSON object.

Example output:
{
  "results": [
    {
      "document_title": null,
      "invoice_date": null,
      "invoice_number": null,
      "currency_code": null,
      "gross_amount": 0.00,
      "seller": {
        "name": null,
        "city": null,
        "state": null,
        "country": null
      },
      "end_customer": {
        "name": null,
        "city": null,
        "state": null,
        "country": null,
        "email": null
      },
      "purchases": [
        {
          "quantity": 1,
          "part_number": null,
          "description": null,
          "serial_numbers": [],
          "unit_price": 0.00
        }
      ]
    }
  ]
}
