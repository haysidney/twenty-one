# Troubleshooting

Operational notes for diagnosing and recovering from plugin failures in the
field. Each entry documents a real incident: the symptom, the root cause, the
recovery steps taken, and the code change that prevents recurrence.

Design rationale lives elsewhere in `docs/`; this folder is specifically for
"the plugin is broken, what now" runbooks.

## Index

- [config-file-bloat.md](config-file-bloat.md) - plugin fails to load because
  `TwentyOne.json` has grown to hundreds of MB / GB. Caused by a serializer
  doubling loop on orphaned `[JsonIgnore]`-proxy keys.
