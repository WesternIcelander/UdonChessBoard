# Prefabs

World creators add the chess objects by dragging prefabs from `Assets/Siggi/ChessBoard/Prefabs/` into their scene (step 14). Each prefab is self-contained: its meshes, materials and Udon programs are saved as assets, so it works in a fresh scene, and it holds nothing for tests or debugging. Placed instances get their network IDs from the VRChat SDK as other scene objects do.

A prefab can't reference objects in the scene, so the links between the chess objects (a board to its arbiter and archive, a view to its board, …) are set after placing them. Most of them fill themselves in: the [wiring script](#wiring) (step 14.11) links each empty one to the one object of its kind in the scene when the scene is saved, when Play Mode starts and before every build, and numbers the boards. Each section below says what it sets and what is left to you.

**The prefabs are the source.** Until step 14.12 the **Tools > Chess Board** builders made each prefab when it was missing and otherwise updated it in place, keeping its objects ([compatibility](compatibility.md), rule 1); 14.12 deleted them. From then on the prefabs are changed in place in Unity, and their meshes, materials and sprites (`Models/`, `Sprites2D/`) are saved assets, edited in place too (rule 3). A builder can be brought back from Git history if ever needed.

**The package** is the contents of `Assets/Siggi/ChessBoard/`: the behaviours and their Udon program assets, `Rules/`, `Prefabs/`, `Models/`, `Sprites2D/`, `Sounds/`, `Languages/`, and in `Editor/` the three editor scripts it needs: the build check (`ChessArbiterBuildCheck.cs`), the 2D view's sync check (`ChessBoard2DViewSyncCheck.cs`, 12.16) and the [wiring script](#wiring) (`ChessSceneWiring.cs`). It depends on `io.siggi.ustools` (`SimpleAuth` and its built-ins, and SimpleI18n), from the VPM listing `https://vpm.siggi.io/index.json`; its `package.json`, with that dependency in `vpmDependencies`, comes with the first release (step 16.1). Nothing under `Assets/Tests/` is part of it.

Every chess prefab shares one `I18n`, the [Chess Language](#chess-language) prefab (step 14.10), placed once per scene beside the chess objects (or beside one of their parents), so the `I18n` sanity check links them to it ([architecture](architecture.md#player-text-step-135)).

**The minimum a world needs:** one **Chess Arbiter**, one **Chess Language** (without it every text shows its key), and per board a **Chess Table** wired to it (or, placed one by one, a **Chess Board** with a **Chess Board View** and a **Chess Board UI** on it; the UI holds the buttons: without it, nobody can join or start a game). Add one **Game Archive** per world to keep finished games (for the history wall and **Load it here**), listed in the arbiter's **Archives**; without one, games are only logged. A **Game History Wall** on it lists those games for players to pick, export and load. The **Personal Game Archive** and the **Personal History Wall** on it keep and show each player's own games across visits. A **Chess Settings Menu** on the arbiter lets organizers change the settings in-world. A **2D Chess Board View** (or the **Movable 2D Chess Board View**) on the boards lets spectators watch them from afar.

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
- **Board Count** (4), first in its Inspector, the **PGN** tags **PGN Event** and **PGN Site** (empty and `VRChat`, step 13.11: the Event and Site tags of every board's games, `?` when empty) and the instance defaults (rules, inactivity, forced lobby settings) at their defaults.

Place one per world. Its **Archives** (step 13.10, empty in the prefab) are the scene's **Game Archive** and **Personal Game Archive**, where every board's logged games go: the wiring fills an empty list with every archive in the scene, and warns about an archive in the scene that a list already set leaves out. With no archive, games are only logged. Its **Board Count** is set by the wiring to the highest board number on it plus one. Change **PGN Event** and **PGN Site** to name your world's event and site in its games' PGN, if you like. The boards and the settings menu point at it (steps 14.2 and 14.7).

**Choosing the organizers:** to use another rule, add `InstanceMasterAuth` (whoever is instance master), `NoOneAuth` (nobody; the editor defaults apply unchanged) or your own `SimpleAuth` subclass to the arbiter's object, drag it into **Simple Auth**, and remove the `FirstJoinerAuth` if you like. Simple Auth must not be left empty: a build is cancelled while it is.

**Piece sets:** add your own sets after **Chess Pieces** (it stays the default at index 0), or put yours first to make it the default. The list must not be empty or have an empty entry: a build is cancelled while it does.

## Chess Board

`Prefabs/Chess Board.prefab` (step 14.2): the logical board, a `ChessBoard` ([architecture](architecture.md#logical-board-and-physical-board), [board lifecycle](board-lifecycle.md)) holding the game, its rules and its synced state, with a `ChessPgnImporter` of its own as its child **ChessPgnImporter**, already its **Importer** (PGN imports, [PGN](pgn.md#import-local-rare)). It has nothing to see: its view and UI (steps 14.3 and 14.4) show it.

Place one per board. After placing it, set:

- **Arbiter**: the scene's **Chess Arbiter**. Left empty, the board logs an error on start and plays with every setting off.
- **Board Number**: 0 for the first board, 1 for the next, and so on, each board its own. It picks the board's per-board setting overrides on the arbiter ([settings](settings.md#per-board-overrides)), so two boards with the same number share them; numbers from the arbiter's **Board Count** (4) up have none and always use the instance defaults. The wiring keeps **Board Count** at the highest board number plus one, inactive boards included, so every board has overrides of its own.
- **Listeners**: nothing for the package's own views. The view, the UI and the 2D view add themselves when they start (`AddListener`); list here only a behaviour of your own that doesn't.

The PGN Event and Site tags of its games are the arbiter's (the board's own fields went in step 13.11). Its games go to the arbiter's **Archives** (the board's own **Archive** field went in step 13.10). The wiring fills in **Arbiter** and gives a board without a number of its own the lowest free one, so a placed board needs nothing set.

## Chess Board View

`Prefabs/Chess Board View.prefab` (step 14.3): the physical board, a `ChessBoardView` ([physical pieces](physical-pieces.md), [player input](player-input.md)) with everything it places: the placeholder **Table**, **Board** and **Squares**, the **Player Blocker**, the 32 **Pieces** (each a `VRCPickup` with a `ChessPiece`), the 64 **Square Targets** for desktop clicks, the **Highlights** (last move, check lines, legal targets), the **Promotion Ghosts**, the **Captured Places** and its **Sounds**. Its meshes and materials are the generated ones in `Models/`; its pieces wear the arbiter's piece sets once the game starts ([customizing](customizing.md#chess-piece-sets-steps-123-1210-and-136)), so the view holds no set of its own. It has no buttons or status display: those are the board's UI (step 14.4).

Its origin is the board's centre on the playing surface, with files along +x and White's end at −z; the table reaches 0.8 m down from there, so raise a placed view 0.8 m above the floor (the [Chess Table](#chess-table) does that for you).

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

## Chess Table

`Prefabs/Chess Table.prefab` (step 14.9): a playable board in one drag. Its root, **Chess Table**, holds nothing itself; its children are nested instances of the three prefabs above, already wired to each other:

- **Chess Board** at the root's origin;
- **Chess Board View** and **Chess Board UI** 0.8 m above it, their origin on the playing surface (so the table stands on the floor where the root is), both with **Board** set to the table's own Chess Board.

Since they're nested instances, a later change to the board, view or UI prefab reaches every table. Drop it onto the ground, as many as you need (one per board). After placing it, set on its **Chess Board** child:

- **Arbiter**: the scene's **Chess Arbiter**.
- **Board Number**: 0 for the first table, 1 for the next, and so on (as for [Chess Board](#chess-board)).

Nothing else: the view and UI add themselves to the board's listeners, the archives are on the arbiter, "Load it here" uses the arbiter's pick, and the `I18n` fields are the sanity check's. Customize the view on the table as on a placed view ([customizing](customizing.md#the-board-model-and-scale-step-122)): the changes are overrides on the table instance. The wiring fills in **Arbiter** and gives a board without a number of its own the lowest free one, so a placed board needs nothing set.

The table is a plain object holding the three, not a variant of the Chess Board prefab, so the board, view and UI stay siblings, as they are when placed one by one, and each is reached the same way whichever was used.

## Game Archive

`Prefabs/Game Archive.prefab` (step 14.5): the instance's archive of finished games, an `InstanceGameArchive` ([archive](archive.md); the script was `GameArchive` before step 13.10, renamed with its GUID kept, so the prefab didn't change) with its 10 synced `GameArchiveHolder`s (**Holder 0** to **Holder 9**, 5 games each, so the last 50 games) and the local-only **Recent (local only)** holder as its children, already in its **Holders** and **Recent** fields, each holder's **Archive** pointing back. Its holders and the archive itself are synced objects ([networking](networking.md)): only the archive's owner writes to them.

Place one per world. It needs nothing set after placing it; the arbiter lists it (its **Archives**, [Chess Arbiter](#chess-arbiter)), and the history wall reads it (step 14.6). Don't change its holders: the archive logs an error on start when one is missing or the recent holder isn't local only, and reverting the fields and objects to the prefab's mends it.

## Personal Game Archive

`Prefabs/Personal Game Archive.prefab` (step 14.6): the local player's own games, a `PersonalGameArchive` ([archive](archive.md#personal-archive-step-1310)) on its own, kept in their VRChat PlayerData across visits and instances; nothing synced. **Capacity** (20 games) and **Key Prefix** (`siggi.chess.personal.`) are at their defaults.

Place at most one per world. After placing it, list it in the arbiter's **Archives** beside the Game Archive, so every board's games reach it too. Change **Key Prefix** only when the world's own PlayerData keys could clash with it: games saved under the old prefix are no longer read.

## Game History Wall

`Prefabs/Game History Wall.prefab` (step 14.6): a portrait world-space canvas (1.2 m by 1.6 m) with a `GameHistoryWall` ([archive](archive.md#history-wall-step-102a)): the **Title**, the hidden **Export PGN** button at its right end, the **Games** scroll view with 50 entries (one per game the Game Archive holds, each with its `GameHistoryRow`, which picks the entry's game), and the hidden **PGN Box** that Export PGN opens over the list's lower half. Its labels are written from the language pack at run time. It takes the pointer through a trigger collider and `VRCUiShape`, so players walk through it.

Place one per world, where players can read it; it faces +z, and its origin is its centre. After placing it, set:

- **Archive**: the scene's **Game Archive**. Left empty, the wall logs an error on start and shows nothing. The wiring fills it in.
- **I18n**: nothing. The `I18n` sanity check fills it from the Chess Language on build and before Play Mode.

A game picked on it is offered by every board's **Load it here** (the pick is on the arbiter), so it needs no board.

## Personal History Wall

`Prefabs/Personal History Wall.prefab` (step 14.6): the same wall for the **Personal Game Archive**, with 20 entries (its default capacity), each with a **Delete** button at its right end (pressed twice within 3 s), and its title from `wall_title_personal` (`My games`). The wall makes more entries at run time when its archive's **Capacity** is raised, so it needs no change for that. The wiring sets its **Archive** to the scene's **Personal Game Archive** (a wall with delete buttons gets the personal archive, one without the Game Archive).

It's a prefab of its own, not a variant of the Game History Wall: every entry differs (the delete button, a narrower label) and so does their number, so a variant would override nearly all of it, and a change to the instance wall's entries could leave the delete buttons out of place.

## Chess Settings Menu

`Prefabs/Chess Settings Menu.prefab` (step 14.7): the organizers' settings menu, a landscape world-space canvas (1.5 m by 1.42 m) with a `ChessSettingsMenu` ([settings](settings.md#settings-menu-step-111a), [organizers](organizers.md)): the **Title**, the **Notice** everyone but organizers sees, and the **Controls** only organizers see: the scope row (All boards or one board, and All default), a row per setting with its label, its value and its three buttons, and **Apply now** with its outcome below it. Each button has a `ChessSettingsButton` of its own, which passes its row and action to the menu. Its labels are written from the language pack at run time. It takes the pointer through a trigger collider and `VRCUiShape`, so players walk through it.

Place at most one per world, where organizers can reach it; it stands at its centre, unrotated, read from its −z side. After placing it, set:

- **Arbiter**: the scene's **Chess Arbiter**. Left empty, the controls stay hidden from everyone and the menu shows only its notice.
- **Boards**: every **Chess Board** on that arbiter, in board-number order, for **Apply now** (re-snapshots). A board left out isn't re-snapshotted from the menu; its settings still change for its next game.
- **I18n**: nothing. The `I18n` sanity check fills it from the Chess Language on build and before Play Mode.

Without it, nobody can change the settings in-world: the arbiter's Inspector values hold for the whole instance. The wiring fills in **Arbiter** and an empty **Boards** (every board on the arbiter, by number), and warns about a board a list already set leaves out.

## 2D Chess Board View

`Prefabs/2D Chess Board View.prefab` (step 14.8): a flat view of one board at a time for watching, a landscape world-space canvas (1.36 m by 1.41 m) with a `ChessBoard2DView` ([2D board view](board-2d.md)): the selector (`<`, **Board N**, `>`), the 8×8 board with a piece image on each square (the sprites in `Sprites2D/`), the players and status texts, the step buttons (`<<` `<` `>` `>>`) of its own replay, and the move list on the right, each cell with its `ChessMoveListCell`. It takes the pointer through a trigger collider and `VRCUiShape`, so players walk through it. It has no pickup: it stays where it's placed.

Place as many as you like; it faces +z (read looking along +z), and its origin is its centre. After placing it, set:

- **Boards**: the **Chess Board**s it steps through, in the order Left and Right go (by board number, as the settings menu numbers them). Left empty, the view logs an error on start and shows nothing. The wiring fills an empty list (without a **Locked Board**) with every board in the scene, by number; a list already set is yours, so add a board placed later to it, or empty it to have it filled again.
- or **Locked Board**: one board, for a view beside its table; the selector is then hidden and **Boards** isn't read.
- **Synced**: your choice, off in the prefab. On, everyone sees the board and position last chosen by anyone, late joiners included; off, each player chooses for themselves.
- **I18n**: nothing. The `I18n` sanity check fills it from the Chess Language on build and before Play Mode.

## Movable 2D Chess Board View

`Prefabs/Movable 2D Chess Board View.prefab` (step 14.8): the same view that players can move and resize ([Moving and resizing](board-2d.md#moving-and-resizing-step-1216)): a `ChessBoard2DViewHolder` at its root with a nested instance of the **2D Chess Board View** prefab as its child (so a change to that prefab reaches both), the **Grip** (a bar under the view, a `VRCPickup`) and the **Corner** (a cube at its bottom right, a second pickup, for resizing). Its origin is the view's centre, as the fixed one's is.

After placing it, set the nested view's **Boards** (or **Locked Board**) and **Synced** as for the fixed view; the holder needs nothing. **Movable** on the holder (on by default) turns moving off (the bar and corner hidden). **Min Size** and **Max Size** (0.25 and 3) are the sizes the corner allows; the bar is fitted to the smallest, so after lowering it check that the bar stays clear of the corner.

**Synced and `VRCObjectSync`:** with **Synced** on, the grip needs a `VRCObjectSync` (so everyone sees the view move), and with it off it must have none. The prefab's grip has none, as its view isn't synced; before every VRChat build, `ChessBoard2DViewSyncCheck` adds one to an instance's grip whose view is synced (an added component, an override on the instance) and removes it when Synced is turned off again, so ticking **Synced** is all a world does. Decided in 14.8: that is enough for instances, so there's no prefab variant per case. In Play Mode (ClientSim, one player) the missing component changes nothing.

**Which to pick:** the fixed view for a view that stays put (one locked beside each table, a wall of games); the movable one for a view players carry around or resize.

## Chess Language

`Prefabs/Chess Language.prefab` (step 14.10): the chess objects' shared `I18n` ([architecture](architecture.md#player-text-step-135)), on an object of its own with no children, holding the package's language packs (`Languages/en.json` first, the fallback, then any other pack in `Languages/` by file name). It isn't **Main I18n**, so it doesn't clash with a world's own main `I18n`, which switches it with `SetLanguageGlobally`.

Place one per scene. Every behaviour of ours with an **I18n** field (the board's view and UI, the history walls, the settings menu, the 2D views and the movable view's holder) leaves it empty in its prefab: the `I18n` sanity check of `io.siggi.ustools` fills it on build and before Play Mode, after asking in a dialog (answer **Yes**), with the `I18n` closest to the behaviour: one beside it (a sibling), else one beside its parent, and so on up to the scene's root objects. So put Chess Language:

- **beside the chess objects**, e.g. all at the scene's root; or
- **beside one of their parents:** at the root with the chess objects inside objects of your own (a **Chess Table**'s view and UI find it this way: nothing is beside them inside the table, so the table's own level counts).

**A world `I18n` of its own at the same level is ambiguous:** two `I18n`s beside each other are equally close, so the sanity check can't pick one for the objects there and cancels the build (and Play Mode), naming that level. Group the chess objects under an empty parent of their own (e.g. **Chess**) with Chess Language beside them inside it; the world's `I18n` stays elsewhere for the world's objects, which then get theirs. Or add an `I18nLock` to an object to keep its fields as you set them by hand.

**Adding or changing a language:** add a pack to the placed Chess Language's **Language Packs** (an override on the instance, after the package's), as [customizing](customizing.md#languages-step-135) says. Every board, wall, menu and 2D view uses that one Chess Language, so the pack reaches them all, and a package update that adds packs or keys still reaches the instance. Don't put your pack first: the first pack is the fallback for keys a pack leaves out.

## Wiring

`ChessSceneWiring` (`Editor/ChessSceneWiring.cs`, step 14.11) links the placed prefabs to each other, so a world drops them in and sets nothing. It runs on each loaded scene when the scene is saved (`EditorSceneManager.sceneSaving`), when Play Mode is entered (`playModeStateChanged`, on `ExitingEditMode`, so the first test already has everything) and before every VRChat build (`IVRCSDKBuildRequestedCallback`, before the build check), and on demand from **Tools > Chess Board > Wire Chess Objects**. Each empty reference takes the one object of its kind in the same scene (inactive ones included):

| Field | Filled with |
|---|---|
| a board's **Arbiter**, the settings menu's **Arbiter** | the scene's **Chess Arbiter** |
| the arbiter's **Archives** (when empty) | every archive in the scene, the Game Archive and the Personal Game Archive |
| a history wall's **Archive** | the **Personal Game Archive** for a wall with delete buttons (the Personal History Wall), the **Game Archive** for one without |
| the settings menu's **Boards** (when empty) | every board on its arbiter, by board number |
| a 2D view's **Boards** (when empty and without a **Locked Board**) | every board in the scene, by board number |

It also gives each board without a number of its own (one another board on the same arbiter has, or a negative one) the lowest free number: of boards sharing a number, one whose number was set (an override, not the prefab's untouched 0) keeps it, then one wired before (its **Arbiter** already set), so a table dropped in beside wired ones takes the next number and theirs stay, wherever it lands in the hierarchy; otherwise the first in the hierarchy keeps it. And it sets each arbiter's **Board Count** to its highest board number plus one, inactive boards counted too, raising or lowering it (logged), so every board has per-board overrides of its own.

A list that's already set (the arbiter's **Archives**, the menu's **Boards**, a 2D view's **Boards**) is the world's choice and stays as it is; for the arbiter's and the menu's, the wiring warns about an archive or an active board in the scene the list leaves out. Empty a list to have it filled again. The view's and UI's **Board** come set in the Chess Table, and the `I18n` fields are the `I18n` sanity check's ([Chess Language](#chess-language)).

Every change is logged (`[ChessSceneWiring] <object>: <field> set to …`), undoable, recorded as an override on the prefab instance and copied to its Udon data, and it marks the scene dirty, so it's saved with the scene. A reference it can't fill, because the scene has no object of that kind or several (two arbiters, say), is logged as an error naming the candidates; before a build it also cancels the build, selects the object and explains in a dialog. Saving and Play Mode go ahead, with the error logged. Drag the right object into the field by hand to choose among several: a reference that's set is never changed.

## Sync methods

Each prefab saves every `UdonBehaviour` with the sync method its script asks for (`UdonBehaviourSyncMode`: Manual for the board, arbiter, archive, 2D view and holder, None for the views, Continuous for the pieces' and squares' `NoVariableSync`), as UdonSharp sets it on scene objects. UdonSharp doesn't set it on an object saved straight to a prefab, so before step 14.5 the built prefabs held Continuous everywhere; the builders set it whenever they saved a prefab from then on, the saved prefabs keep it, and **Test Prefabs** checks it.
