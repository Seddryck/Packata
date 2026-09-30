# Changelog

## Unreleased

### Breaking changes

- Made `Packata.Core` standards-neutral and moved the Data Package v2 model, validation, and JSON/YAML serialization to `Packata.DataPackage`.
- Replaced Data Package-specific reader and provisioner entry points with canonical endpoint/schema contracts.
- Renamed storage abstractions from package-specific names to document-neutral names.
- Removed the superseded `Packata.DataContractSpecification` projects; ODCS support remains in `Packata.OpenDataContract`.

### Added

- Canonical contract, asset, schema, field, endpoint, governance, diagnostics, and extension metadata models.
- Data Package v2 and ODCS adapters into the canonical model.
- Concurrent-safe asynchronous readers for delimited, spreadsheet, Parquet, and database endpoints.
