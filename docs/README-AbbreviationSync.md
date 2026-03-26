# Abbreviation Sync Guide

This document explains how abbreviation ingestion works end-to-end in SmartGlassesMiddleware.

## Goal

The system builds a searchable abbreviation dictionary so commands and highlighting can match both forms of the same concept.

Example:
- ACE
- Angiotensin Converting Enzyme

Both can be treated as related terms during search/highlight operations.

## Data Sources

The sync pipeline merges data from these sources:

1. openFDA
- Endpoint: https://api.fda.gov/drug/label.json
- Used for SPL/label text extraction.

2. DailyMed
- Index endpoint: https://dailymed.nlm.nih.gov/dailymed/services/v2/spls.json
- Detail endpoint per setid: https://dailymed.nlm.nih.gov/dailymed/services/v2/spls/{setid}.xml
- Used for SPL XML text extraction.

3. RxNav (free)
- Endpoint: https://rxnav.nlm.nih.gov/REST/allconcepts.json
- Used for concept names and synonyms.

4. RxTerms (free)
- Endpoint: https://clinicaltables.nlm.nih.gov/api/rxterms/v3/search
- Used for medication display strings.

5. Built-in list
- Hardcoded baseline set in AbbreviationService.
- Always included for resilience.

## Sync Lifecycle

1. App startup
- Cache is loaded from DB.
- Background service checks staleness.

2. Background schedule
- Controlled by AbbreviationSync:IntervalDays.
- Default is monthly (30 days).
- If newest abbreviation UpdatedAt is older than interval, sync runs immediately.

3. Merge and upsert
- Pairs are deduplicated in memory by lowercase (shortForm, expandedForm).
- Existing DB pairs are updated; missing pairs are inserted.

## Extraction Strategy

### Generic text extraction

Pattern-based extraction is used broadly:

1. ABBR (expanded form)
2. expanded form (ABBR)

This is applied to openFDA text and DailyMed XML text, and also to Rx source text payloads.

### Source-specific extraction

Additional source-specific logic exists for medication strings:

1. RxNav medication-name parser
- Scans tokens in concept names/synonyms.
- Maps known pharma shorthand to canonical expansions.

2. RxTerms medication-name parser
- Scans raw display strings.
- Includes structured token parser for dosage/form shorthand.
- Maps trusted forms such as ER, XR, ODT, HCl, MG, ML, TAB, CAP, SOLN, SUSP, INJ, OINT, CRM, GEL, PKT.

### Normalization and quality filters

Before insert:

1. Expanded text normalization
- Whitespace cleanup.
- Known typo/variant normalization.

2. Quality gates
- Reject obvious prose/noise captures.
- Reject numeric-only/phone-like short forms.
- Enforce acronym-shape and acronym-expansion alignment for non built-in sources.

3. Trusted pharma pair bypass
- Known medication shorthand pairs are explicitly accepted.

## Exhaustive Mode

Exhaustive behavior is controlled by caps in appsettings under AbbreviationSync.

Current convention:
- Value <= 0 means unlimited scan for that source.

Keys:

1. OpenFda.MaxRecords
- 0 means page until source is exhausted.

2. DailyMed.MaxXmlFilesPerSync
- 0 means walk all available SPLs from index paging.

3. RxNav.MaxConcepts
- 0 means scan all returned concepts.

4. RxTerms.MaxRecords
- 0 means continue paging until each seed term is exhausted.

5. RxTerms.SeedTerms
- Comma-separated seeds.
- Supports broad crawling (for example a-z and 0-9).

## Configuration Reference

All under AbbreviationSync in appsettings files:

1. IntervalDays
2. OpenFda.Enabled
3. OpenFda.BaseUrl
4. OpenFda.Limit
5. OpenFda.MaxRecords
6. DailyMed.Enabled
7. DailyMed.BulkUrl
8. DailyMed.MaxXmlFilesPerSync
9. RxNav.Enabled
10. RxNav.BaseUrl
11. RxNav.ConceptTypes
12. RxNav.MaxConcepts
13. RxTerms.Enabled
14. RxTerms.BaseUrl
15. RxTerms.SeedTerms
16. RxTerms.PageSize
17. RxTerms.MaxRecords

## Main Files

1. Sync and parsing logic
- src/SmartGlassesMiddleware/Services/AbbreviationService.cs

2. Scheduler
- src/SmartGlassesMiddleware/Services/AbbreviationSyncBackgroundService.cs

3. Startup wiring and table/index ensure
- src/SmartGlassesMiddleware/Program.cs

4. Entity and DbContext
- src/SmartGlassesMiddleware/Models/Abbreviation.cs
- src/SmartGlassesMiddleware/Data/ApplicationDbContext.cs

## Operational Notes

1. High-cost runs
- Exhaustive mode can take a long time and generate heavy network/database load.

2. Safety behavior
- If an external source fails, app keeps running.
- Existing DB abbreviations and built-in rows remain available.

3. Deterministic smoke testing
- For fast validation, temporarily set bounded caps with environment overrides.

## Quick Test Recipe

1. Force stale timestamps in DB.
2. Run container with desired source toggles and temporary caps.
3. Inspect logs for extracted counts per source.
4. Check DB counts grouped by Source.

Example SQL check:

SELECT "Source", COUNT(*)
FROM "Abbreviations"
GROUP BY "Source"
ORDER BY 1;
