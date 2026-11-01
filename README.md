> [Version française](readme-fr.md)

# HackerOs-AppMarketPlace

The catalog of the **Hack Store**, the app store of [HackerOS](https://github.com/Kazo-ca/HackerOs)
([ADR 0188](https://github.com/Kazo-ca/HackerOs/blob/main/doc/en/wasm/dev/adr/0188-hack-store-and-app-catalog.md)).

- The **`main`** branch holds one folder per app version, reviewed by pull request.
- The **`packages`** branch is written by our workflow after each merge: `catalog.json`, the
  `.hackapp` packages and the images, read by the Hack Store and `hackpkg` through
  `raw.githubusercontent.com`.

An author **never provides a binary**: our CI builds the app itself from the given commit, and
that package is what gets published. What reaches users is exactly the reviewed code.

## Propose an app

1. Make the app repository public and check that it builds with the HackerOS NuGet packages:
   `dotnet build -c Release -t:HackerOsPack` (repository template:
   [HackerOs-AppTemplate](https://github.com/Kazo-ca/HackerOs-AppTemplate)).
2. Fork this repository and add a folder `apps/<appId>_<version>/`:

   ```text
   apps/org.example.myapp_1.0.0/
     icon.png                   PNG 256×256, 200 KB at most
     short_description.en.txt   one line, 120 characters at most (English required)
     short_description.fr.txt   optional, one language per file
     description.en.md          long description (English required), 20 KB at most
     description.fr.md          optional
     manifest.json              the manifest.json of the package HackerOsPack built, unchanged
     store.json                 see below
     screenshots/1.png … 5.png  optional, 1 MB and 3840 px per side at most each
   ```

   ```json
   {
     "repository": "https://github.com/example/HackerOs-App-MyApp",
     "commit": "3f2a9c1e0b7d4c5a8e6f1a2b3c4d5e6f7a8b9c0d",
     "category": "productivity",
     "tags": ["notes", "markdown"],
     "license": "MIT",
     "project": "src/MyApp/MyApp.csproj"
   }
   ```

   `commit` is the full SHA (40 characters); `category` comes from
   [`categories.json`](categories.json); `project` is optional (without it, the repository root
   must hold a single project or solution). A repository made with the `hackeros-solution`
   template holds a solution with a host and tests: name the app's `.csproj` in `project`.

   `manifest.json` is the one **inside the package**, not the one of your project: `HackerOsPack`
   adds every static resource of the app to the package's manifest (HackerOS ADR 0221), and the
   catalog compares that manifest with the package its CI builds. Take it with
   `unzip -p <bin>/hackeros-packages/<version>/<appId>-<version>.hackapp manifest.json`; the
   **Publish to the Hack Store** workflow of the template does it for you.
3. Open a pull request to `main` and fill in the template.
4. The **Validate app submissions** workflow checks the format, builds the app at the given
   commit, compares the generated manifest with yours, runs CodeQL, then writes a comment with the
   requested capabilities (dangerous ones highlighted) and the embedded JavaScript files.
5. A maintainer reads the app's code at that commit and approves. Nothing is merged
   automatically.
6. After the merge, the **Publish catalog** workflow rebuilds the app at the same commit and
   updates the `packages` branch.

A published version never changes: a fix is a new version, so a new folder. The **3 newest
versions** of each app stay downloadable.

## Documentation

- [Catalog format, checks and publication](doc/en/catalog-format.md): every rule, the format of
  `catalog.json`, the catalog tool and the repository settings.

## License

[MIT](LICENSE) for this repository; each app keeps its own license, given in its `store.json`.
