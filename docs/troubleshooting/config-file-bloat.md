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

A Newtonsoft serialization footgun on `[JsonIgnore]` proxy properties.

`Configuration` exposes many `[JsonIgnore]` proxy properties that delegate to
the active venue (`RoundHistory`, `ActiveVenue`, `Tips`, `PlayerStatsStore`,
`DealerName`, ...). `VenueSettings.StatsSessions` is also `[JsonIgnore]` (its
canonical store is the per-session files under `{ConfigDir}/sessions/`).

In an **older build** these properties were serialized at the config root.
Two compounding problems resulted:

1. **Orphaned keys round-trip forever.** Once a property became `[JsonIgnore]`,
   its old key on disk was unknown to the typed loader and got captured into
   the type's `[JsonExtensionData] ExtraData`, then re-written on every save.
   No migration dropped it (violating the documented "removals need a migration
   step too" rule in CLAUDE.md).

2. **Collection doubling on load.** Newtonsoft's default
   `ObjectCreationHandling.Auto` reuses an existing collection instance and
   *appends* to it. With the root `RoundHistory` proxy returning the same list
   as `Venues[ActiveVenueIndex].RoundHistory`, each load appended the venue's
   list to itself -> the list doubled every load/save cycle. ~8 cycles turned
   62 real rounds into 15,066 (243x) and 8 archived sessions into 1,464 copies
   (~145k full `GameState` round snapshots).

The real session data was never at risk - the canonical session store
(`{ConfigDir}/TwentyOne/sessions/`) is separate and was healthy throughout.

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

Two layers, because the schema migration alone proved insufficient:

- Schema **v3 migration** (`ConfigMigrations.cs`) drops the orphaned root proxy
  keys and per-venue `StatsSessions` from the raw JObject before the typed load.
- **Runtime self-heal** (`Configuration.DropOrphanedExtraData`, called from the
  plugin ctor on every load) drains the same keys from the `[JsonExtensionData]`
  `ExtraData` dictionaries *after* deserialization, so the first `Save()` never
  re-emits them.
- Current code marks the proxies `[JsonIgnore]`, so the doubling loop cannot
  recur; the two cleanups only remove orphans left by older builds.

### Why the one-shot migration was not enough

The migration is gated on `SchemaVersion < CurrentSchemaVersion`, so it runs
**exactly once** at the version transition. If that run does not persist a clean
file (observed in the field: the 2->3 transition left flat-root orphans on disk),
the keys are captured into `Configuration.ExtraData` on the next load and
`[JsonExtensionData]` re-emits them flat forever - and the migration never fires
again because the version is already current. Note that `[JsonExtensionData]`
writes its entries as **flat siblings** of the object's real properties, so a
config can show an *empty* `"ExtraData": {}` object while still carrying the
orphan keys at the root; do not trust an empty ExtraData object as proof of a
clean file. The runtime self-heal closes this gap: it is idempotent and
version-independent, so it corrects any config that still carries the orphans.

### Rules to avoid reintroducing this

- When a property becomes `[JsonIgnore]` or is removed, **add a migration step
  that drops its old JSON key** (CLAUDE.md "Removals need a migration step too").
- Be wary of a `[JsonIgnore]` proxy that returns a *mutable collection* shared
  with another serialized property. If it ever loses `[JsonIgnore]`,
  `ObjectCreationHandling.Auto` will double that collection on every load.
