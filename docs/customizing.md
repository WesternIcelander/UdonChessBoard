# Customizing a board

What a world creator can change on a board view (the **Chess Board View** prefab, [Prefabs](prefabs.md#chess-board-view), on its own or in a **Chess Table**), without touching the code. On a placed instance the changes are overrides: deleting **Board** or **Squares** and adding a model of your own work there as on an unpacked view. The view has no table: it stands on the world's own (see [Prefabs](prefabs.md#chess-board-view) for the size it's made for).

## The board model and scale (step 12.2)

A view (`ChessBoardView (<board>)`) holds these children:

| Child | What it is | May it go? |
|---|---|---|
| **Board**, **Squares** | the placeholder board: its frame, and the 64 squares as one mesh | yes |
| **Player Blocker** | the solid box keeping players without a seat out of the table under the view ([Physical pieces](physical-pieces.md#walking-through-the-board-step-95a2)) | keep it; resize its `BoxCollider` to your table |
| **Pieces**, **Square Targets**, **Highlights**, **Promotion Ghosts** | the pieces, desktop mode's click targets, the highlights and the promotion ghosts ([Promotion pieces](#chess-piece-sets-steps-123-1210-and-136)) | no: the view uses them |
| **Captured Places** | where captured pieces stand ([below](#captured-piece-places-step-124)) | move them freely; delete some for fewer places |

The view reads nothing from **Board** or **Squares** (nor did it from the **Table** it had before the change to 14.3), so delete them and put your own model in their place, as a child of the view so it scales with it:

- **The playing surface** at the view's y = 0 (the pieces stand on it).
- **a1** at the view's −x −z corner, files a to h along +x, ranks 1 to 8 along +z (White at −z), the board's centre at the view's origin.
- **Each square** `squareSize` wide (0.05 view units: the whole board 0.4 units across), so the model's squares lie under the square targets.
- **No colliders** on it, or triggers only, so players can walk up to the board and clicks reach the square targets and pieces. The square targets stay separate from the model: they take the clicks, so a model needs none of its own.

**Package updates** keep these changes: the prefab is only ever edited in place ([compatibility](compatibility.md)), so a deleted **Board** or **Squares** stays deleted and your model stays where it is, as overrides on your instance. (Until step 14.12 a builder rebuilt views, carrying these changes over.)

**Scaling:** scale the view's transform, uniformly (the same on x, y and z). Everything the view places (pieces, highlights, click targets, animations, captured pieces) is in its local space, so it all scales with it, and drops are read through the view's transform. `squareSize` isn't for resizing: the square targets and highlights are built for it.

- Give the board's UI (`ChessBoardUI (<board>)`, beside the view) the same scale and place, so its panels stay around the table. The view works without it matching, but the panels then stand where the unscaled table's ends would be.
- The **Player Blocker** and the board's 2 cm thickness scale too: a view at 2 sits 4 cm above the table top and its blocker reaches 1.56 m down, so resize the blocker's `BoxCollider` to your table.
- The pickups' grab range is VRChat's, in metres: a much smaller view packs the pieces closer than a hand can tell apart in VR.
- Leave the view's groups (**Pieces**, **Square Targets**, **Highlights**) unmoved, unturned and unscaled: the view places their children in its own space. On Start it logs a warning naming the first object whose group isn't (`ChessBoardView.LocalSpaceProblem`).

## Chess piece sets (steps 12.3, 12.10 and 13.6)

The pieces' models come from **chess piece sets**: a `ChessPieceSet` behaviour on an object holding one template per colour and piece type (pawn, knight, bishop, rook, queen, king), and the promotion pieces (the see-through pieces a promotion is chosen from). Any models will do, whatever way they face, whatever their size, wherever their origin and however many material slots they have: each is set up by moving, turning and scaling its template in the editor, where it can be seen.

**The world's sets.** The Chess Arbiter lists every set the world offers in **Piece Sets** (under **Pieces**); the first is the default. Each board syncs which of them White and Black wear, so every player sees the same pieces, and a reset puts both back to the first. The **Chess Arbiter** prefab comes with one, **Chess Pieces** (the prefab of that name, from the placeholder models in `Assets/Siggi/ChessBoard/Models/`, with the White Pieces and Black Pieces materials), nested under it and listed first ([Prefabs](prefabs.md)). The list may not be empty or have an empty entry: a build is cancelled while it does (with a dialog saying how to fix it). (Before step 13.6 an arbiter had two sets, **White Pieces** and **Black Pieces**, and before 14.1 Chess Pieces was built under the scene's arbiter; the builder replaced both with the nested prefab until it was deleted in 14.12.)

**Making a set, and letting players choose one:** the step-by-step guide is [Piece sets](api/piece-sets.md): preparing the models, unpacking a copy of **Chess Pieces** (not a prefab variant), setting up each White template on its guide facing Black (+z), Black's three ways per type (materials only, nothing, or a model of its own facing White), the promotion pieces, adding the set to the arbiter's **Piece Sets** (its place is its index), checking it in Play Mode with the errors the view logs, and the calls a world's own UI makes (`arbiter.SetPreferredPieceSet`, `board.SetPieceSet`, `arbiter.SetPieceSetOverride`). No UI of ours changes a board's sets. A side's change of set changes its pieces in place, at once, wherever they are.

## Captured-piece places (step 12.4)

Captured pieces stand on **places**: objects in the board view's **White Captured Places** and **Black Captured Places** fields (on `ChessBoardView (<board>)`), one list per colour of the pieces captured, filled in order: the first piece captured stands on the first place, the second on the second, and so on. The **Chess Board View** prefab has 16 per colour under **Captured Places** (**White 1** … **Black 16**), in two columns of 8 beside the board on the table, each colour's on the side of the player who captures it (White's pieces by Black, to Black's right).

- **Move them anywhere:** each place is just a position, read in the world, so it may be at another height than the board (a captured piece's trip arcs from the board to it), on a shelf or on the table, inside the view or elsewhere in the scene. Put the place where the piece's base should stand, as the square's centre on the playing surface is for a piece on the board: the piece set's offset, turn and size apply there too. The place's own rotation isn't used.
- **Fewer places:** delete places (or leave entries empty); the rest are filled in order, and captured pieces beyond the last place are hidden until a takeback or a replay step frees a place for them. 16 per colour is enough for every piece a side can lose.
- **Your own objects:** put any objects in the two fields instead, in the order you want them filled.
- **Rebuilding** the view keeps your places: the old view's **Captured Places** moves to the new one, and the fields keep pointing at the same objects.

Every player sees the same pieces in the same places, late joiners too: the order comes from the game's moves. A game from a position with pieces missing starts with those pieces in the first places (queen, rooks, bishops, knights, pawns), and a promoted pawn that's captured goes to its place as a pawn.

## Letting players move the 2D view (step 12.16)

A 2D board view ([2D board view](board-2d.md)) stays where it's placed unless it's in a holder: place the **Movable 2D Chess Board View** prefab instead of the **2D Chess Board View** ([Prefabs](prefabs.md#movable-2d-chess-board-view), step 14.8). Players then move it by the bar under it and resize it by the cube at its bottom right ([Moving and resizing](board-2d.md#moving-and-resizing-step-1216)).

- **Allowing it:** tick or untick **Movable** on the holder (the prefab's root, `ChessBoard2DViewHolder`); off, the bar and cube are hidden and nothing can be picked up. **Min Size** and **Max Size** limit resizing (shares of the size it has in the scene).
- **Everyone or each player:** the view's own **Synced** decides both what it shows and where it is: on, a move or resize is seen by everyone, late joiners included; off, each player's stays their own. The grip's `VRCObjectSync` follows the tick by itself before every build.
- **Laying it out:** move the holder to place it all; or move the **Grip** and **Corner** relative to the view in the editor (the view keeps the place it has from the grip, and the corner sizes it from where it is), without scaling them. Scale the view itself, not the holder, to change its starting size.
- **Putting it back:** call the holder's `PutBack()` from a button of your own (as `SendCustomEvent("PutBack")`) to bring it back where you placed it, at its size there.

## Languages (step 13.5)

The chess objects' text comes from **Chess Language**, an `I18n` with the package's packs (`Languages/en.json`), placed once per scene from its prefab beside the chess objects (see [Prefabs](prefabs.md#chess-language) for where it goes, and [Architecture](architecture.md#player-text-step-135)). Each player sees the language of their VRChat client when a pack has it, English otherwise; a world whose own main `I18n` switches language (`SetLanguageGlobally`) switches the chess text too, where Chess Language has a pack of that language.

- **Adding a language:** write a pack with your language's `languageCode` (`de`, `ja`, …) and `languageName`, and the keys of `en.json` translated, keeping each `${name}` placeholder (they may move within the sentence), and add it to the end of your placed Chess Language's **Language Packs** (an override on the instance; every board, wall, menu and 2D view uses it). A key the pack leaves out shows in English. No code changes. With a world `I18n` of your own, give the chess pack the same `languageCode` as the world's, so its `SetLanguageGlobally` finds both.
- **Changing some English texts:** add a pack with `languageCode` `en` and only the keys to change, after the package's: a pack with a code already loaded overrides only the strings it has.
- **Keep `en.json` first:** the first pack is the fallback for missing keys.
- Text that goes into a PGN export (tags, results, SAN) stays English, as PGN requires.

## Text and font (step 12.17)

Every text on the boards' UIs, the 2D views, the settings menu and the history wall is TextMeshPro (`TextMeshProUGUI`, and `TMP_InputField` for the PGN box's fields), so it stays sharp up close.

- **The font** is TextMeshPro's default, **LiberationSans SDF**, from TMP Essential Resources (`Assets/TextMesh Pro/`). The package doesn't ship them: when a world without them places one of the chess objects, TMP's importer pops up to import them (the VRChat SDK asks too). The objects point at the font by the GUID those resources always have (`8f586378b4e144a9851e7b34d9b748ee`), so they find it once imported.
- **Characters:** the font and its fallback (**LiberationSans SDF - Fallback**, a dynamic font asset) cover Latin, Greek and Cyrillic, so names such as `Siggі` or `Кот` and the board's own texts (`½-½`, `…`) all show. Characters outside them (Japanese, Chinese, Korean, emoji) show as `□` in Unity (in Play Mode too), with a warning naming the character in the log. In VRChat they show: the client brings its own TextMeshPro fallback fonts (checked with a Japanese player name from a pasted PGN, 2026-10-20).
- **Your own font:** give a text another font asset in its **Font Asset** field, or add a font asset with the characters you need to **LiberationSans SDF**'s **Fallback Font Assets** list.
