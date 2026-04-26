# Release Notes

## 3.5.0 - 2026-04-26

### Highlights

- Makes `SL004` explicit opt-in so projects are not warned about a missing primary localization file unless they enable that check themselves.
- Adds the `SenlinzLocalizationWarnMissingPrimaryFile` MSBuild property so consumers can re-enable the warning when they want stricter validation.
- Updates package metadata and repository documentation for the `3.5.0` release.

### Packages

- `Senlinz.Localization` `3.5.0`
- `Senlinz.Localization.Abstractions` `3.5.0`

### Release checklist

- Keep repository package versions aligned with the published release version `3.5.0`.
- Tag the release with either `v3.5.0` or `V3.5.0`.
- Publish the generated NuGet packages from the release workflow artifacts.

## 3.4.0 - 2026-04-26

### Highlights

- Removes the accidentally introduced `buildTransitive` packaging and restores the intended direct-reference-only design for localization source generation.
- Keeps direct-reference convenience by still auto-including `$(SenlinzLocalizationFolder)/**/*.json` in `AdditionalFiles` for projects that install the package themselves.
- Clarifies in the repository documentation that direct package references were always the intended usage.

### Packages

- `Senlinz.Localization` `3.4.0`
- `Senlinz.Localization.Abstractions` `3.4.0`

### Release checklist

- Keep repository package versions aligned with the published release version `3.4.0`.
- Tag the release with either `v3.4.0` or `V3.4.0`.
- Publish the generated NuGet packages from the release workflow artifacts.

## 3.3.0 - 2026-04-26

### Highlights

- Adds `SenlinzLocalizationFolder` so projects can keep localization JSON in a configurable folder instead of only `L`.
- Scans the configured localization folder recursively, so nested JSON files are discovered without changing generator behavior elsewhere.
- Ignores unrelated JSON files outside the configured localization folder even when they are included in `AdditionalFiles`.

### Packages

- `Senlinz.Localization` `3.3.0`
- `Senlinz.Localization.Abstractions` `3.3.0`

### Release checklist

- Keep repository package versions aligned with the published release version `3.3.0`.
- Tag the release with either `v3.3.0` or `V3.3.0`.
- Publish the generated NuGet packages from the release workflow artifacts.

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
