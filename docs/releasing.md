# Releasing

How a version gets from the working tree to a dealer's plugin installer. There
is no CI - every step here is manual.

Two repos are involved:

- **`haysidney/twenty-one`** (this one) - source, git tags, and the release
  assets (`latest.zip`).
- **`haysidney/DalamudPlugins`** - `repo.json`, the custom-repo manifest users
  add to Dalamud. Lists several plugins; moved out of this repo in `c365f80`.

## The one ordering rule

**Cut the release before touching the manifest.** `repo.json` points at
`releases/download/vX.Y.Z/latest.zip`. Publish the manifest first and every
install and update 404s until the release exists. Release first, verify the URL
resolves, then bump the manifest - there is then no window where installers
point at a missing asset.

## Steps

### 1. Bump the version

`<Version>` in `TwentyOne/TwentyOne.csproj`, in the same commit that earns it
(see AGENTS.md > Versioning). 4-part `0.MINOR.PATCH.0`; the 4th is always 0.

### 2. Gate on a clean build and tests

```bash
nix develop --command dotnet build TwentyOne/TwentyOne.csproj -c Debug
nix develop --command dotnet build TwentyOne/TwentyOne.csproj -c Release
nix develop --command dotnet test TwentyOne.Tests/TwentyOne.Tests.csproj
```

### 3. Verify the artifact

The Release build writes `TwentyOne/bin/Release/TwentyOne/latest.zip`.

```bash
cat TwentyOne/bin/Release/TwentyOne/TwentyOne.json   # AssemblyVersion matches?
unzip -l TwentyOne/bin/Release/TwentyOne/latest.zip
```

`Newtonsoft.Json.dll` must **not** be in the zip. If it is, stop - that is the
config-bloat bug (AGENTS.md > Build, `docs/troubleshooting/config-file-bloat.md`).

### 4. Push, tag, push the tag

```bash
git push origin main
git tag -a v0.8.0 -m "v0.8.0

<one-paragraph summary>"
git push origin v0.8.0
```

Annotated only, `vMAJOR.MINOR.PATCH`, one per build actually run live or shared.

### 5. Cut the GitHub release

```bash
gh release create v0.8.0 TwentyOne/bin/Release/TwentyOne/latest.zip \
  --repo haysidney/twenty-one --title "v0.8.0" --notes "..."
```

The asset must be named `latest.zip` - the manifest URL depends on it.

Notes are dealer-facing, not a changelog. Lead with what changed on screen.
**Call out any behavioral default that changes for existing configs** - e.g.
0.8.0's `LossCoverage` defaults to the venue covering losses, so a losing night
settles differently than on 0.7.0. Those land silently otherwise: additive
config fields need no migration, so nobody is prompted.

### 6. Verify the download URL before going further

```bash
curl -sIL -o /dev/null -w "%{http_code}\n" \
  https://github.com/haysidney/twenty-one/releases/download/v0.8.0/latest.zip
```

Must print `200`.

### 7. Bump the manifest

No local clone needed - patch it through the API. **Only the version and the
three URLs change.** Do not regenerate `repo.json` from the build output: the
manifest's `Description` is hand-maintained and longer than the csproj one, and
regenerating would clobber it (see Known drift).

```bash
gh api repos/haysidney/DalamudPlugins/contents/repo.json --jq '.content' \
  | base64 -d > repo.json

python3 - <<'EOF'
import json
d = json.load(open('repo.json'))
e = next(x for x in d if x['InternalName'] == 'TwentyOne')
e['AssemblyVersion'] = '0.8.0.0'
for k in ('DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting'):
    e[k] = e[k].replace('/v0.7.0/', '/v0.8.0/')
json.dump(d, open('repo.json', 'w'), indent=2)
open('repo.json', 'a').write('\n')
EOF

SHA=$(gh api repos/haysidney/DalamudPlugins/contents/repo.json --jq '.sha')
gh api repos/haysidney/DalamudPlugins/contents/repo.json -X PUT \
  -f message="chore: bump Twenty One to 0.8.0" \
  -f content="$(base64 -w0 repo.json)" -f sha="$SHA"
```

Diff it against the live copy before the PUT - the change should be exactly four
lines.

### 8. Verify

```bash
gh api repos/haysidney/DalamudPlugins/contents/repo.json --jq '.content' | base64 -d
```

`AssemblyVersion`, the three URLs, and an intact `Description`.

## Known drift

`TwentyOne.csproj` generates a short description ("Facilitates Blackjack games
via Chat.") while `repo.json` carries a longer hand-written one that users see
in the installer list. They diverge further each release. Syncing the csproj to
match would remove the trap in step 7.

## If it goes wrong

- **Manifest points at a missing asset.** Upload `latest.zip` to the existing
  release (`gh release upload v0.8.0 ...`) rather than rolling the manifest
  back - faster, and no user sees a version go backwards.
- **Bad build shipped.** Bump to a patch version and release again. Do not
  delete or re-point a published tag; installers cache by version.
