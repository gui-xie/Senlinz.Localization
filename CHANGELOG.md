# Changelog

All notable changes to this project will be documented in this file.

## [3.1.0] - 2026-04-16

### Added

- Added analyzer diagnostics for invalid localization JSON files (`SL001`).
- Added analyzer diagnostics for duplicate flattened localization keys (`SL002`).
- Added analyzer diagnostics for conflicting generated localization identifiers (`SL003`).
- Added tests that cover the new source-generation diagnostics.

### Changed

- Refactored the localization generator into `LSourceGenerator`, `ResourceSourceGenerator`, and `EnumSourceGenerator`.
- Moved shared source-generation helpers into a dedicated helpers module.
- Updated release metadata, project package notes, and repository documentation for the `3.1.0` release.
