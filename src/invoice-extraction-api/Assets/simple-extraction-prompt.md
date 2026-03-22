TASK: Extract the following values from a proof-of-sale document:

document_title: string|null
document_number: string|null
order_date: YYYY-MM-DD|null
currency_iso_code: ISO-4217|null
grand_total: number|null

seller:
  name: string|null
  city: string|null
  country_iso_code: ISO-3166-1|null

buyer:
  name: string|null
  city: string|null
  email: string|null

line_items: array<object>

Extract ALL ROWS from the document’s line-item table and use its columns as the schema for `line_item` objects. Typical fields include:  
- description
- quantity
- unit_price
- product_code or part_number or sku. 

STRICT Output Rules:
- Output must be valid JSON
- Do not include markdown
- Do not include explanations
- Do not include comments
- Do not include trailing text
