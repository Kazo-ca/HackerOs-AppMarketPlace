<!-- One PR per app version. Only add files under apps/<appId>_<version>/. -->

## App

- **App id and version:** `org.example.myapp` `1.0.0`
- **Repository and commit:** https://github.com/owner/repo/tree/<full commit SHA>
- **What it does:**

## New capabilities or changes since the previous version

<!-- List the capabilities this version adds and why the app needs them. Write "None" for a first version without capabilities. -->

## Checklist

- [ ] The folder is named `<appId>_<version>` and matches `manifest.json`.
- [ ] `manifest.json` is the file my build generated at that commit, unchanged.
- [ ] `icon.png` is a 256×256 PNG of 200 KB at most.
- [ ] `short_description.en.txt` (120 characters at most) and `description.en.md` are present.
- [ ] `store.json` names the full commit SHA, a category from `categories.json`, and the license of the repository.
- [ ] The repository is public, and `dotnet build -c Release -t:HackerOsPack` works at that commit with the HackerOS NuGet packages.
- [ ] No binary in this PR: our CI builds the package.
