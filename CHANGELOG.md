# Changelog

All notable changes to this project will be documented in this file.

## [3.1.0] - Unreleased

> Planned next release. The latest published NuGet release is still `3.0.0`.

### Added

- Added analyzer diagnostics for invalid localization JSON files (`SL001`).
- Added analyzer diagnostics for duplicate flattened localization keys (`SL002`).
- Added analyzer diagnostics for conflicting generated localization identifiers (`SL003`).
- Added tests that cover the new source-generation diagnostics.

### Changed

- Refactored the localization generator into `LSourceGenerator`, `ResourceSourceGenerator`, and `EnumSourceGenerator`.
- Moved shared source-generation helpers into a dedicated helpers module.
- Updated release metadata, project package notes, and repository documentation for the upcoming `3.1.0` release.

## [3.0.0]

### Added

- Published the first public NuGet release for `Senlinz.Localization`.
- Published the shared runtime contracts package `Senlinz.Localization.Abstractions`.
