> [English version](../en/catalog-format.md)

# Format du catalogue, vérifications et publication

Ce document décrit le dépôt catalogue du Hack Store tel qu'il est construit ici : le format d'un
dossier d'app, ce que vérifie une pull request, ce que publie une fusion, et le format de
`catalog.json` que lisent le Hack Store et `hackpkg`. La décision d'origine est
l'[ADR 0188](https://github.com/Kazo-ca/HackerOs/blob/main/doc/fr/wasm/dev/adr/0188-hack-store-and-app-catalog.md)
(§4 à §6) ; les règles de confiance et d'installation sont dans
l'[ADR 0189](https://github.com/Kazo-ca/HackerOs/blob/main/doc/fr/wasm/dev/adr/0189-app-sources-trust-and-per-user-install.md).

## Architecture

```text
main                              relue par PR
  apps/<appId>_<version>/         un dossier par version d'app
  categories.json                 liste fermée des catégories
  tools/                          outil du catalogue (C#) et ses tests
  external/HackerOs               sous-module : bibliothèques HackerOS lues par l'outil
  .github/actions/build-app/      compile une app depuis son commit (+ CodeQL)
  .github/workflows/validate.yml  vérifie une PR
  .github/workflows/pr-comment.yml écrit le commentaire de revue
  .github/workflows/publish.yml   publie après la fusion

packages                          écrite seulement par publish.yml, sans historique de code
  catalog.json
  packages/<appId>/<version>/<appId>-<version>.hackapp
  assets/<appId>/<version>/icon.png, screenshots/N.png, description.<lang>.md
```

L'outil `tools/HackerOs.MarketPlace.Tool` lit les manifestes avec `AppManifestJsonSerializer` et
`AppManifestValidator`, et les paquets avec `AppPackage.Read` : ce sont les classes de HackerOS
elles-mêmes, donc le catalogue applique exactement les règles de l'OS à l'installation. Le lecteur
de paquets (ADR 0182) n'est pas encore dans les paquets NuGet 2.1.0 : l'outil compile les deux
bibliothèques depuis le sous-module `external/HackerOs`, sur le tag `v2.2.1` (2026-11-01 : le
lecteur de paquets de cette version accepte les assemblages satellites, ADR 0222 de HackerOS).
Quand une version publiée les contiendra, le sous-module sera remplacé par des `PackageReference`.

La CI compile une app soumise à son commit avec le sous-module `external/HackerOs` de l'app (une
app créée avec le modèle `hackeros-solution` y lit sa version de HackerOS) : l'action
`build-app` récupère ce seul sous-module, et seulement s'il pointe vers
`https://github.com/Kazo-ca/HackerOs`.

## Le dossier d'une version

Le nom est `<appId>_<version>` : l'identifiant de l'app (au moins trois segments en minuscules
séparés par des points, règle de `AppManifestValidator`) et sa version SemVer exacte. Tout fichier
qui n'est pas dans ce tableau est refusé, pour qu'aucun binaire n'entre dans `main`.

| Fichier | Obligatoire | Règle |
|---|---|---|
| `icon.png` | oui | PNG 256×256, 200 Ko au plus |
| `short_description.<lang>.txt` | `en` oui | UTF-8, une ligne non vide, 120 caractères au plus |
| `description.<lang>.md` | `en` oui | UTF-8, non vide, 20 Ko au plus |
| `manifest.json` | oui | celui **du paquet** construit par `HackerOsPack` (pas celui du projet : le paquet y ajoute les ressources statiques, ADR 0221 de HackerOS) ; lecture stricte et validation de l'OS ; `id` et `version` égaux au nom du dossier |
| `store.json` | oui | voir ci-dessous |
| `screenshots/1.png` … `5.png` | non | 1 à 5 PNG numérotés sans trou, 1 Mo et 3840 px de côté au plus |

`<lang>` est un code de langue comme `fr` ou `pt-BR`.

### `store.json`

Lu strictement : une propriété inconnue ou en double est une erreur.

| Propriété | Obligatoire | Règle |
|---|---|---|
| `repository` | oui | `https://github.com/<owner>/<repo>`, dépôt public |
| `commit` | oui | SHA complet de 40 caractères hexadécimaux en minuscules |
| `category` | oui | un `id` de `categories.json` |
| `tags` | non | 10 au plus, minuscules, chiffres et `-`, 30 caractères au plus, sans doublon |
| `license` | oui | expression SPDX (`MIT`, `GPL-3.0-only`, `MIT OR Apache-2.0`) |
| `project` | non | chemin relatif du `.csproj` à compiler (lettres, chiffres, `.`, `_`, `-`, `/`, sans `..`) |
| `kind` | non | seulement `app` pour l'instant ; les packs de thèmes (ADR 0190) viendront avec leur chantier |

### `categories.json`

Un tableau de `{ "id": "...", "labels": { "en": "...", "fr": "..." } }`. L'`id` est en minuscules
(2 à 32 caractères, chiffres et `-`), unique, et les libellés anglais et français sont obligatoires.
Une catégorie s'ajoute par une PR sur ce fichier.

## Vérification d'une pull request (`validate.yml`)

Les numéros sont ceux de l'ADR 0188 §5.

| Étape | Où | Ce qui est fait |
|---|---|---|
| — | job *Catalog tool tests* | tests de l'outil |
| — | job *Check folders* | un fichier d'un dossier déjà fusionné ne peut ni changer ni disparaître ; la PR ne peut qu'ajouter des dossiers |
| 1, 2 | job *Check folders* | `validate` sur `categories.json` et tous les dossiers |
| 3 | job *Build* | `git fetch` du commit dans le dépôt de `store.json` ; échec s'il n'existe pas |
| 4 | job *Build* puis *review* | `dotnet build -c Release -t:HackerOsPack`, puis `check-package` : le paquet doit être lu par `AppPackage.Read` et son manifeste être celui du dossier |
| 5 | — | vérification des API contre le runtime élagué : faite par `hackpkg` à l'installation (ADR 0182) ; pas en CI pour la v1 (décision de Benoit, Q1 = A ; [#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3)) |
| 6 | job *Build* | CodeQL (`csharp`, plus `javascript-typescript` si le dépôt a des fichiers JS ou TS) ; les résultats sont résumés dans le commentaire |
| 7 | *review* puis `pr-comment.yml` | commentaire : source, licence, SDK, taille et SHA-256 du paquet, capacités (les dangereuses d'abord, marquées ⚠️), fichiers JavaScript, assemblies, résultats CodeQL |

Puis un mainteneur relit le code et approuve ; rien n'est fusionné automatiquement.

### Sécurité des workflows

- Les jobs qui compilent l'app exécutent son code (MSBuild peut lancer n'importe quoi) : ils n'ont
  qu'un jeton en lecture et aucun secret. L'outil est compilé et lancé dans un autre job, qui ne
  fait que lire les artefacts.
- Une PR venant d'un fork ne peut pas écrire de commentaire : `pr-comment.yml` tourne depuis `main`
  sur l'événement `workflow_run`, lit l'artefact comme des données, et ne commente que si le numéro
  de PR désigne une PR ouverte dont la tête est le commit vérifié.
- Les valeurs venant de la PR (noms de dossiers, champs de `store.json`) passent au shell par des
  variables d'environnement, jamais par `${{ }}`, et ne sont utilisées qu'après la vérification du
  format.
- Une PR peut modifier les workflows eux-mêmes : le relecteur vérifie que la PR ne touche que
  `apps/` (ou alors c'est une PR de maintenance relue comme telle).

## Publication (`publish.yml`)

Après une fusion dans `main` qui ajoute des dossiers (ou change `categories.json`) :

1. *plan* liste les dossiers ajoutés par le push et les revérifie ;
2. *build* recompile chaque app au même commit (sans CodeQL), avec un jeton en lecture ;
3. *publish* (seul job avec `contents: write`) lance `publish` de l'outil sur une copie de la
   branche `packages` (créée orpheline la première fois), puis pousse un commit.

L'outil revérifie chaque dossier et chaque paquet ; si l'un échoue, rien n'est écrit. Il copie le
paquet et les images, réécrit `catalog.json`, puis ne garde que les **3 dernières versions** de
chaque app (les plus anciennes sont retirées de `packages/` et `assets/` ; leur dossier reste dans
l'historique de `main`). Une version plus ancienne publiée en retard ne change pas la fiche de
l'app.

Pour republier une version (par exemple après un changement de l'outil) : lancer **Publish
catalog** à la main avec les noms de dossiers séparés par des espaces.

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

- Les chemins sont relatifs à la racine de la branche `packages`.
- Les apps sont triées par `appId`, leurs versions de la plus récente à la plus ancienne.
- Les champs de la fiche (`name`, `category`, `tags`, `shortDescriptions`, `license`,
  `repository`) viennent de la version la plus récente. `capabilities` et `sdkCompatibility` sont
  par version, pour que le Store montre ce qu'une mise à jour ajoute.
- `schemaVersion` change si le format change de façon incompatible ; un lecteur refuse une version
  plus grande que la sienne.
- `signature` (facultatif) : signature ECDSA P-256 / SHA-256 des octets du `.hackapp`, au format
  IEEE P1363 (`r||s`, 64 octets) en base64 ([ADR 0214](https://github.com/Kazo-ca/HackerOs/blob/main/doc/fr/wasm/dev/adr/0214-hack-store-hot-add-signing-transactions.md)
  de HackerOs, §2). `publish.yml` signe chaque nouvelle version avec le secret
  `HACK_STORE_SIGNING_KEY` quand il existe ; sans lui, les versions sont publiées sans signature.
  Une version déjà publiée garde sa signature.

### Clé de signature (à faire par Benoit)

1. Sur ton ordinateur, dans une copie de ce dépôt :
   `dotnet run --project tools/HackerOs.MarketPlace.Tool -- keygen --private-key ~/hack-store-signing.pem`.
   L'outil écrit la clé privée dans un nouveau fichier (`0600`), ne remplace jamais un fichier
   existant, et affiche la clé publique.
2. Copier le contenu du fichier `.pem` dans le secret `HACK_STORE_SIGNING_KEY` du dépôt
   (Settings ▸ Secrets and variables ▸ Actions), puis garder le fichier hors ligne. La clé
   privée ne passe jamais par un agent ni par une PR.
3. Mettre la clé publique dans `OfficialCatalogSigningKey` (`AppPackageDefaults.cs`) ou dans la
   propriété MSBuild `HackerOsAppSigningKeys` de HackerOs (`catalog:official=<clé publique>`) :
   dès cette version de l'OS, tout paquet du catalogue officiel doit porter une signature valide.
4. Republier les versions existantes (workflow *Publish catalog* ▸ Run workflow, avec leurs
   dossiers) pour qu'elles reçoivent une signature avant que l'OS l'exige.

## L'outil du catalogue

```text
dotnet run --project tools/HackerOs.MarketPlace.Tool -- validate --root . [dossier...]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- check-package --root . --folder <dossier> --report <f.md> [--package <f.hackapp>] [--sarif <dossier>]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- publish --root . --built <dossier> --output <copie de packages> [--keep 3] [--published-at <ISO 8601>]
dotnet run --project tools/HackerOs.MarketPlace.Tool -- keygen --private-key <fichier.pem>
dotnet run --project tools/HackerOs.MarketPlace.Tool -- sign --package <f.hackapp> [--private-key <fichier.pem>]
dotnet test tools/HackerOs.MarketPlace.slnx
```

Code de sortie : 0 si tout va bien, 1 si quelque chose est refusé, 2 pour une erreur d'usage. Dans
GitHub Actions, les erreurs deviennent des annotations sur les fichiers de la PR. Cloner avec
`--recurse-submodules` (ou `git submodule update --init`) avant de compiler l'outil.

## Réglage du dépôt (à faire par Benoit)

- Protection de `main` : PR obligatoire, **revue des Code Owners** obligatoire (`.github/CODEOWNERS`
  désigne `@werddomain`), vérifications *Catalog tool tests*, *Check folders* et *Check packages and
  write the review* obligatoires.
- Actions ▸ General : demander l'approbation avant de lancer les workflows des PR de contributeurs
  extérieurs.
- Protection de `packages` : écriture réservée à GitHub Actions.

## Décisions prises dans ce chantier

- `store.json` reçoit deux champs facultatifs : `project` (le `.csproj` à compiler) et `kind`
  (`app` seulement pour l'instant). Décision de Benoit (Q2 = A).
- Limites que l'ADR ne fixait pas : description longue 20 Ko, captures 1 Mo et 3840 px de côté,
  10 étiquettes.
- `catalog.json` contient aussi les catégories et leurs libellés, pour que le Store n'ait qu'un
  fichier à lire.
- Une version fusionnée est immuable : une PR ne peut qu'ajouter des dossiers.
- La vérification des API en CI (ADR 0188 §5 étape 5) est laissée à l'installation pour la v1
  (décision de Benoit, Q1 = A) ; elle viendra avec [#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3).

## Tâches

- [x] `categories.json`, modèle de PR, `CODEOWNERS`.
- [x] Outil du catalogue : format, manifeste, paquet, commentaire de revue, publication, tests.
- [x] `validate.yml`, `pr-comment.yml`, `publish.yml` et l'action `build-app`.
- [x] README (anglais, français) et ce document.
- [ ] Vérification des API contre le runtime élagué en CI ([#3](https://github.com/Kazo-ca/HackerOs-AppMarketPlace/issues/3)).
- [ ] Remplacer le sous-module par les paquets NuGet quand une version les contient.
- [ ] Packs de thèmes (`kind: "theme-pack"`, ADR 0190).
