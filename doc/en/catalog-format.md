> [Version française](../fr/catalog-format.md)

# Catalog format, checks and publication

This document describes the Hack Store catalog repository as built here: the format of an app
folder, what a pull request is checked for, what a merge publishes, and the format of
`catalog.json` that the Hack Store and `hackpkg` read. The original decision is
[ADR 0188](https://github.com/Kazo-ca/HackerOs/blob/main/doc/en/wasm/dev/adr/0188-hack-store-and-app-catalog.md)
(§4 to §6); trust and installation rules are in
[ADR 0189](https://github.com/Kazo-ca/HackerOs/blob/main/doc/en/wasm/dev/adr/0189-app-sources-trust-and-per-user-install.md).

## Architecture

```text
main                              reviewed by PR
  apps/<appId>_<version>/         one folder per app version
  categories.json                 closed list of categories
  tools/                          catalog tool (C#) and its tests
  external/HackerOs               submodule: HackerOS libraries the tool reads with
  .github/actions/build-app/      builds an app from its commit (+ CodeQL)
  .github/workflows/validate.yml  checks a PR
  .github/workflows/pr-comment.yml writes the review comment
  .github/workflows/publish.yml   publishes after the merge

packages                          written only by publish.yml, no code history
  catalog.json
  packages/<appId>/<version>/<appId>-<version>.hackapp
  assets/<appId>/<version>/icon.png, screenshots/N.png, description.<lang>.md
```

The tool `tools/HackerOs.MarketPlace.Tool` reads manifests with `AppManifestJsonSerializer` and
`AppManifestValidator`, and packages with `AppPackage.Read`: the HackerOS classes themselves, so the
catalog applies exactly the rules the OS applies at install time. The package reader (ADR 0182) is
not in the 2.1.0 NuGet packages yet: the tool builds the two libraries from the
`external/HackerOs` submodule, on the `v2.2.1` tag (2026-11-01: the package reader of that version
accepts the satellite assemblies, HackerOS ADR 0222). Once a release carries them, the submodule is
replaced by `PackageReference` items.

The CI builds a submitted app at its commit with the app's own `external/HackerOs` submodule (an
app made with the `hackeros-solution` template reads its HackerOS version there): the
`build-app` action fetches that one submodule, and only when it points to
`https://github.com/Kazo-ca/HackerOs`.

## A version folder

The name is `<appId>_<version>`: the app id (at least three lower-case dot-separated segments,
the `AppManifestValidator` rule) and its exact SemVer. Any file that is not in this table is
refused, so no binary enters `main`.

| File | Required | Rule |
|---|---|---|
| `icon.png` | yes | PNG 256×256, 200 KB at most |
| `short_description.<lang>.txt` | `en` yes | UTF-8, one non-empty line, 120 characters at most |
| `description.<lang>.md` | `en` yes | UTF-8, not empty, 20 KB at most |
| `manifest.json` | yes | the one **of the package** `HackerOsPack` built (not the project's: the package adds the static resources to it, HackerOS ADR 0221); strict read and OS validation; `id` and `version` equal to the folder name |
| `store.json` | yes | see below |
| `screenshots/1.png` … `5.png` | no | 1 to 5 PNG numbered without gaps, 1 MB and 3840 px per side at most |

`<lang>` is a language code such as `fr` or `pt-BR`.

### `store.json`

Read strictly: an unknown or duplicate property is an error.

| Property | Required | Rule |
|---|---|---|
| `repository` | yes | `https://github.com/<owner>/<repo>`, public repository |
| `commit` | yes | full 40-character lower-case hexadecimal SHA |
| `category` | yes | an `id` from `categories.json` |
| `tags` | no | at most 10, lower case, digits and `-`, 30 characters at most, no duplicate |
| `license` | yes | SPDX expression (`MIT`, `GPL-3.0-only`, `MIT OR Apache-2.0`) |
| `project` | no | relative path of the `.csproj` to build (letters, digits, `.`, `_`, `-`, `/`, no `..`) |
| `kind` | no | only `app` for now; theme packs (ADR 0190) come with their work item |

### `categories.json`

An array of `{ "id": "...", "labels": { "en": "...", "fr": "..." } }`. The `id` is lower case
(2 to 32 characters, digits and `-`), unique, and English and French labels are required. A
category is added by a PR on this file.

## Checking a pull request (`validate.yml`)

The numbers are those of ADR 0188 §5.

| Step | Where | What is done |
|---|---|---|
| — | job *Catalog tool tests* | tests of the tool |
| — | job *Check folders* | a file of an already merged folder may neither change nor disappear; a PR may only add folders |
| 1, 2 | job *Check folders* | `validate` on `categories.json` and every folder |
| 3 | job *Build* | `git fetch` of the commit from the `store.json` repository; fails if it does not exist |
| 4 | job *Build* then *review* | `dotnet build -c Release -t:HackerOsPack`, then `check-package`: the package must be read by `AppPackage.Read` and its manifest must be the folder's |
| 5 | — | API check against the trimmed runtime: done by `hackpkg` at install time (ADR 0182); not in CI for v1 (Benoit's decision, Q1 = A; [#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3)) |
| 6 | job *Build* | CodeQL on the app's code: C# from its traced build (`manual`); JavaScript/TypeScript, when the repository has some outside `external/`, from its sources with the runner's CodeQL CLI (`none`, the only mode JavaScript supports, which the CodeQL action would run on the catalog's own directory); `external/**` (the HackerOS submodule) is ignored; results are summed up in the comment |
| 7 | *review* then `pr-comment.yml` | comment: source, license, SDK, package size and SHA-256, capabilities (dangerous first, marked ⚠️), JavaScript files, assemblies, CodeQL results |

Then a maintainer reads the code and approves; nothing is merged automatically.

### Workflow security

- The jobs that build the app run its code (MSBuild can run anything): they only have a read-only
  token and no secret. The tool is built and run in another job, which only reads the artifacts.
- A PR from a fork cannot write a comment: `pr-comment.yml` runs from `main` on the
  `workflow_run` event, reads the artifact as data, and only comments when the PR number names an
  open PR whose head is the checked commit.
- Values coming from the PR (folder names, `store.json` fields) reach the shell through
  environment variables, never through `${{ }}`, and are only used after the format check.
- A PR can change the workflows themselves: the reviewer checks that the PR only touches `apps/`
  (otherwise it is a maintenance PR, reviewed as such).

## Publication (`publish.yml`)

After a merge into `main` that adds folders (or changes `categories.json`):

1. *plan* lists the folders the push added and checks them again;
2. *build* rebuilds each app at the same commit (no CodeQL), with a read-only token;
3. *publish* (the only job with `contents: write`) runs the tool's `publish` on a copy of the
   `packages` branch (created as an orphan the first time), then pushes a commit.

The tool checks every folder and package again; if one fails, nothing is written. It copies the
package and the images, rewrites `catalog.json`, then keeps only the **3 newest versions** of each
app (older ones leave `packages/` and `assets/`; their folder stays in the history of `main`). An
older version published late does not change the app's listing.

To publish a version again (for example after a change of the tool): run **Publish catalog** by
hand with the folder names separated by spaces.

### `catalog.json`

```json
{
  "schemaVersion": 1,
  "categories": [
    { "id": "productivity", "labels": { "en": "Productivity", "fr": "Productivité" } }
  ],
  "apps": [
    {
      "appId": "org.example.notes",
      "kind": "app",
      "name": "Notes",
      "category": "productivity",
      "tags": ["notes", "markdown"],
      "shortDescriptions": { "en": "Take notes in Markdown.", "fr": "Prendre des notes en Markdown." },
      "license": "MIT",
      "repository": "https://github.com/example/HackerOs-App-Notes",
      "latestVersion": "1.2.0",
      "versions": [
        {
          "version": "1.2.0",
          "commit": "3f2a9c1e0b7d4c5a8e6f1a2b3c4d5e6f7a8b9c0d",
          "package": "packages/org.example.notes/1.2.0/org.example.notes-1.2.0.hackapp",
          "sha256": "…",
          "size": 48213,
          "publishedAt": "2026-10-30T12:00:00+00:00",
          "capabilities": ["clipboard.read"],
          "sdkCompatibility": { "minimumVersion": "2.1.0" },
          "icon": "assets/org.example.notes/1.2.0/icon.png",
          "screenshots": ["assets/org.example.notes/1.2.0/screenshots/1.png"],
          "descriptions": { "en": "assets/org.example.notes/1.2.0/description.en.md" },
          "signature": "MEUCIQ…"
        }
      ]
    }
  ]
}
```

- Paths are relative to the root of the `packages` branch.
- Apps are sorted by `appId`, their versions from newest to oldest.
- The listing fields (`name`, `category`, `tags`, `shortDescriptions`, `license`, `repository`)
  come from the newest version. `capabilities` and `sdkCompatibility` are per version, so the Store
  can show what an update adds.
- `schemaVersion` changes when the format changes incompatibly; a reader refuses a higher version
  than its own.
- `signature` (optional): ECDSA P-256 / SHA-256 signature of the `.hackapp` bytes, in IEEE P1363
  form (`r||s`, 64 bytes), base64 ([ADR 0214](https://github.com/Kazo-ca/HackerOs/blob/main/doc/en/wasm/dev/adr/0214-hack-store-hot-add-signing-transactions.md)
  of HackerOs, §2). `publish.yml` signs each new version with the `HACK_STORE_SIGNING_KEY` secret
  when it exists; without it, versions are published unsigned. A version already published keeps
  its signature.

### Signing key (for Benoit)

1. On your computer, in a copy of this repository:
   `dotnet run --project tools/HackerOs.MarketPlace.Tool -- keygen --private-key ~/hack-store-signing.pem`.
   The tool writes the private key to a new file (`0600`), never replaces an existing file, and
   prints the public key.
2. Copy the content of the `.pem` file into the repository's `HACK_STORE_SIGNING_KEY` secret
   (Settings ▸ Secrets and variables ▸ Actions), then keep the file offline. The private key never
   goes through an agent or a PR.
3. Put the public key in `OfficialCatalogSigningKey` (`AppPackageDefaults.cs`) or in HackerOs's
   `HackerOsAppSigningKeys` MSBuild property (`catalog:official=<public key>`): from that OS
   version on, every package of the official catalog must carry a valid signature.
4. Publish the existing versions again (*Publish catalog* workflow ▸ Run workflow, with their
   folders) so they get a signature before the OS requires it.

## The catalog tool

```text
dotnet run --project tools/HackerOs.MarketPlace.Tool -- validate --root . [folder...]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- check-package --root . --folder <folder> --report <f.md> [--package <f.hackapp>] [--sarif <dir>]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- publish --root . --built <dir> --output <packages copy> [--keep 3] [--published-at <ISO 8601>]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- keygen --private-key <file.pem>
dotnet run --project tools/HackerOs.MarketPlace.Tool -- sign --package <f.hackapp> [--private-key <file.pem>]
dotnet test tools/HackerOs.MarketPlace.slnx
```

Exit code: 0 when all is well, 1 when something is refused, 2 for a usage error. In GitHub
Actions, errors become annotations on the PR's files. Clone with `--recurse-submodules` (or
`git submodule update --init`) before building the tool.

## Repository settings (for Benoit)

- Protection of `main`: PR required, **Code Owners review** required (`.github/CODEOWNERS` names
  `@werddomain`), checks *Catalog tool tests*, *Check folders* and *Check packages and write the
  review* required.
- Actions ▸ General: require approval before running workflows of PRs from outside contributors.
- Protection of `packages`: writing reserved to GitHub Actions.

## Decisions taken in this work item

- `store.json` gets two optional fields: `project` (the `.csproj` to build) and `kind` (only `app`
  for now). Benoit's decision (Q2 = A).
- Limits the ADR did not set: long description 20 KB, screenshots 1 MB and 3840 px per side,
  10 tags.
- `catalog.json` also holds the categories and their labels, so the Store reads a single file.
- A merged version is immutable: a PR may only add folders.
- The API check in CI (ADR 0188 §5 step 5) is left to install time for v1 (Benoit's decision,
  Q1 = A); it comes with [#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3).

## Tasks

- [x] `categories.json`, PR template, `CODEOWNERS`.
- [x] Catalog tool: format, manifest, package, review comment, publication, tests.
- [x] `validate.yml`, `pr-comment.yml`, `publish.yml` and the `build-app` action.
- [x] README (English, French) and this document.
- [ ] API check against the trimmed runtime in CI ([#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3)).
- [ ] Replace the submodule by the NuGet packages once a release carries them.
- [ ] Theme packs (`kind: "theme-pack"`, ADR 0190).
