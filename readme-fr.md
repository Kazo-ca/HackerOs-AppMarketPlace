> [English version](README.md)

# HackerOs-AppMarketPlace

Le catalogue du **Hack Store**, la boutique d'apps de [HackerOS](https://github.com/Kazo-ca/HackerOs)
([ADR 0188](https://github.com/Kazo-ca/HackerOs/blob/main/doc/fr/wasm/dev/adr/0188-hack-store-and-app-catalog.md)).

- La branche **`main`** contient un dossier par version d'app, relu par pull request.
- La branche **`packages`** est écrite par notre action après chaque fusion : `catalog.json`, les
  paquets `.hackapp` et les images, lus par le Hack Store et `hackpkg` à travers
  `raw.githubusercontent.com`.

Un auteur ne fournit **jamais de binaire** : notre CI compile l'app elle-même depuis le commit
indiqué, et c'est ce paquet qui est publié. Ce qui arrive chez l'utilisateur est donc exactement le
code relu.

## Proposer une app

1. Rendez le dépôt de l'app public et vérifiez qu'il se compile avec les paquets NuGet de HackerOS :
   `dotnet build -c Release -t:HackerOsPack` (modèle de dépôt :
   [HackerOs-AppTemplate](https://github.com/Kazo-ca/HackerOs-AppTemplate)).
2. Faites un fork de ce dépôt et ajoutez un dossier `apps/<appId>_<version>/` :

   ```text
   apps/org.example.myapp_1.0.0/
     icon.png                   PNG 256×256, 200 Ko au plus
     short_description.en.txt   une ligne, 120 caractères au plus (anglais obligatoire)
     short_description.fr.txt   facultatif, une langue par fichier
     description.en.md          description longue (anglais obligatoire), 20 Ko au plus
     description.fr.md          facultatif
     manifest.json              le manifest.json généré par votre build, sans modification
     store.json                 voir ci-dessous
     screenshots/1.png … 5.png  facultatif, 1 Mo et 3840 px de côté au plus chacune
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

   `commit` est le SHA complet (40 caractères) ; `category` vient de
   [`categories.json`](categories.json) ; `project` est facultatif (sans lui, la racine du dépôt
   doit contenir un seul projet ou une seule solution).
3. Ouvrez une pull request vers `main` et remplissez le modèle.
4. L'action **Validate app submissions** vérifie le format, compile l'app au commit indiqué,
   compare le manifeste produit avec le vôtre, lance CodeQL, puis écrit un commentaire avec les
   capacités demandées (les dangereuses en évidence) et les fichiers JavaScript embarqués.
5. Un mainteneur relit le code de l'app à ce commit et approuve. Rien n'est fusionné
   automatiquement.
6. Après la fusion, l'action **Publish catalog** recompile l'app au même commit et met à jour la
   branche `packages`.

Une version publiée ne change plus : une correction est une nouvelle version, donc un nouveau
dossier. Les **3 dernières versions** de chaque app restent téléchargeables.

## Documentation

- [Format du catalogue, vérifications et publication](doc/fr/catalog-format.md) : toutes les
  règles, le format de `catalog.json`, l'outil du catalogue et le réglage du dépôt.

## Licence

[MIT](LICENSE) pour ce dépôt ; chaque app garde sa propre licence, indiquée dans son `store.json`.
