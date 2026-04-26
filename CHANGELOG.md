# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [3.5.0] - 2026-04-26

### Changed

- Made diagnostic `SL004` explicit opt-in through `SenlinzLocalizationWarnMissingPrimaryFile`, so missing primary localization files no longer warn by default.
- Updated package versions, release metadata, and documentation for the `3.5.0` release.

## [3.4.0] - 2026-04-26

### Fixed

- Automatically includes `$(SenlinzLocalizationFolder)/**/*.json` as `AdditionalFiles` for direct package references so existing localization files are discovered without extra project configuration.

### Changed

- Removed the accidentally introduced `buildTransitive` packaging so the original direct-reference-only design for generating `L.g.cs` is restored.
- Updated release metadata and documentation for the `3.4.0` release.

## [3.3.0] - 2026-04-26

### Added

- Added configurable `SenlinzLocalizationFolder` support so the generator only scans JSON files under the selected folder, defaulting to `L` and including all nested subfolders.

### Changed

- Updated release metadata and documentation for the `3.3.0` release.

## [3.2.0] - 2026-04-18

### Added

- Added diagnostic `SL004` to warn when the configured primary localization file is missing from `AdditionalFiles`.

### Changed

- Generated `L` members are now readonly fields to prevent accidental mutation at runtime.
- Resolver now materializes read-only localization dictionaries to avoid downstream callers mutating cached resources.

## [3.1.0] - 2026-04-17

### Added

- Added analyzer diagnostics for invalid localization JSON files (`SL001`).
- Added analyzer diagnostics for duplicate flattened localization keys (`SL002`).
- Added analyzer diagnostics for conflicting generated localization identifiers (`SL003`).
- Added tests that cover the new source-generation diagnostics.

### Changed

- Refactored the localization generator into `LSourceGenerator`, `ResourceSourceGenerator`, and `EnumSourceGenerator`.
- Moved shared source-generation helpers into a dedicated helpers module.
- Updated release metadata, project package notes, and repository documentation for the `3.1.0` release.

## [3.0.0]

### Added

- Published the first public NuGet release for `Senlinz.Localization`.
- Published the shared runtime contracts package `Senlinz.Localization.Abstractions`.
