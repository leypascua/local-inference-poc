# Prompt Workshop Implementation Plan

## Goal

Build `Prompt Workshop` as a self-contained full-stack TanStack app in `src/prompt-workshop/`.

The app is a single-user, local-first workspace for:
- authoring prompts
- optionally authoring a JSON Schema string for structured responses
- uploading document files (`pdf`, `jpeg`, `jpg`, `png`)
- running the same project against different inference providers
- viewing the latest saved result for each file

This plan is intentionally written for an LLM implementation agent. Follow it exactly unless a later review explicitly changes the plan.

## Source Of Truth

- Product concept: `specs/prompt-workshop.spec.md`
- Rough UI layout: `specs/prompt-workshop-wireframe.png`
- All new implementation lives in: `src/prompt-workshop/`
- Use `npm` as the package manager

## Non-Negotiable Constraints

- Build one full-stack TanStack app only; do not create a separate backend service outside `src/prompt-workshop/`.
- Do not depend on `src/invoice-extraction-api/` at runtime.
- It is acceptable to borrow ideas from existing repo code, but Prompt Workshop must run independently.
- Use SQLite for metadata, uploaded file contents, and results; use the local filesystem only for temporary working files.
- Store only the latest result per file; reruns overwrite previous results.
- A project can enable or disable structured response schema mode.
- The structured response schema is stored as a string containing JSON Schema.
- Success means the provider returned parseable JSON.
- If schema mode is enabled and validation fails, still save the result and mark it as schema-invalid.
- Support provider switching inside a project at any time.
- Implement `Mistral OCR` only as a stub for now.

## Tech Stack

- Full-stack framework: TanStack Start
- Frontend: React + TypeScript
- Routing: TanStack Router
- Data fetching/cache: TanStack Query
- UI primitives: shadcn/ui
- Editors: Monaco Editor
- Database: SQLite
- ORM/migrations: Drizzle ORM + Drizzle Kit
- Validation: Zod for request/input validation, AJV for JSON Schema validation
- Package manager: `npm`

## App Shape

Build the app as a project-centric workspace.

- Left sidebar:
  - Projects list
  - Providers list
  - Create new actions
- Main panel:
  - Active project header
  - Configure action
  - Retry All action
  - File upload dropzone
  - File list with status and `View Result`
- Project configuration surface:
  - provider selector
  - model selector
  - prompt editor
  - `Enable structured response schema` toggle
  - schema editor when enabled
- Result viewer:
  - raw response
  - parsed JSON
  - JSON parse status
  - schema validation status
  - provider/model metadata
  - error details

## Required Directory Layout

Create and keep all implementation under `src/prompt-workshop/`.

Recommended structure:

```text
src/prompt-workshop/
  package.json
  tsconfig.json
  vite.config.ts
  drizzle.config.ts
  src/
    routes/
    components/
    lib/
    styles/
    server/
      db/
      repos/
      files/
      inference/
      validation/
      projects/
      providers/
      results/
  data/
    .gitkeep
```

## Persistent Storage Rules

Use this storage layout:

- SQLite database: `src/prompt-workshop/data/prompt-workshop.db`
- Uploaded files: stored as SQLite BLOBs in the app database
- Temp working files: `src/prompt-workshop/data/tmp/`

Rules:

- Store uploaded files as database blobs.
- Always store file metadata in SQLite.
- Always overwrite the prior saved result for the same file.
- Preserve uploaded files until the user deletes them.
- Use the local filesystem only for temporary working files derived from SQLite-backed uploads.

## Data Model

Implement these tables exactly unless a later review approves a schema change.

### `providers`

- `id` text primary key
- `name` text not null
- `adapterType` text not null
- `baseUrl` text not null
- `apiKey` text not null
- `createdAt` text not null
- `updatedAt` text not null

Allowed `adapterType` values:
- `openai_compatible`
- `mistral_ocr`

### `providerModels`

- `id` text primary key
- `providerId` text not null
- `modelName` text not null
- `sortOrder` integer not null

### `projects`

- `id` text primary key
- `name` text not null
- `versionLabel` text not null
- `providerId` text not null
- `selectedModel` text not null
- `promptText` text not null
- `schemaEnabled` integer not null
- `structuredResponseSchema` text nullable
- `createdAt` text not null
- `updatedAt` text not null

### `projectFiles`

- `id` text primary key
- `projectId` text not null
- `originalName` text not null
- `mimeType` text not null
- `sizeBytes` integer not null
- `status` text not null
- `lastRunAt` text nullable
- `createdAt` text not null
- `updatedAt` text not null

Allowed `status` values:
- `idle`
- `running`
- `success`
- `json_invalid`
- `schema_invalid`
- `provider_error`

### `fileContents`

- `fileId` text primary key
- `contentBlob` blob not null
- `sha256` text nullable
- `createdAt` text not null
- `updatedAt` text not null

### `fileResults`

- `fileId` text primary key
- `providerId` text not null
- `modelName` text not null
- `rawResponseText` text nullable
- `parsedJsonText` text nullable
- `jsonParseStatus` text not null
- `schemaValidationStatus` text nullable
- `errorCode` text nullable
- `errorMessage` text nullable
- `durationMs` integer nullable
- `updatedAt` text not null

Interpretation:

- one `projectFiles` row has exactly one `fileContents` row
- one `projectFiles` row has at most one `fileResults` row
- rerunning a file replaces the prior `fileResults` record

## Inference Adapter Contract

Create a shared server-side interface for all provider adapters.

Suggested shape:

```ts
type InferenceAdapterInput = {
  provider: {
    id: string
    name: string
    adapterType: 'openai_compatible' | 'mistral_ocr'
    baseUrl: string
    apiKey: string
  }
  modelName: string
  promptText: string
  schemaEnabled: boolean
  structuredResponseSchema: string | null
  file: {
    fileId: string
    fileName: string
    mimeType: string
    sizeBytes: number
    bytes: Uint8Array
  }
}

type InferenceAdapterOutput = {
  rawResponseText: string
  parsedJsonText: string | null
  durationMs: number
  providerLabel: string
  modelName: string
}
```

Rules:

- All adapters must satisfy the same interface.
- The app must parse JSON after the adapter returns.
- The adapter must not be trusted as the final authority on validity.
- Schema validation always happens in Prompt Workshop server code, not only in the provider.
- If an adapter or dependency requires a file path, materialize a temp file from the SQLite BLOB and delete it after processing.

## Provider Implementations

### 1. `OpenAI API Compatible`

Implement a real adapter that:

- accepts uploaded file bytes from Prompt Workshop SQLite storage
- supports image and PDF project files
- sends prompt text to a provider using OpenAI-compatible APIs
- includes schema instructions when `schemaEnabled` is true
- returns the raw response body or extracted content needed for debugging
- returns parseable JSON when the upstream provider cooperates

Notes:

- If a PDF needs rasterization, do that inside Prompt Workshop server code.
- If a dependency requires a real file path, write a temp file under `src/prompt-workshop/data/tmp/` and remove it after use.
- Keep the implementation generic; Prompt Workshop is not invoice-specific.

### 2. `Mistral OCR`

Implement only a stub adapter for now.

- Do not make any HTTP calls.
- Always return a deterministic dummy JSON object.
- Include the actual uploaded file name in the dummy payload.
- Mark the result in the UI as a stubbed adapter response.

Use this exact payload shape:

```json
{
  "stub": true,
  "adapter": "mistral_ocr",
  "message": "Mistral OCR adapter not implemented yet.",
  "document_count": 1,
  "documents": [
    {
      "file_name": "<actual uploaded filename>",
      "extracted_text": "This is a placeholder response from the Mistral OCR stub."
    }
  ]
}
```

## Project Behavior Rules

These rules are product behavior, not optional implementation details.

- A project always points to one active provider.
- A project always points to one selected model.
- The user can change provider and model at any time.
- A project may have schema mode enabled or disabled.
- If schema mode is disabled:
  - do not send structured schema data to the adapter
  - do not run schema validation after the response
- If schema mode is enabled:
  - the schema string must contain valid JSON Schema before any run can start
  - run schema validation after JSON parsing succeeds
- A successful run means the response parsed as JSON.
- If JSON parsing fails, mark the file as `json_invalid`.
- If JSON parsing succeeds but schema validation fails, mark the file as `schema_invalid`.
- If the adapter errors or upstream request fails, mark the file as `provider_error`.

## Upload Rules

Accept only these file types:

- `application/pdf`
- `image/jpeg`
- `image/jpg`
- `image/png`

Rules:

- Use multipart file upload from the browser.
- Do not use base64 uploads from the web UI.
- Save uploads immediately to SQLite BLOB storage.
- Reject unsupported file types with clear validation messages.
- Reject files larger than `10 MB` based on the original uploaded file size.

## JSON And Schema Validation Rules

Implement validation in this order:

1. adapter returns raw response
2. Prompt Workshop server attempts `JSON.parse`
3. if parse fails:
   - save raw response
   - set `parsedJsonText = null`
   - set `jsonParseStatus = failed`
   - set file status to `json_invalid`
4. if parse succeeds:
   - save raw response
   - save normalized pretty JSON string to `parsedJsonText`
   - set `jsonParseStatus = success`
5. if project schema mode is enabled:
   - validate JSON against AJV using the project's schema string
   - if valid, set file status to `success`
   - if invalid, set file status to `schema_invalid`
6. if schema mode is disabled:
   - set file status to `success`

## Server Surface

Implement Prompt Workshop backend functionality inside TanStack Start server code.

The exact mechanism can be either server functions or API routes, but the app must expose these operations:

- list providers
- create provider
- update provider
- delete provider
- list projects
- create project
- update project
- delete project
- list project files
- upload project files
- delete a project file
- run a single file
- run all files in a project
- fetch the latest result for a file

Implementation rule:

- keep repository logic, file I/O, validation, and inference orchestration in server modules, not directly inside route components

## UI Routes

Implement the following route structure:

- `/`
  - redirects to the first project if one exists
  - otherwise shows an empty-state workspace
- `/projects/$projectId`
  - main project workspace

The main route must render:

- sidebar with projects and providers
- active project header
- configure action
- retry-all action
- drag-and-drop upload area
- file table/list
- result view panel or modal

## UI Requirements

### Sidebar

- show projects section
- show providers section
- support create-new actions for both
- highlight the active project

### Project Configuration UI

- fields:
  - project name
  - version label
  - provider selector
  - model selector
  - prompt editor
  - schema enabled toggle
  - schema editor when enabled
- use Monaco for prompt and schema editing
- validate required fields before save

### File List UI

For each file show:

- file name
- file type
- file size
- current status
- run action
- view result action

### Result Viewer UI

Always show:

- provider used
- model used
- last run time
- duration
- raw response text
- parsed JSON if present
- JSON parse status
- schema validation status when applicable
- error message when applicable

If adapter type is `mistral_ocr`, show a visible badge or note that the response is from a stub.

## Sequential Batch Execution

`Run All` must be sequential in V1.

Rules:

- do not run files concurrently in V1
- update each file to `running` before its execution begins
- persist the result as soon as each file completes
- continue to the next file after success or failure
- the final project view must reflect the latest saved result for every file

## Recommended Implementation Order

Follow this order exactly.

### Phase 1 - Scaffold

1. Create a TanStack Start app in `src/prompt-workshop/` using `npm`.
2. Add TypeScript configuration.
3. Add base styling and shadcn setup.
4. Add TanStack Router route generation/config.

### Phase 2 - Persistence And Server Foundations

1. Add Drizzle + SQLite setup.
2. Define the database schema.
3. Add migration generation and migration runner.
4. Add repository modules for providers, projects, files, file contents, and results.
5. Add file storage helpers for SQLite BLOB persistence and temp-file materialization.

### Phase 3 - Validation And Inference Contracts

1. Add Zod input schemas.
2. Add AJV schema validation service.
3. Add the adapter interface.
4. Add adapter factory/resolver.
5. Add `MistralOcrStubAdapter` first.
6. Add `OpenAiCompatibleAdapter` second.

### Phase 4 - Core Server Operations

1. Implement provider CRUD.
2. Implement project CRUD.
3. Implement file upload/list/delete.
4. Implement single-file run orchestration.
5. Implement run-all sequential orchestration.
6. Implement latest-result fetch.

### Phase 5 - Frontend Workspace

1. Build app shell and sidebar.
2. Build project workspace route.
3. Build provider create/edit UI.
4. Build project config UI.
5. Build upload dropzone.
6. Build file list.
7. Build result viewer.
8. Wire mutations and optimistic refresh behavior carefully.

### Phase 6 - Quality And Hardening

1. Add empty states and loading states.
2. Add error toasts/messages.
3. Add status badges.
4. Add smoke tests.
5. Verify the overwrite behavior for reruns.

## Acceptance Criteria

The implementation is correct only when all of these are true.

- The full app runs from `src/prompt-workshop/` with `npm` commands.
- A single portable SQLite database file contains project metadata, uploaded file contents, and latest results.
- A user can create a provider with adapter type, base URL, API key, and multiple model names.
- A user can create a project with prompt text and optional schema string.
- A project can disable schema mode completely.
- A user can upload supported files into a project.
- The app rejects uploaded files larger than `10 MB`.
- A user can run one file.
- A user can run all files sequentially.
- Switching provider/model in a project affects the next run without creating a new project.
- The app stores only the latest result per file.
- The result viewer shows raw response and parsed JSON.
- The app distinguishes `success`, `json_invalid`, `schema_invalid`, and `provider_error`.
- The `Mistral OCR` provider path works via a stubbed JSON response.

## Guardrails For The Implementation LLM

- Do not add auth.
- Do not add collaboration.
- Do not add export features.
- Do not add run history.
- Do not add provider comparison dashboards.
- Do not make the app invoice-specific.
- Do not create a second service outside `src/prompt-workshop/`.
- Do not persist uploaded files outside the portable SQLite database except for temporary working files.
- Do not skip server-side validation.
- Do not special-case `Mistral OCR` after the adapter layer; it must flow through the same result pipeline.

## Suggested Npm Workflow

Use `npm` consistently.

Suggested commands after scaffolding:

```bash
npm install
npm run dev
npm run build
```

Add package scripts for:

- `dev`
- `build`
- `start`
- `test`
- `db:generate`
- `db:migrate`

## Review Notes

If future reviews change scope, update this plan instead of letting implementation drift.

The next review should focus on:

- exact TanStack Start bootstrap choice
- exact Drizzle schema definitions
- exact adapter request shape for `OpenAI API Compatible`
- whether PDF rasterization happens in V1 or only image passthrough first
