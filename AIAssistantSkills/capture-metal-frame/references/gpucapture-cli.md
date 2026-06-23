# `gpucapture` CLI reference

Verified 2026-06-23 on macOS 27.0 (build 26A5353q), device `Mac15,6` (id 0), binary `/usr/bin/gpucapture`.

`gpucapture` performs GPU trace captures by attaching to a running process.

```
gpucapture <command> [<args>]
gpucapture <command> -h
```

## Commands

| Command      | Purpose |
|--------------|---------|
| `list`       | List all capturable processes. |
| `boundaries` | List all capturable boundary objects for a given PID (`-p`). |
| `start`      | Begin a capture of a target process → writes a `.gputrace`. |
| `stop`       | End an active capture in a process. |

Top-level options: `-h/--help`, `--version`, `--list-devices`.

## `gpucapture start`

```
gpucapture start [options]
```

| Flag | Long | Meaning | Default |
|------|------|---------|---------|
| `-p` | `--pid` | PID of the process to capture | `0` |
| `-l` | `--label` | label of the boundary object | |
| `-b` | `--boundary` | ID of the boundary object | `0` |
| `-c` | `--count` | number of boundary-object complete iterations | `1` |
| `-o` | `--output` | output path for the `.gputrace` on the host filesystem | |
| `-d` | `--device` | device ID of the target process | |
| `-u` | `--until-exit` | keep capturing until stopped or the process exits | |
| `-du` | `--disable-unused-recording` | disable unused-resource recording | |
| `-db` | `--disable-backtrace-recording` | disable backtrace recording | |

## `gpucapture stop`

| Flag | Long | Meaning |
|------|------|---------|
| `-p` | `--pid` | PID to stop a capture for (default `0`) |
| `-d` | `--device` | device ID for the target PID |
| `-a` | `--all` | stop all active captures |

## `gpucapture boundaries`

`gpucapture boundaries -p <PID>` lists the boundary objects available to scope a capture for that PID.

## v1 capture sequence (for the skill)

1. Launch the macOS Standalone Player with env `MTL_CAPTURE_ENABLED=1`
   (add `MTLCAPTURE_WAIT_FOR_SIGNAL=1` for short-lived processes), wait ~10s for first frames.
2. `gpucapture list` → resolve the player PID (or use the PID from the launch).
3. `gpucapture boundaries -p <PID>` → discover boundary labels/ids.
4. `gpucapture start -p <PID> -o <path>/frame.gputrace [-l <label> | -b <id>] [-c 1]` → capture.
5. Trace is written to the `-o` path.

### Needs runtime confirmation (do during implementation)
- Semantics of boundary `0` / `--count 1` for a single Unity frame when no named boundary exists.
- Whether `--until-exit` + `stop` is more reliable than a boundary-scoped capture for Unity players.
- Exact PID/labels Unity's Metal layer exposes (does Unity register named capture scopes / boundaries?).
