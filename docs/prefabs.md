# Prefabs

World creators add the chess objects by dragging prefabs from `Assets/Siggi/ChessBoard/Prefabs/` into their scene (step 14). Each prefab is self-contained: its meshes, materials and Udon programs are saved as assets, so it works in a fresh scene, and it holds nothing for tests or debugging. Placed instances get their network IDs from the VRChat SDK as other scene objects do.

A prefab can't reference objects in the scene, so the links between the chess objects (a board to its arbiter and archive, a view to its board, …) are set after placing them: by hand in the Inspector, until step 14.11 fills them in automatically. Each section below says what to set.

Until step 14.13 the **Tools > Chess Board** builders make each prefab when it's missing and otherwise update it in place, keeping its objects ([compatibility](compatibility.md), rule 1). From then on the prefabs are the source, changed in place in Unity.

Every chess prefab shares one `I18n`, the **Chess Language** prefab (step 14.10), placed once per scene as a sibling or on a parent of the chess objects, so the `I18n` sanity check links them to it ([architecture](architecture.md), languages).

**The minimum a world needs:** one **Chess Arbiter**, and per board a **Chess Board** wired to it with a **Chess Board View** and a **Chess Board UI** on it (the UI holds the buttons: without it, nobody can join or start a game). Add one **Game Archive** per world to keep finished games (for the history wall and **Load it here**), listed in the arbiter's **Archives**; without one, games are only logged. The **Personal Game Archive** and its wall (step 14.6) keep each player's own games across visits.

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

Place one per world. After placing it, set **Archives** (step 13.10, empty in the prefab): the scene's **Game Archive** and **Personal Game Archive**, where every board's logged games go. Left empty, games are only logged. The boards and the settings menu point at it (steps 14.2 and 14.7).

**Choosing the organizers:** to use another rule, add `InstanceMasterAuth` (whoever is instance master), `NoOneAuth` (nobody; the editor defaults apply unchanged) or your own `SimpleAuth` subclass to the arbiter's object, drag it into **Simple Auth**, and remove the `FirstJoinerAuth` if you like. Simple Auth must not be left empty: a build is cancelled while it is.

**Piece sets:** add your own sets after **Chess Pieces** (it stays the default at index 0), or put yours first to make it the default. The list must not be empty or have an empty entry: a build is cancelled while it does.

## Chess Board

`Prefabs/Chess Board.prefab` (step 14.2): the logical board, a `ChessBoard` ([architecture](architecture.md#logical-board-and-physical-board), [board lifecycle](board-lifecycle.md)) holding the game, its rules and its synced state, with a `ChessPgnImporter` of its own as its child **ChessPgnImporter**, already its **Importer** (PGN imports, [PGN](pgn.md#import-local-rare)). It has nothing to see: its view and UI (steps 14.3 and 14.4) show it.

Place one per board. After placing it, set:

- **Arbiter**: the scene's **Chess Arbiter**. Left empty, the board logs an error on start and plays with every setting off.
- **Board Number**: 0 for the first board, 1 for the next, and so on, each board its own. It picks the board's per-board setting overrides on the arbiter ([settings](settings.md#per-board-overrides)), so two boards with the same number share them; numbers from the arbiter's **Board Count** (4) up have none and always use the instance defaults. Raise **Board Count** for more boards with overrides of their own.
- **Listeners**: nothing for the package's own views. The view, the UI and the 2D view add themselves when they start (`AddListener`); list here only a behaviour of your own that doesn't.

**PGN Event** and **PGN Site** (empty and `VRChat`) are the tags of the board's exported and logged games. Its games go to the arbiter's **Archives** (the board's own **Archive** field went in step 13.10). Step 14.11 will fill in the arbiter and board numbers by itself.

## Chess Board View

`Prefabs/Chess Board View.prefab` (step 14.3): the physical board, a `ChessBoardView` ([physical pieces](physical-pieces.md), [player input](player-input.md)) with everything it places: the placeholder **Table**, **Board** and **Squares**, the **Player Blocker**, the 32 **Pieces** (each a `VRCPickup` with a `ChessPiece`), the 64 **Square Targets** for desktop clicks, the **Highlights** (last move, check lines, legal targets), the **Promotion Ghosts**, the **Captured Places** and its **Sounds**. Its meshes and materials are the generated ones in `Models/`; its pieces wear the arbiter's piece sets once the game starts ([customizing](customizing.md#chess-piece-sets-steps-123-1210-and-136)), so the view holds no set of its own. It has no buttons or status display: those are the board's UI (step 14.4).

Its origin is the board's centre on the playing surface, with files along +x and White's end at −z; the table reaches 0.8 m down from there, so raise a placed view 0.8 m above the floor (step 14.9's Chess Table does that for you).

Place one per board. After placing it, set:

- **Board**: the board it shows, a placed **Chess Board**. The view adds itself to the board's listeners when it starts; left empty, it shows nothing.
- **I18n**: nothing. The `I18n` sanity check fills it from the Chess Language on build and before Play Mode.

What a world may change on it (its model, its size, the captured places) is in [customizing](customizing.md#the-board-model-and-scale-step-122). Change it on the placed instance (an override) or unpack it; the package's later updates reach whatever the instance leaves as it is.

## Chess Board UI

`Prefabs/Chess Board UI.prefab` (step 14.4): the board's UI, a `ChessBoardUI` ([player input](player-input.md#the-boards-ui-step-121)) with the **Buttons (White)** and **Buttons (Black)** panels (each button calling the UI's `_Press…` event for its action), the **Status (White)** and **Status (Black)** panels ([physical pieces](physical-pieces.md)), the hidden **PGN Box** with its copy and paste parts ([PGN](pgn.md#input--output)), and its **Sounds** (every sound but the move sounds). Its labels and hints are empty in the prefab: the UI writes them from the language pack.

Its origin is the board's centre on the playing surface with the view's axes, as the view's is: its panels stand at the ends of the view's table and the PGN box over the board's far edge. Put it where the board's **Chess Board View** stands, with the same rotation and scale (beside the view, not inside it), so the panels sit on the table.

Place one per board. After placing it, set:

- **Board**: the board it shows and acts on, the same **Chess Board** as the view's. The UI adds itself to the board's listeners when it starts; left empty, it logs an error and shows nothing.
- **I18n**: nothing. The `I18n` sanity check fills it from the Chess Language on build and before Play Mode.

**Clock Bases** and **Clock Increments** are the time controls the Clock button steps through. A world that wants its own UI can leave this prefab out and write a `ChessBoardListener` of its own; the view works without it.

## Game Archive

`Prefabs/Game Archive.prefab` (step 14.5): the instance's archive of finished games, an `InstanceGameArchive` ([archive](archive.md); the script was `GameArchive` before step 13.10, renamed with its GUID kept, so the prefab didn't change) with its 10 synced `GameArchiveHolder`s (**Holder 0** to **Holder 9**, 5 games each, so the last 50 games) and the local-only **Recent (local only)** holder as its children, already in its **Holders** and **Recent** fields, each holder's **Archive** pointing back. Its holders and the archive itself are synced objects ([networking](networking.md)): only the archive's owner writes to them.

Place one per world. It needs nothing set after placing it; the arbiter lists it (its **Archives**, [Chess Arbiter](#chess-arbiter)), and the history wall reads it (step 14.6). Don't change its holders: the archive logs an error on start when one is missing or the recent holder isn't local only, and reverting the fields and objects to the prefab's mends it.

## Sync methods

Each prefab saves every `UdonBehaviour` with the sync method its script asks for (`UdonBehaviourSyncMode`: Manual for the board, arbiter and archive, None for the views, Continuous for the pieces' and squares' `NoVariableSync`), as UdonSharp sets it on scene objects. UdonSharp doesn't set it on an object saved straight to a prefab, so before step 14.5 the built prefabs held Continuous everywhere; the builders now set it whenever they save a prefab (`ChessBuilderUtil.SetSyncMethods`), and **Test Prefabs** checks it.
