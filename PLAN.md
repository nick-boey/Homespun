# Implementation Plan: Use Session Log as Session Test Data (chMFDN)

## Issue Summary

Session logs stored as JSONL files in `/data/sessions/{projectId}/` were not being used for the mock container's test data. The mock container was using hardcoded demo data that displayed correctly, but real session data was available and should be used instead for more realistic testing.

**Status: IMPLEMENTED**

## Implementation Complete

### Changes Made

1. **Created JsonlSessionLoader service** (`src/Homespun/Features/Testing/Services/`)
   - `IJsonlSessionLoader.cs` - Interface for loading sessions from JSONL files
   - `JsonlSessionLoader.cs` - Implementation that:
     - Loads messages from JSONL files
     - Parses all content types (Text, Thinking, ToolUse, ToolResult)
     - Loads session metadata from `.meta.json` files
     - Handles multiple projects and sessions

2. **Updated MockServiceExtensions.cs**
   - Registered `IJsonlSessionLoader` as a singleton service

3. **Updated MockDataSeederService.cs**
   - Added `IJsonlSessionLoader` dependency injection
   - Changed `SeedDemoSessions()` to `SeedSessionsAsync()` which:
     - First tries to load sessions from JSONL files in `/data/sessions/`
     - Falls back to hardcoded demo data if no JSONL files found

4. **Added comprehensive tests**
   - `JsonlSessionLoaderTests.cs` - 16 unit tests for the loader
   - `JsonlSessionLoaderRealDataTests.cs` - 6 integration tests with real session data

### Test Results

All tests pass:
- 755 total tests (749 existing + 6 new integration tests)
- 16 unit tests specifically for JsonlSessionLoader

### Key Implementation Details

#### JSONL Data Format
The existing JSONL files have correct data structure:
- Messages with `role=1` (Assistant) contain Thinking, Text, and ToolUse content
- Messages with `role=0` (User) contain Text (real user input) or ToolResult content
- Metadata files contain session info (projectId, entityId, mode, model, etc.)

#### Session Loading Flow
1. MockDataSeederService starts on application startup
2. Checks if `/data/sessions/` directory exists
3. Uses JsonlSessionLoader to load all sessions
4. If sessions found, adds them to IClaudeSessionStore
5. Falls back to hardcoded demo data if no JSONL files exist

### Files Created/Modified

#### New Files
- `src/Homespun/Features/Testing/Services/IJsonlSessionLoader.cs`
- `src/Homespun/Features/Testing/Services/JsonlSessionLoader.cs`
- `tests/Homespun.Tests/Features/Testing/JsonlSessionLoaderTests.cs`
- `tests/Homespun.Tests/Features/Testing/JsonlSessionLoaderRealDataTests.cs`

#### Modified Files
- `src/Homespun/Features/Testing/MockServiceExtensions.cs` - Added IJsonlSessionLoader registration
- `src/Homespun/Features/Testing/MockDataSeederService.cs` - Added JSONL loading logic

## Usage

When the mock container starts:
1. If `/data/sessions/` contains JSONL files, they will be loaded automatically
2. Sessions appear in the UI with real message data
3. All content types render correctly (text, thinking, tool uses, tool results)

## Definition of Done

- [x] JsonlSessionLoader created with JSONL parsing
- [x] Unit tests pass for JsonlSessionLoader
- [x] Integration tests pass with real session data
- [x] MockDataSeederService loads from JSONL files
- [x] Falls back to demo data when no JSONL available
- [x] All 755 tests pass
- [x] Build succeeds without errors
