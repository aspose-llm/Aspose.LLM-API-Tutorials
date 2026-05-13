# 05 — Invoice / Receipt → Structured JSON

Turn OCR'd or copy-pasted receipt/invoice text into a strictly-typed JSON
object that an expense system can ingest without further post-processing.

## What it demonstrates

- **Single-object JSON extraction.** Unlike #02 (array of multi-field
  records) and #04 (homogeneous list), this recipe asks the model for one
  nested `{}` payload — vendor block, line-items array, totals. The
  balanced-brace walker recovers the object even if the model wraps it in
  a code fence or adds a preamble.
- **Schema round-trip.** The model's output is deserialised into typed
  records and re-serialised on write. Field ordering becomes stable;
  unknown fields are dropped; missing optional fields land as null.
- **Numbers as numbers.** Monetary fields use `double?`. If the model
  returns `"1250.00"` (quoted), deserialisation fails loudly rather than
  silently coercing.
- **Currency hint.** `--currency-hint USD` (etc.) nudges the model on
  ambiguous symbols (the `$` problem).

## Run it

```bash
cd 08-solutions/05-InvoiceToJson
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run
```

With no arguments, runs against the bundled
[`assets/sample-receipt.txt`](assets/sample-receipt.txt). Write to a
specific output file with `--out parsed.json`. Override currency
detection with `--currency-hint USD|EUR|RUB|...` (ISO 4217).

## Output schema

```json
{
  "vendor": { "name": "...", "address": "...", "tax_id": "..." },
  "document_type": "receipt" | "invoice" | "unknown",
  "document_number": "...",
  "date": "YYYY-MM-DD",
  "currency": "USD",
  "line_items": [
    { "description": "...", "quantity": 2, "unit_price": 280.0, "total": 560.0 }
  ],
  "subtotal": 740.00,
  "tax": 123.33,
  "tax_rate": 0.20,
  "tip": 0,
  "total": 863.33,
  "payment_method": "cash" | "card" | "bank_transfer" | "other"
}
```

Any field may be `null` if the source doesn't contain it.

## Aspose-product synergy

Upstream pipeline you might bolt on (NOT included to keep dependencies
minimal):

```csharp
// Aspose.OCR for image input:
var ocr = new Aspose.OCR.AsposeOcr();
var receiptText = ocr.RecognizeImage("receipt.jpg");

// Aspose.PDF for vendor invoices:
using var pdf = new Aspose.Pdf.Document("invoice.pdf");
var receiptText = new Aspose.Pdf.Text.TextAbsorber().Visit(pdf).Text;

// Then feed receiptText into this tutorial.
```

## Known limitations

- **OCR noise.** `l`-vs-`1` confusions and stray pipes are recovered most
  of the time but occasional misreads slip through. Always reconcile
  `total` against `subtotal + tax`.
- **Locale formats.** European `1.250,50` vs US `1,250.50` — the model
  handles both but compute-after-parse arithmetic in your downstream
  code should respect the parsed numeric values, not the source string.
- **Split payments.** A receipt that splits total across cash + card
  is rendered as a single `payment_method` (the dominant one). Multi-
  payment representation is a stretch goal.
- **Discounts.** Negative line items work; loyalty / coupon codes
  rendered as separate "fees" lines may or may not be picked up.

## Files

- [`Program.cs`](Program.cs) — ~395 lines including comments.
- [`InvoiceToJson.csproj`](InvoiceToJson.csproj)
- [`assets/sample-receipt.txt`](assets/sample-receipt.txt) — bundled
  sample (coffee-shop receipt with realistic OCR-style noise).
