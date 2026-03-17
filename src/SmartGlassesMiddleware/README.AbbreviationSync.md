# Abbreviation Sync (Plain-Language Guide)

This feature helps the app understand medical abbreviations in documents.

Example:
- If a document says `ACE` in one place and `angiotensin converting enzyme` in another place,
- the app can treat them as related,
- and highlight both in the same color.

## What data sources are used?
The app combines 3 sources:

1. openFDA label data
- Source: FDA open data API
- Purpose: pull recent drug-label text and find abbreviation pairs

2. DailyMed SPL bulk data
- Source: DailyMed bulk ZIP/XML feed
- Purpose: scan official label text at scale and find abbreviation pairs

3. Built-in safety list
- Source: local list in code
- Purpose: keep common abbreviations available even if external services are unavailable

## When does syncing happen?
1. On app startup
- The app loads the existing abbreviation dictionary from the database into memory (fast lookup cache).
- It does **not** force a full external fetch every restart.

2. Weekly scheduler
- A background service checks the last successful update time.
- If data is older than the configured interval (default 7 days), it syncs now.
- If data is still fresh, it waits until the next due time.

This is designed for ECS environments where containers can spin up/down.

## How duplicates are prevented
Duplicates are prevented in two places:

1. During collection
- While combining data from openFDA, DailyMed, and built-in list,
- entries are keyed by `(short form + expanded form)` in lowercase.
- If the same pair appears twice, only one is kept.

2. Before database insert
- The app checks if that pair already exists in the database (case-insensitive).
- If it exists, it updates timestamps/source metadata instead of inserting a new row.

Result: the dictionary stays clean and does not grow with repeated duplicate rows.

## How text becomes a usable dictionary
The app looks for patterns in label text such as:
- `ABBR (expanded form)`
- `expanded form (ABBR)`

Then it stores each valid pair as:
- Short form (example: `ACE`)
- Expanded form (example: `angiotensin converting enzyme`)
- Source (`openFDA`, `DailyMed`, or `built-in`)
- Updated time

At runtime, this becomes a searchable dictionary used by highlight commands.

## Configuration knobs
These settings are in `appsettings*.json` under `AbbreviationSync`:

- `IntervalDays`: how often to refresh (default 7)
- `OpenFda.Enabled`: turn openFDA sync on/off
- `OpenFda.BaseUrl`: API base URL
- `OpenFda.Limit`: number of labels to fetch per run
- `DailyMed.Enabled`: turn DailyMed sync on/off
- `DailyMed.BulkUrl`: DailyMed bulk ZIP/XML URL
- `DailyMed.MaxXmlFilesPerSync`: cap for how much DailyMed data to parse per run

## Where this is implemented
- Sync + parsing: `src/SmartGlassesMiddleware/Services/AbbreviationService.cs`
- Weekly scheduler: `src/SmartGlassesMiddleware/Services/AbbreviationSyncBackgroundService.cs`
- Startup wiring: `src/SmartGlassesMiddleware/Program.cs`
- Database model: `src/SmartGlassesMiddleware/Models/Abbreviation.cs`
- Database mapping: `src/SmartGlassesMiddleware/Data/ApplicationDbContext.cs`

## If something goes wrong
- The app continues running.
- It keeps using the last saved dictionary from the database.
- Built-in abbreviations still provide a baseline.
