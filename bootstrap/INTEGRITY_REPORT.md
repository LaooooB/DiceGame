# Town / region progression — source integrity blocker

## Status

**Gameplay implementation is blocked. No town, region, settlement, unlock or save-system integration has been implemented by these diagnostic commits. `main` has not been changed.**

Repository examined: `LaooooB/DiceGame`.
Source commit: `933627aa0c0a893a5404ee04517e029fb1e029c6`.
Diagnostic commit: `4cf70f3e1c2c34d052f1520d37ae8d6c5fa750aa`.
Executed workflow: https://github.com/LaooooB/DiceGame/actions/runs/34929402440
Job ID: `104254264421`.

The inspection job finished successfully as a diagnostic procedure; this does **not** mean that any project archive passed integrity checks. It only reads compressed bytes and tar member metadata; it does not execute recovered project code, install game dependencies, write recovered files, or replace the existing branch contents.

## Observed results

`bootstrap/READY` declares 14 chunks and 218032 base64 bytes, with base64 SHA-256 `adadb79d7ed195fa6f642c9afe904f1fd8ecafa7787c3adf728618bd46b3395e` and archive SHA-256 `841aee729a779e4d90d18ecff6f8848002595eec67a942da21038cbb1735b94f`. It does not identify which candidate directory the manifest belongs to; therefore the table below reports each candidate independently.

| Candidate | Files | Encoded bytes | Actual diagnostic result |
| --- | ---: | ---: | --- |
| `bootstrap/slimchunks` | 9 | 177886 | Strict base64 decoding fails with `Incorrect padding`. Decoding only the aligned prefix for diagnosis reaches XZ `Corrupt input data` around compressed input offset 43264. No end-of-stream marker is reached. |
| `bootstrap/xzchunks` | 2 | 32000 | Base64 decoding succeeds, but XZ reports `Corrupt input data` around compressed input offset 18176. No end-of-stream marker is reached. |
| `bootstrap/chunks` | 1 | 16000 | The gzip decoder consumes the available prefix without reporting corruption but never reaches end-of-stream. The tar content is truncated. |

Offsets are the beginning of the 256-byte input block being processed when the decoder fails, not the exact location of the original corruption.

The longest decoded prefix lists legacy presentation reference files, the JavaScript reference implementation, and `Tests/DiceGame.Tests.csproj`. It stops inside `Tests/Program.cs`. These diagnostic prefixes do not establish that their contents are authoritative or checksum-verified. None provides a complete current Godot project with `project.godot`, `DiceGame.csproj`, `Scenes/Main.tscn` and the referenced production scripts/assets.

The previous materialization run also failed at archive expansion:
https://github.com/LaooooB/DiceGame/actions/runs/34928461014
Its `Commit complete project` step was skipped.

## Required source repair

Commit the actual, working Godot project files to `main`, including scripts, scenes, configuration and required assets. Alternatively, replace the transport payload with a complete, checksum-verified archive generated from that same working project. Do not change the manifest counts or bypass decompression checks merely to make CI green. A partial reference implementation is not a safe substitute for the existing game.

## Requested implementation boundary (not implemented here)

Town preparation -> choose dice and region -> in-run growth -> victory or defeat -> return to town for construction and unlocks -> choose the next run.

Unlock dice and mechanics, not characters. Preserve existing combat behavior, the maximum of eight board slots, a deck containing at most six distinct dice types, same-type/same-pip merge restrictions, and hold-to-aim/release-to-fire controls. Keep unlock and growth content data-driven so the game designer can supply it. Integrate the loop with the actual combat, native Control UI, settlement, region records and persistent save state only after the real source has been recovered.
