# Release Notes

## 3.2.0 - 2026-04-18

### Highlights

- Emits diagnostic `SL004` when the configured primary localization file is missing, preventing silent generator output.
- Makes generated `L` members readonly to avoid accidental mutation.
- Shields resolver dictionaries behind read-only copies so cached resources cannot be modified after creation.

### Packages

- `Senlinz.Localization` `3.2.0`
- `Senlinz.Localization.Abstractions` `3.2.0`

### Release checklist

- Keep repository package versions aligned with the published release version `3.2.0`.
- Tag the release with either `v3.2.0` or `V3.2.0`.
- Publish the generated NuGet packages from the release workflow artifacts.

## 3.1.0 - 2026-04-17

### Highlights

- Added generator diagnostics for invalid localization JSON files (`SL001`).
- Added generator diagnostics for duplicate flattened localization keys (`SL002`).
- Added generator diagnostics for conflicting generated localization identifiers (`SL003`).
- Refactored the localization generator into focused source-generation components to make future maintenance and diagnostics work easier.

### Packages

- `Senlinz.Localization` `3.1.0`
- `Senlinz.Localization.Abstractions` `3.1.0`

### Release checklist

- Keep repository package versions aligned with the published release version `3.1.0`.
- Tag the release with either `v3.1.0` or `V3.1.0`.
- Publish the generated NuGet packages from the release workflow artifacts.
