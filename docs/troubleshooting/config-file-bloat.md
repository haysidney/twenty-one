# Config file bloat: plugin fails to load

## Symptom

The plugin silently fails to load (no MainWindow, `/twentyone` does nothing).
The config file is huge - hundreds of MB to multiple GB:

```bash
ls -la ~/.xlcore/pluginConfigs/TwentyOne.json
# e.g. 1022594253 bytes (~1.0 GB) where a healthy config is ~1-3 MB
```

Dalamud loads the config via `File.ReadAllText` + Newtonsoft `JObject.Parse`.
A multi-hundred-MB file either OOMs or takes long enough that init never
completes, so the plugin never registers its windows.

## Root cause

**A bundled second copy of `Newtonsoft.Json.dll`.** This is the classic Dalamud
plugin gotcha, and it is the source of the whole mess - everything below is a
downstream symptom.

`TwentyOne.Game.csproj` referenced `Newtonsoft.Json` as an ordinary
`PackageReference`, so the build copied `Newtonsoft.Json.dll` into the plugin
output. Dalamud already provides Newtonsoft at runtime. With two copies loaded,
the plugin's types are compiled against the *bundled* Newtonsoft, but Dalamud
serializes the config with *its own* Newtonsoft - so the attribute **types do
not match**. Dalamud's serializer looks for *its* `JsonIgnoreAttribute` /
`JsonExtensionDataAttribute`; the plugin's are a different `System.Type`, so they
are silently ignored. With the attributes invisible to the serializer:

1. **`[JsonIgnore]` is ignored -> the proxies serialize.** `Configuration`
   exposes `[JsonIgnore]` proxy properties that delegate to the active venue
   (`RoundHistory`, `ActiveVenue`, `Tips`, `DealerName`, ...). Ignored, they get
   written as flat top-level keys = the "orphans".
2. **`[JsonIgnore]` is ignored on read too -> doubling.** On load the flat-root
   `RoundHistory` / `ActiveVenue` bind back through the proxy setters, and
   `ObjectCreationHandling.Auto` *appends* to the same `Venues[Active].RoundHistory`
   list. Active venue + root `RoundHistory` + root `ActiveVenue.RoundHistory` =
   the list tripled every load. A handful of reloads turned 94 rounds into
   2,538 (and a 1 GB file the first time around).
3. **`[JsonExtensionData]` is ignored -> nothing captures.** The forward-compat
   bags stayed empty (the diagnostic confirmed flat-root orphans with an empty
   `ExtraData`), so any `ExtraData`-based cleanup was aimed at thin air.

The fix: don't bundle Newtonsoft. `TwentyOne.Game`'s reference now carries
`<ExcludeAssets>runtime</ExcludeAssets>` (compile-only); the test project, which
has no Dalamud to supply it, references Newtonsoft directly. With a single
Newtonsoft at runtime the attributes match, `[JsonIgnore]` takes effect, the
proxies stop serializing, and the load-time cleanup self-heals the lingering
orphans.

The real session data was never at risk - the canonical session store
(`{ConfigDir}/TwentyOne/sessions/`) is separate and was healthy throughout.

### Why this was so hard to pin down

Unit tests and standalone repros always passed because a test project has only
**one** Newtonsoft, so the attributes match there. The bug only manifests inside
Dalamud's two-Newtonsoft runtime. If `[JsonIgnore]` "works in tests but not in
game", suspect a duplicated framework assembly before anything else. A load-time
diagnostic that dumps the raw-vs-typed config shape to a file (we used a throwaway
`WriteLoadDiagnostic`) is the fastest way to see the truth from outside the game.

## Diagnosis

Confirm the bloat source without loading the whole file into an editor:

```bash
# What types dominate the file?
python3 - <<'EOF'
import re, collections
c=collections.Counter()
with open("/home/USER/.xlcore/pluginConfigs/TwentyOne.json",errors='replace') as f:
    for chunk in iter(lambda:f.read(1<<20),''):
        for m in re.findall(r'"\$type": "([^"]+)"',chunk): c[m]+=1
for k,v in c.most_common(10): print(v,k)
EOF
```

Tell-tale signs:
- Huge counts of `RoundHistoryEntry` / `BankTransactionEntry` / `PlayerStatsSession`.
- `StatsSessions` present at venue level (should be gone - it lives in files).
- Root-level keys that are `[JsonIgnore]` proxies (`ActiveVenue`, `RoundHistory`,
  `Tips`, ...) present in the JSON.
- A venue `RoundHistory` whose entries repeat (distinct `RoundNumber` count far
  below the array length).

## Recovery

1. **Back up the bloated file first** (never delete it until recovery is verified):

   ```bash
   cp -n ~/.xlcore/pluginConfigs/TwentyOne.json \
         ~/.xlcore/pluginConfigs/TwentyOne.json.bloated-backup-$(date +%Y%m%d)
   ```

2. **Strip the duplicated data.** The recovery script (~14s, needs a few GB RAM
   for a 1 GB file):
   - removes every orphaned `[JsonIgnore]`-proxy key from the config root
     (canonical copies live in `Venues[ActiveVenueIndex]`),
   - removes `StatsSessions` from each venue (sessions live in their own files),
   - de-duplicates each venue's `RoundHistory` by detecting the smallest exact
     repeat period (`list == list[:P]` repeated) and keeping `list[:P]`.

   See the "Strip ExtraData.StatsSessions" / period-detection approach used in
   the Jun 2026 incident; the principle is: only drop data that is provably a
   duplicate (exact repetition or an orphaned proxy key), never legitimate play
   history.

3. **Simplest fallback:** if recent history is expendable, restore the most
   recent small `*.bak-schema-*` backup over `TwentyOne.json`. The plugin
   re-runs migrations on next load.

## Prevention

**Primary: never bundle an assembly Dalamud already provides.** Newtonsoft.Json
(and ImGui, etc.) come from Dalamud at runtime. Any non-Dalamud project in the
solution that needs them for compilation must reference them compile-only:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

Sanity check after building: `ls TwentyOne/bin/Debug/Newtonsoft.Json.dll` must
be **absent**. If it is present, the attribute-identity bug is back.

**Secondary (defense in depth):** two cheap cleanups run on every load and keep
a stray regression from snowballing:

- `ExtensionDataCleaner.ClearAll` clears all `[JsonExtensionData]` dictionaries
  when the on-disk `SchemaVersion <= CurrentSchemaVersion` (every captured key is
  then provably an orphan; a *newer*-version config keeps its `ExtraData` for
  downgrade safety). Safe by construction - those dicts only hold unknown keys -
  and wrapped in try/catch.
- `ConfigMigrations.DedupRoundHistory` collapses duplicate `RoundNumber`s per
  venue (a live venue's rounds are unique, so repeats are corruption).

These are belt-and-suspenders, not the fix. With a single Newtonsoft at runtime
`[JsonIgnore]` works and the orphans/doubling never arise in the first place.

### Three gotchas worth remembering

- **"Works in tests, not in game" == suspect a duplicated framework assembly.**
  Attributes/reflection behave differently when two copies of a library are
  loaded. Standalone repros can't reproduce it.

- The schema migration is gated on `SchemaVersion < CurrentSchemaVersion`, so it
  runs **once** at a version transition. Never rely on it to remove something that
  must *stay* gone - if it fails to persist, it never fires again. Cleanup that
  must happen every load belongs in the load-time `ExtensionDataCleaner` path, not
  a migration step.
- `[JsonExtensionData]` writes its entries as **flat siblings** of the object's
  real properties, so a config can show an *empty* `"ExtraData": {}` object while
  still carrying orphan keys at the root. Do not trust an empty ExtraData object
  as proof of a clean file; inspect the actual top-level keys.
