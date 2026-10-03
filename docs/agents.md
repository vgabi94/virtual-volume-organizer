# Using vvo from an AI agent

Paste the section below into your agent's instructions (for example `CLAUDE.md` or `AGENTS.md`),
with the path to your catalogue filled in.

---

## Virtual Volume Organizer catalogues (`vvo`)

`vvo` reads and edits `.vvo` catalogues: records of what is on offline drives and backups (names,
sizes, timestamps), grouped into virtual volumes. It never touches the files on disk except to read
them when scanning.

- Pass `--db <path>` to every command. The catalogue is `D:\Catalogues\backup.vvo`.
- stdout is always one JSON document. Read the exit code: `0` success, `2` bad arguments,
  `3` not found, `4` confirmation required, `5` catalogue busy (retry later), `6` declined or cancelled.
  On failure stdout is `{ "error": { "code", "message", ... } }`.
- Everything is named by id. List first, then act on the ids the listing returned:
  - `vvo volume list` and `vvo folder list [--volume <id>]` for what the catalogue holds.
  - `vvo ls <id>`, `vvo tree <id> [--depth N]`, `vvo stat <id>` to look inside a folder. A folder
    entry id stands for the top of its tree.
  - `vvo search <text> [--folder <entry id>] [--limit N]` to find files by name.
- Each record carries `entries[]`: its `cataloguePath` (`Volume:\Folder\sub\file`) and
  `physicalPath` (where it was on disk) for every folder entry it is listed under.
- `vvo compare <entry id> --disk <path>` shows what changed on disk since the scan, and
  `vvo compare <entry id> <other entry id>` diffs two folders. Neither writes anything.
- Run `vvo <command> --help` for the options of a command.

### Destructive commands need the user

`volume delete`, `folder delete`, `folder rescan`, `rm`, and anything that replaces an existing file
(`db new`, `db copy`, `db export`, `db import`) ask the user to type a word at a terminal. You
cannot answer for them, and there is no flag to skip it. When you run one, it changes nothing and
exits with code `4`:

```json
{ "error": { "code": "confirmation_required", "message": "...", "command": "vvo folder delete ... --db ..." } }
```

Show the user `message`, which says what would be lost, and ask them to run `command` themselves in
a terminal. Before asking for a rescan, run the `preview` command it returns (a `compare --disk`) so
you can tell them what would change.
