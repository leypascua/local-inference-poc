Extract proof-of-purchase data from the uploaded document(s) into exactly one JSON object.

Return JSON only.
Do not wrap the JSON in markdown fences.
Do not add explanations, commentary, or extra text.

Only extract sale/commercial documents such as sales invoice, tax invoice, receipt, delivery receipt, purchase order, order confirmation, or similar buyer-seller transaction records.

Treat a document as an eligible proof-of-purchase when all of the following are true:
- A seller or merchant of record can be identified.
- At least one purchased line item is present.
- The document clearly represents a commercial transaction or fulfillment record.

If no qualifying document is present, return {"results":[]}.

If multiple pages/files describe the same transaction (for example, sales invoice on one page and delivery receipt on another), merge them into one result and do not duplicate it.

The JSON must match this structure exactly:
{
  "results": [
    {
      "document_title": string|null,
      "invoice_date": string|null,
      "invoice_number": string|null,
      "currency_code": string|null,
      "gross_amount": number|null,
      "seller": {
        "name": string|null,
        "city": string|null,
        "state": string|null,
        "country": string|null
      },
      "end_customer": {
        "name": string|null,
        "city": string|null,
        "state": string|null,
        "country": string|null,
        "email": string|null
      },
      "purchases": [
        {
          "quantity": number|null,
          "part_number": string|null,
          "description": string|null,
          "serial_numbers": [string, ...],
          "unit_price": number|null
        }
      ]
    }
  ]
}

Hard requirements:
- Every result item must include all keys shown above.
- `seller` must always include `name`, `city`, `state`, and `country`.
- `end_customer` must always include `name`, `city`, `state`, `country`, and `email`.
- `purchases` must always be present, even if empty.
- Every purchase item must include `quantity`, `part_number`, `description`, `serial_numbers`, and `unit_price`.
- `serial_numbers` must always be an array.
- Never omit required keys.
- Use `null` for unknown, missing, unreadable, or ambiguous scalar values.
- Use `[]` for missing repeated values.

Extraction rules:
- Each `results` item is one document or one merged transaction.
- Use exact visible values only. Do not invent, infer, normalize, or paraphrase unsupported text except where code formatting rules below explicitly require normalization.
- Ensure extracted values exactly match the document text. Do not truncate or replace text with ellipsis.
- Avoid duplicate purchase rows.
- `document_title` must describe the document type visible on the page, such as `sales invoice`, `delivery receipt`, or `purchase order`.
- `invoice_date` is the main document or transaction date and must be formatted as `YYYY-MM-DD` when the date is clear; otherwise use `null`.
- `invoice_number` may come from labels such as invoice number, receipt number, order number, DR number, or reference number only when it is clearly the main document or transaction identifier.
- `gross_amount` is the full document total, not a line amount.
- `currency_code` must be a valid 3-letter ISO 4217 code such as `USD`, `EUR`, or `PHP`. Use visible currency symbols, labels, and seller location only as supporting hints. If the code is not clear, use `null`.
- `country` for `seller` and `end_customer` must be a 2-letter ISO 3166-1 alpha-2 code or `null`. Do not output country names such as `Philippines`.
- `seller` is the merchant, store, or vendor that sold the goods or services. Do not confuse the marketplace or platform with the seller unless the marketplace is explicitly the seller of record.
- `end_customer` is the buyer or recipient. Prefer values from labels such as customer, buyer, sold to, bill to, or ship to.
- `email` must be a valid email address or `null`.
- Put purchased line items only in `purchases`.
- Extract one purchase object per visible purchased line item.
- If serial numbers are shown under or near a line item, attach them to that purchase item instead of creating a separate purchase.

HP-specific product guidance:
- `description` in each purchase should describe visible Hewlett-Packard (HP) product lines such as notebooks (Envy, Pavilion, Victus, ZBook, Elitebook, Probook, Omen, Omnibook, Laptop 13-xxxxxxyyy) and printers (LaserJet, OfficeJet, Smart Tank).
- Put visible SKU, model, or part codes in `part_number` using these rules:
  - A purchased item can have multiple model or part codes; join multiple visible codes with a comma.
  - Common formats are 7 to 11 contiguous characters, such as `AB1C2DE` or `AB1C2DE#EFG`.
  - For HP laptops, also consider formats like `aa-yyyyyyyy` where `aa` is one of `13`, `14`, `15`, `16`, or `24`, `yyyyyyyy` is alphanumeric.
- Extract `serial_numbers` using these rules:
  - 10 to 13 contiguous alphanumeric characters only.
  - Do not include prefixes such as `SN`, `S/N`, `SN#`, `SN:`, or `Serial:` in the extracted value.
  - Examples: `CN301234567`, `1AB123CDEF`, `0AB123456C`.

Number parsing:
- Use the seller's location only as a hint when interpreting localized number formats.
- Examples: `1.234,99` may mean 1234.99, `1 234,56` may mean 1234.56, and `1,234.56` may mean 1234.56.

Example valid output:
{
  "results": [
    {
      "document_title": null,
      "invoice_date": null,
      "invoice_number": null,
      "currency_code": null,
      "gross_amount": null,
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
          "quantity": null,
          "part_number": null,
          "description": null,
          "serial_numbers": [],
          "unit_price": null
        }
      ]
    }
  ]
}
