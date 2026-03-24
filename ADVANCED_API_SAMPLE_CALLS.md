## Endpoint
POST /advanced/query

Use multipart/form-data so the PDF can be uploaded as binary.

## Request Contract

Required fields:
- requestReferenceId (string, caller-generated correlation ID)
- responseReferenceId (string, caller-generated response ID to echo in API responses)
- prompt (string)

Optional fields:
- sessionId (string)
- userId (string)
- documentName (string)
- currentPage (integer, 1-based page number the user is currently viewing)
- highlights (JSON array serialized as a string in multipart)
- pdf (file, application/pdf)

### Highlights JSON Shape

```json
[
  {
    "word": "randomized controlled trial",
    "pages": [2, 4]
  },
  {
    "word": "adverse events",
    "pages": [6, 9]
  }
]
```

## Sample curl Request

With document:

```bash
curl -X POST "https://your-api.example.com/advanced/query" \
  -H "Authorization: Bearer $TOKEN" \
  -F "requestReferenceId=caller-20260324-001-req" \
  -F "responseReferenceId=caller-20260324-001-res" \
  -F "prompt=Summarize this medical paper with focus on study design, primary outcomes, adverse events, and limitations. Include cited pages." \
  -F 'highlights=[{"word":"randomized controlled trial","pages":[2,4]},{"word":"adverse events","pages":[6,9]}]' \
  -F "pdf=@/path/to/medical-paper.pdf;type=application/pdf" \
  -F "documentName=medical-paper.pdf" \
  -F "currentPage=6" \
  -F "sessionId=session-42"
```

No document (prompt only):

```bash
curl -X POST "https://your-api.example.com/advanced/query" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "requestReferenceId": "caller-20260324-201-req",
    "responseReferenceId": "caller-20260324-201-res",
    "prompt": "Give me a concise medical summary format I can use to evaluate clinical papers.",
    "sessionId": "session-42"
  }'
```

## Success Response Contract

```json
{
  "responseReferenceId": "caller-20260324-001-res",
  "requestReferenceId": "caller-20260324-001-req",
  "answer": "This medical paper reports a randomized controlled trial with statistically significant improvement in the primary endpoint, while noting a higher mild adverse-event rate in the intervention arm and limited external validity.",
  "references": [
    {
      "referenceId": "ref-1",
      "page": 2,
      "label": "Study design",
      "quote": "We conducted a multicenter, randomized, double-blind, placebo-controlled trial.",
      "highlightedWord": "randomized controlled trial",
      "confidence": 0.94
    },
    {
      "referenceId": "ref-2",
      "page": 6,
      "label": "Safety profile",
      "quote": "Mild adverse events occurred more frequently in the intervention group than in controls.",
      "highlightedWord": "adverse events",
      "confidence": 0.91
    }
  ]
}
```

## Error Response Contract

```json
{
  "responseReferenceId": "id-20260324-001-res",
  "requestReferenceId": "id-20260324-001-req",
  "error": {
    "code": "INVALID_PDF",
    "message": "The uploaded file is not a valid PDF."
  }
}
```

## PDF upload policy

Do not send PDFs as base64 in JSON. PDFs must be uploaded using `multipart/form-data` (see the curl example above) when including a document. For prompt-only requests, omit `documentName` and do not attach a PDF.

Prompt-only JSON request (no document):

```json
{
  "requestReferenceId": "caller-20260324-202-req",
  "responseReferenceId": "caller-20260324-202-res",
  "prompt": "Summarize current best-practice criteria for reading a randomized clinical trial.",
  "sessionId": "session-42"
}
```

Prompt-only success response:

```json
{
  "responseReferenceId": "caller-20260324-202-res",
  "requestReferenceId": "caller-20260324-202-req",
  "answer": "When reviewing an RCT, prioritize population definition, randomization method, blinding, primary endpoint selection, effect size, confidence intervals, adverse-event profile, and external validity.",
  "references": []
}
```

## JSON Validation Samples (Valid and Invalid)

Use these examples to test request validation and response handling for medical paper summaries.

### Valid Sample 1: Full medical summary request

Request:

```json
{
  "requestReferenceId": "caller-20260324-101-req",
  "responseReferenceId": "caller-20260324-101-res",
  "prompt": "Summarize this cardiology paper with focus on population, intervention, comparator, outcomes, and limitations.",
  "documentName": "cardiology-rct.pdf",
  "currentPage": 5,
  "sessionId": "session-42",
  "highlights": [
    {
      "word": "double-blind",
      "pages": [2]
    },
    {
      "word": "primary endpoint",
      "pages": [5, 6]
    }
  ]
}
```

Response:

```json
{
  "responseReferenceId": "caller-20260324-101-res",
  "requestReferenceId": "caller-20260324-101-req",
  "answer": "The paper describes a double-blind RCT in adults with heart failure and reports significant improvement in the primary endpoint, with modest increases in mild adverse events.",
  "references": [
    {
      "referenceId": "ref-1",
      "page": 2,
      "label": "Trial design",
      "quote": "This study was double-blind and placebo-controlled.",
      "highlightedWord": "double-blind",
      "confidence": 0.95
    },
    {
      "referenceId": "ref-2",
      "page": 6,
      "label": "Primary outcome",
      "quote": "The intervention group achieved a statistically significant reduction in the primary endpoint.",
      "highlightedWord": "primary endpoint",
      "confidence": 0.92
    }
  ]
}
```

### Valid Sample 2: Safety-focused summary request

Request:

```json
{
  "requestReferenceId": "caller-20260324-102-req",
  "responseReferenceId": "caller-20260324-102-res",
  "prompt": "Summarize safety outcomes and adverse events from this oncology paper and cite the supporting pages.",
  "documentName": "oncology-phase3.pdf",
  "currentPage": 9,
  "highlights": [
    {
      "word": "adverse events",
      "pages": [8, 9, 10]
    }
  ]
}
```

Response:

```json
{
  "responseReferenceId": "caller-20260324-102-res",
  "requestReferenceId": "caller-20260324-102-req",
  "answer": "Grade 3 or higher adverse events were more frequent in the treatment arm, while treatment discontinuation rates were similar across groups.",
  "references": [
    {
      "referenceId": "ref-1",
      "page": 9,
      "label": "Severe adverse events",
      "quote": "Grade 3 or higher adverse events occurred in 28% of intervention patients versus 19% in controls.",
      "highlightedWord": "adverse events",
      "confidence": 0.9
    }
  ]
}
```

### Invalid Sample 1: Missing required IDs

Request (invalid):

```json
{
  "prompt": "Summarize this neurology paper.",
  "documentName": "neurology-study.pdf",
  "highlights": [
    {
      "word": "primary endpoint",
      "pages": [4]
    }
  ]
}
```

Expected error response:

```json
{
  "responseReferenceId": "unknown",
  "requestReferenceId": "unknown",
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "requestReferenceId and responseReferenceId are required."
  }
}
```

### Invalid Sample 2: Wrong data type in pages

Request (invalid):

```json
{
  "requestReferenceId": "caller-20260324-103-req",
  "responseReferenceId": "caller-20260324-103-res",
  "prompt": "Summarize outcomes from this infectious disease trial.",
  "documentName": "infectious-disease-trial.pdf",
  "highlights": [
    {
      "word": "mortality",
      "pages": "7"
    }
  ]
}
```

Expected error response:

```json
{
  "responseReferenceId": "caller-20260324-103-res",
  "requestReferenceId": "caller-20260324-103-req",
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "highlights[0].pages must be an array of integers."
  }
}
```
