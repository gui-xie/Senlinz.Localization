# Release Notes

## 3.1.0 - 2026-04-16

### Highlights

- Added generator diagnostics for invalid localization JSON files (`SL001`).
- Added generator diagnostics for duplicate flattened localization keys (`SL002`).
- Added generator diagnostics for conflicting generated localization identifiers (`SL003`).
- Refactored the localization generator into focused source-generation components to make future maintenance and diagnostics work easier.

### Packages

- `Senlinz.Localization` `3.1.0`
- `Senlinz.Localization.Abstractions` `3.1.0`

### Release checklist

- Update NuGet package versions to `3.1.0`.
- Tag the release with either `v3.1.0` or `V3.1.0`.
- Publish the generated NuGet packages from the release workflow artifacts.
