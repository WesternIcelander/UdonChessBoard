# Compatibility with existing worlds

Once the package is released (step 16), worlds will already be using it. A world creator updating to a newer version should only have to import the new package: no re-wiring, no re-placing, no lost customizations. Where that can't be done (a new feature that only the world creator can decide on), the change must be as small as possible and say exactly what to do.

These rules apply to every change made after the first release (version 1, step 16.1). Before then, nothing outside this project depends on the prefabs, and the builders could still rebuild them from scratch until step 14.12 deleted them. Since then the prefabs are the source, changed in place in Unity.

## What a world holds

A world that uses the package has:

- **Prefab instances** of our prefabs (step 14), with the world creator's **overrides**: changed field values, moved or scaled objects, a replaced board model, captured-piece places moved, their own piece sets assigned. Unity stores each override against the overridden object's internal ID (its file ID inside the prefab) and the field's serialized name.
- **References between instances** in the scene (a board to its arbiter, a view to its board, …), set by the wiring script (14.11) or by hand.
- **Its own scripts** calling our public API: `SetPieceSet`, `ChessBoardListener` subclasses, the board's input methods and getters (step 15's API documents), and perhaps `SendCustomEvent` with our method names.
- **Asset references** to our scripts, UdonSharp program assets, meshes and materials, by the GUID in each asset's `.meta` file.
- Possibly **unpacked** prefabs, or objects built without prefabs: no prefab link, so prefab changes don't reach them.

Anything that changes one of these identities, IDs, names or signatures without carrying the world's data across breaks the world.

## Rules

1. **Prefabs are edited in place, never rebuilt.** An object recreated in a prefab gets a new file ID, so every override on the old object is lost. A change to a prefab, in Unity's prefab editor or by an editor script, keeps every existing object (changing it where it is), adds only what is new, and saves. Removing an object deletes what worlds overrode on it: only when it's no longer used, and say so in the changelog. The builders that rebuilt the prefabs and views from scratch in steps 9 to 14 were deleted in 14.12, before the first release.
   - **A prefab's root transform never reaches placed instances.** Unity saves an instance root's position, rotation and, for a RectTransform, its size, anchors and pivot on every instance (default overrides), so changing them in the prefab changes no placed copy. A world-space canvas can't grow: new UI fits in the rect the canvas already has, and `VRCUiShape` takes the pointer only within it (found in 14.13, when the settings menu's taller canvas left its new rows unclickable in the test scene; **Test Prefabs** now checks the menu's controls fit its canvas, on the scene's instance too).
2. **Saved fields are never renamed, retyped or removed in a way that loses a world's value.** A world's overrides are saved on its instances under each field's serialized name, so a prefab update can't change them: a renamed field reads as empty in every world that set it (e.g. 12.10's `pieceSet` becoming `whitePieceSet` and `blackPieceSet`). Instead:
   - a plain rename uses `[FormerlySerializedAs("oldName")]` on the new field, **never removed**. Checked in Unity (during the since-dropped migrator step): Unity carries the value over onto the UdonSharp proxy for a prefab, a scene object and a prefab instance's override alike, and UdonSharp copies the proxy to the Udon data before Play Mode and every build. An instance's override stays saved under the old name, applied only through the attribute, so it must stay for good;
   - anything more (a field split, retyped or whose meaning changed) keeps the old field, hidden from the Inspector (`[HideInInspector]`), and the code falls back to it at run time while the new field is empty (e.g. `if (whitePieceSet == null) whitePieceSet = pieceSet;`), which works on instances, unpacked copies and hand-built objects alike; or, better, the change is avoided by adding a new field and leaving the old one as it is.
   A field whose meaning changes is a new field: give it a new name.
3. **Asset GUIDs stay the same.** Scripts, UdonSharp program assets (`.asset`), prefabs, meshes, materials and sprites are referenced by GUID. Ship every `.meta` file in the package, move and rename assets only inside Unity (or move the `.meta` with the file), and never delete and recreate an asset that worlds can reference (generated placeholders included: update the existing asset).
4. **New parts need defaults the world doesn't have to supply.** A new required reference or object comes with the prefab (as the arbiter carries its default piece set), is found at run time, or is filled in by the wiring script. A new field's default keeps today's behaviour, unless the change is meant to reach every world (then rule 7).
5. **No editor migrator: the prefabs carry the changes.** An updated prefab reaches every instance in everything it doesn't override: new objects and components, new fields with their defaults, changed values. What it can't reach (overrides saved under an old field name, unpacked or hand-built copies) is handled by rule 2 and by run-time fallbacks, and new references by the wiring script (14.11). So there's no saved data version and no editor script upgrading old data; one was planned and dropped as not needed.
6. **The public API keeps working.** Public methods and fields that world scripts may use (everything step 15 documents, and `ChessBoardListener`'s virtual methods) keep their names and parameters. A changed signature gets a new method, with the old one kept, forwarding to it, and marked deprecated in its comment and in the changelog. Public method names also stay because `SendCustomEvent` calls them by name. Public methods starting with `_` (step 13.7) aren't public API and aren't covered: they may be renamed, changed or removed in any release, as may public fields not shown in the Inspector. Return codes and constants keep their values; new ones get new values.
7. **Changes that do reach every world are deliberate.** A changed default in a prefab reaches every instance that didn't override it, and a changed behaviour reaches every world. Both are allowed when they're improvements every world wants (better pieces, a bug fix), but never by accident: list them in the changelog.
8. **Every release has a changelog entry.** `CHANGELOG.md` in the package: what changed, what reaches an existing world, and anything the world creator must do by hand (ideally nothing), with the version number.
9. **Every release is tested against the previous one.** A test scene saved with the previous release (instances with typical overrides) is opened under the new version: it must open and wire with no error, nothing it overrode may be lost, and every Play Mode test must pass on it (step 16.1).

## Not a concern

- **Networking between versions.** Every client in an instance runs the same world build, so synced fields can change freely between versions.
- **Worlds that unpacked or copied our objects** don't get prefab changes. Run-time fallbacks (rule 2) and the wiring script still cover them; what they can't add, the changelog says.
