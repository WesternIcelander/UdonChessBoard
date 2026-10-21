# Compatibility with existing worlds

Once the package is released (step 15), worlds will already be using it. A world creator updating to a newer version should only have to import the new package: no re-wiring, no re-placing, no lost customizations. Where that can't be done (a new feature that only the world creator can decide on), the change must be as small as possible and say exactly what to do.

These rules apply to every change made after the first release (version 1, step 15.1). Before then, nothing outside this project depends on the prefabs, so the builders may still rebuild them from scratch until they're deleted (step 13.12). From then on the prefabs are the source, changed in place in Unity.

## What a world holds

A world that uses the package has:

- **Prefab instances** of our prefabs (step 13), with the world creator's **overrides**: changed field values, moved or scaled objects, a replaced board model, captured-piece places moved, their own piece sets assigned. Unity stores each override against the overridden object's internal ID (its file ID inside the prefab) and the field's serialized name.
- **References between instances** in the scene (a board to its arbiter, a view to its board, …), set by the wiring script (13.10) or by hand.
- **Its own scripts** calling our public API: `SetChessPieces`, `ChessBoardListener` subclasses, the board's input methods and getters (step 14's API documents), and perhaps `SendCustomEvent` with our method names.
- **Asset references** to our scripts, UdonSharp program assets, meshes and materials, by the GUID in each asset's `.meta` file.
- Possibly **unpacked** prefabs, or objects built without prefabs: no prefab link, so prefab changes don't reach them.

Anything that changes one of these identities, IDs, names or signatures without carrying the world's data across breaks the world.

## Rules

1. **Prefabs are edited in place, never rebuilt.** An object recreated in a prefab gets a new file ID, so every override on the old object is lost. A change to a prefab, in Unity's prefab editor or by an editor script, keeps every existing object (changing it where it is), adds only what is new, and saves. Removing an object deletes what worlds overrode on it: only when it's no longer used, and say so in the changelog. The builders that rebuilt the prefabs and views from scratch in steps 9 to 13 are deleted in 13.12, before the first release.
2. **Saved fields are never renamed, retyped or removed without migrating them.** A renamed field reads as empty in every world that set it (e.g. 12.10's `pieceSet` becoming `whitePieceSet` and `blackPieceSet`). Either:
   - keep the old field (hidden from the Inspector, `[HideInInspector]`) and have the migrator (rule 5) move its value to the new field and clear it; or
   - use `[FormerlySerializedAs("oldName")]`, but only once it's been checked in Unity that UdonSharp carries the value over on its behaviours (proxy and Udon data both).
   A field whose meaning changes is a new field: give it a new name and migrate.
3. **Asset GUIDs stay the same.** Scripts, UdonSharp program assets (`.asset`), prefabs, meshes, materials and sprites are referenced by GUID. Ship every `.meta` file in the package, move and rename assets only inside Unity (or move the `.meta` with the file), and never delete and recreate an asset that worlds can reference (generated placeholders included: update the existing asset).
4. **New parts need defaults the world doesn't have to supply.** A new required reference or object comes with the prefab (as the arbiter carries its default piece sets), is found at run time, or is filled in by the wiring script or the migrator. A new field's default keeps today's behaviour, unless the change is meant to reach every world (then rule 7).
5. **An automatic migrator upgrades old data.** Every component of ours whose saved data has ever changed carries a saved version number (`dataVersion`, an `int` saved at the current version in the prefabs and raised by the migrator; its default in code is 0, since a component saved before the field existed loads with that default). An editor script, run when a scene is opened, when it's saved and before every build (with the build check), finds our components in the loaded scenes, prefab instances and unpacked copies alike, and brings each one from its version to the current one, step by step: moving renamed fields, adding missing children, filling new references, then re-copying the UdonSharp proxy to its Udon data (`UdonSharpEditorUtility.CopyProxyToUdon`) and marking the scene dirty. It logs what it changed. A component it can't upgrade blocks the build, with a dialog saying what to do, as the build check does for empty fields. The prefabs themselves are upgraded in place (rule 1), with their `dataVersion` raised, so instances get the change through the prefab and the migrator only fills in what overrides hide.
6. **The public API keeps working.** Public methods and fields that world scripts may use (everything step 14 documents, and `ChessBoardListener`'s virtual methods) keep their names and parameters. A changed signature gets a new method, with the old one kept, forwarding to it, and marked deprecated in its comment and in the changelog. Public method names also stay because `SendCustomEvent` calls them by name. Return codes and constants keep their values; new ones get new values.
7. **Changes that do reach every world are deliberate.** A changed default in a prefab reaches every instance that didn't override it, and a changed behaviour reaches every world. Both are allowed when they're improvements every world wants (better pieces, a bug fix), but never by accident: list them in the changelog.
8. **Every release has a changelog entry.** `CHANGELOG.md` in the package: what changed, what the migrator does to an existing world, and anything the world creator must do by hand (ideally nothing), with the version number.
9. **Every release is tested against the previous one.** A test scene saved with the previous release (instances with typical overrides) is opened under the new version: the migrator must upgrade it with no error, nothing it overrode may be lost, and every Play Mode test must pass on it (step 15.1).

## Not a concern

- **Networking between versions.** Every client in an instance runs the same world build, so synced fields can change freely between versions.
- **Worlds that unpacked or copied our objects** don't get prefab changes, but the migrator still upgrades their components (rule 5). What it can't add, the changelog says.
