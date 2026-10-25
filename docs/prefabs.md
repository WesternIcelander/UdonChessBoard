# Prefabs

World creators add the chess objects by dragging prefabs from `Assets/Siggi/ChessBoard/Prefabs/` into their scene (step 14). Each prefab is self-contained: its meshes, materials and Udon programs are saved as assets, so it works in a fresh scene, and it holds nothing for tests or debugging. Placed instances get their network IDs from the VRChat SDK as other scene objects do.

A prefab can't reference objects in the scene, so the links between the chess objects (a board to its arbiter and archive, a view to its board, …) are set after placing them: by hand in the Inspector, until step 14.11 fills them in automatically. Each section below says what to set.

Until step 14.13 the **Tools > Chess Board** builders make each prefab when it's missing and otherwise update it in place, keeping its objects ([compatibility](compatibility.md), rule 1). From then on the prefabs are the source, changed in place in Unity.

Every chess prefab shares one `I18n`, the **Chess Language** prefab (step 14.10), placed once per scene as a sibling or on a parent of the chess objects, so the `I18n` sanity check links them to it ([architecture](architecture.md), languages).

**The minimum a world needs:** one **Chess Arbiter** and a **Chess Board** per board, wired to it. The view, UI and archive prefabs (steps 14.3 to 14.5) are added here as they're made.

## Chess Pieces

`Prefabs/Chess Pieces.prefab` (step 14.1): the default chess piece set, a `ChessPieceSet` with a slot per piece type for each colour ([customizing](customizing.md#chess-piece-sets-steps-123-1210-and-136)). The White row holds the placeholder meshes from `Models/` with the White Pieces material; behind it are White's promotion pieces, with the see-through Promotion Ghost material; behind those is the Black row, whose **Model**s are renderers with the Black Pieces material and no mesh, so Black wears White's models turned to face White. Each slot holds a **Guide** and a **Model**, and the set's fields point at its own **Model**s.

It's nested in the Chess Arbiter prefab, so a world doesn't need to place it for the default set. It's also where a world's own set starts:

1. Drag **Chess Pieces** into the scene.
2. Right-click it and choose **Prefab > Unpack Completely**. Don't make a prefab variant instead: whatever a variant doesn't override follows the package's set, so a package update could change your pieces (a Black slot you left to the fallback would take a mesh the package later gives its own default).
3. Rename it, change its templates as [customizing](customizing.md#chess-piece-sets-steps-123-1210-and-136) says, and add it to the arbiter's **Piece Sets**.

A world's set never changes when the package updates. The full guide is step 15.5's.

## Chess Arbiter

`Prefabs/Chess Arbiter.prefab` (`ChessArbiter.prefab` beside the scripts before step 14.1, moved with its GUID kept): the instance's `ChessArbiter` ([settings](settings.md)) with:

- a `FirstJoinerAuth` on the same object, which its **Simple Auth** field points to: the first player in the instance is the organizer ([organizers](organizers.md));
- a nested instance of **Chess Pieces** as its child, listed alone in **Piece Sets**, so it's the default set;
- `boardCount` (4) and the instance defaults (rules, inactivity, forced lobby settings) at their defaults.

Place one per world. It needs nothing set after placing it; the boards and the settings menu point at it (steps 14.2 and 14.7).

**Choosing the organizers:** to use another rule, add `InstanceMasterAuth` (whoever is instance master), `NoOneAuth` (nobody; the editor defaults apply unchanged) or your own `SimpleAuth` subclass to the arbiter's object, drag it into **Simple Auth**, and remove the `FirstJoinerAuth` if you like. Simple Auth must not be left empty: a build is cancelled while it is.

**Piece sets:** add your own sets after **Chess Pieces** (it stays the default at index 0), or put yours first to make it the default. The list must not be empty or have an empty entry: a build is cancelled while it does.

## Chess Board

`Prefabs/Chess Board.prefab` (step 14.2): the logical board, a `ChessBoard` ([architecture](architecture.md#logical-board-and-physical-board), [board lifecycle](board-lifecycle.md)) holding the game, its rules and its synced state, with a `ChessPgnImporter` of its own as its child **ChessPgnImporter**, already its **Importer** (PGN imports, [PGN](pgn.md#import-local-rare)). It has nothing to see: its view and UI (steps 14.3 and 14.4) show it.

Place one per board. After placing it, set:

- **Arbiter**: the scene's **Chess Arbiter**. Left empty, the board logs an error on start and plays with every setting off.
- **Board Number**: 0 for the first board, 1 for the next, and so on, each board its own. It picks the board's per-board setting overrides on the arbiter ([settings](settings.md#per-board-overrides)), so two boards with the same number share them; numbers from the arbiter's **Board Count** (4) up have none and always use the instance defaults. Raise **Board Count** for more boards with overrides of their own.
- **Archive**: the scene's game archive (step 14.5), where the board's finished games go ([archive](archive.md)). Left empty, games are still logged but not archived.
- **Listeners**: nothing for the package's own views. The view, the UI and the 2D view add themselves when they start (`AddListener`); list here only a behaviour of your own that doesn't.

**PGN Event** and **PGN Site** (empty and `VRChat`) are the tags of the board's exported and logged games. Step 14.11 will fill in the arbiter, archive and board numbers by itself.
