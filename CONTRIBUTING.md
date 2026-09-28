# Contributing to Mangarr

Thanks for helping. Bug reports and feature requests go in
[GitHub issues](https://github.com/opentomedb/mangarr/issues); catalogue data (dates, volume
counts, missing series) goes to [OpenTome](https://github.com/opentomedb/opentome). This file
is for working on the code.

## Orientation

Mangarr is Readarr (C# / .NET 6 backend, React frontend) with manga concepts swapped in.
Read [`CONTEXT.md`](CONTEXT.md) first: it maps Readarr's concepts to Mangarr's (an *author* is a
series, a *book* is a volume, an *edition* is a file attach point) and describes the metadata
providers, grabbing and the light-novel file homes. [`docs/adr/`](docs/adr/) records the bigger
decisions.

The ground rule: **stay byte-compatible with Readarr's engine, database schema and API**, and
make Mangarr's changes small and additive (display, extra fields, providers). Prowlarr and other
tools talk to Mangarr as if it were Readarr, so the API must keep working for them.

## Building

The Docker image is the reference build:

```sh
docker build --build-arg MANGARR_VERSION=10.0.0.$(git rev-list --count HEAD) -t mangarr:local .
```

It builds the frontend with Node 18 / Yarn and the backend with the .NET 6 SDK in Release
(analyzers off), on Debian 12. `MANGARR_VERSION` stamps every assembly; without it the build
reports `10.0.0.0`. CI release images add the private history's commit count (the exported `.build-base`),
so their numbers continue the private series; a local build from the public repo numbers from 1.

For the backend alone (from the repo root, .NET 6 SDK):

```sh
dotnet build src/Readarr.sln -c Release -p:Platform=Posix -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false
```

For the frontend: `yarn install --frozen-lockfile --ignore-engines`, then `yarn build`
(`yarn start` watches). The Node version is 18, as in the image.

## Tests

The core test suite is what CI runs before an image is published
([`.github/workflows/docker.yml`](.github/workflows/docker.yml)). The same steps work locally in
the SDK container:

```sh
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:6.0 sh -c '
  apt-get update -qq && apt-get install -y -qq libsqlite3-0=3.34.1-3 >/dev/null
  for p in src/NzbDrone.Mono/Readarr.Mono.csproj src/Readarr.Api.V1/Readarr.Api.V1.csproj src/NzbDrone.Core.Test/Readarr.Core.Test.csproj; do
    dotnet build "$p" -c Release -p:Platform=Posix -p:SolutionDir=/src/src/ -p:RunAnalyzers=false -nologo -v q || exit 1
  done
  cp _output/net6.0/Readarr.Mono.dll _tests/net6.0/
  dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj -c Release -p:Platform=Posix -p:SolutionDir=/src/src/ -p:RunAnalyzers=false --no-build \
    --filter "Category!=ManualTest&Category!=IntegrationTest&Category!=AutomationTest&Category!=WINDOWS"'
```

- `-p:SolutionDir=/src/src/` is required; without it every file fails StyleCop's SA1200.
- The database tests load the system `libsqlite3.so.0`, which the SDK image lacks. The version
  is pinned to the Debian 11 main-archive build because the security-archive build the image
  would otherwise pick has been unavailable.
- Narrow a run with `--filter "FullyQualifiedName~QualityParser"`.
- The baseline is **0 failures**. Readarr's Goodreads/BookInfo integration fixtures are
  permanently `[Ignore]`d (the service they called is gone).

Add a test for every fix. Mangarr's own tests sit beside Readarr's in `src/NzbDrone.Core.Test`.

## Translations

UI text goes through `translate('Key')` in the frontend and `en.json` keys on the server; the
[i18n tooling](scripts/i18n/) enforces it.

- **English** (`src/NzbDrone.Core/Localization/Core/en.json`) is ordinal-sorted. Add keys only
  with `python3 scripts/i18n/en_keys.py add <new-keys.json>`, and check with
  `python3 scripts/i18n/en_keys.py verify <base-ref>`.
- **French, German and Japanese** are written only by `scripts/i18n/locale_tool.py`
  (`sheet` → edit the draft column of `docs/i18n/<lang>-review.csv` → `apply` → `check`), using
  the glossary in `scripts/i18n/glossary.json` (series / volume: série/tome, Serie/Band,
  シリーズ/巻).
- `locale_tool.py` (`sheet` and `check`) diffs against the upstream Readarr fork commit
  `0b79d30`, which this repository's history doesn't contain, so it is maintainer-only for now.
  Contributors: send translation fixes as edits to `docs/i18n/<lang>-review.csv` or the locale
  JSON, and the maintainer runs the tool.
- `LocaleCoverageFixture` fails when a new English key is missing from fr, de or ja, or its
  placeholders differ.
- A new server message (a rejection reason, validation error, queue warning) is built from a
  template on its carrier, never an interpolated string, and needs a `Server*` key;
  `ServerMessageGuardFixture` names any site you missed.
- Native-speaker reviews of the fr/de/ja drafts are very welcome: correct the `docs/i18n` sheet
  and open a pull request.

## Pull requests

- One topic per pull request, with tests, based on the default branch.
- Say whether it adds a database migration. Mangarr's migrations continue Readarr's numbering;
  a new one only adds (a column, a table, config rows) and must be safe for an image rollback.
- A UI change needs its strings translated (above) and should work at phone width.
- Run `yarn lint` for frontend changes.

By contributing you agree that your contribution is licensed under the GPLv3, like the rest of
the code.
