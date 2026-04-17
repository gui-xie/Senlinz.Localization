# Release Notes

## Upcoming release - 3.1.0

The latest published NuGet release is `3.0.0`. The items below describe the next planned package update.

### Highlights

- Added generator diagnostics for invalid localization JSON files (`SL001`).
- Added generator diagnostics for duplicate flattened localization keys (`SL002`).
- Added generator diagnostics for conflicting generated localization identifiers (`SL003`).
- Refactored the localization generator into focused source-generation components to make future maintenance and diagnostics work easier.

### Packages

- Latest published:
  - `Senlinz.Localization` `3.0.0`
  - `Senlinz.Localization.Abstractions` `3.0.0`
- Planned next release:
  - `Senlinz.Localization` `3.1.0`
  - `Senlinz.Localization.Abstractions` `3.1.0`

### Release checklist

- Keep repository package versions aligned with the next planned release version `3.1.0`.
- Tag the release with either `v3.1.0` or `V3.1.0`.
- Publish the generated NuGet packages from the release workflow artifacts.
