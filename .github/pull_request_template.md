## Scope

<!-- What this change does, and which task it belongs to. State what it does not do. -->

## Validation

Record every check with `PASS`, `FAIL`, `SKIPPED (environment)`, or
`NOT RUN (owner validation pending)`, and name the platform. Compilation is not a
runtime test.

| Check | Platform | Result |
| --- | --- | --- |
| `dotnet restore Nymeria.OMSI.slnx` |  |  |
| `dotnet build Nymeria.OMSI.slnx --configuration Release` |  |  |
| `dotnet test Nymeria.OMSI.slnx --configuration Release` |  |  |
| `scripts/check-commit-trailers.sh <range>` |  |  |
| Windows host run (owner) | Windows | `NOT RUN (owner validation pending)` |

## Windows runtime

- [ ] Not affected by this change.
- [ ] Owner validation required. Manual smoke test: [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md#windows-manual-smoke-test)
- Owner result (if reported): <!-- PASS/FAIL, console output, exit code -->

## Dependencies and engine pin

- [ ] No dependency or engine change.
- [ ] Engine submodule pointer changed. Old commit: `…` New commit: `…`
      (separate, reviewed engine upgrade)
- [ ] Package/version change: <!-- name, old version, new version, reason -->

## Checklist

- [ ] Scope stays inside the current task; no OMSI parsing or simulation added early.
- [ ] No engine source, example code, shader asset, or third-party implementation copied.
- [ ] `external/Nymeria/` is unmodified.
- [ ] No build output, generated shader bytecode, caches, credentials, or machine-specific paths committed.
- [ ] Documentation updated where behavior, policy, or the engine pin changed.
- [ ] No AI attribution trailers or footers in commits or this description.
