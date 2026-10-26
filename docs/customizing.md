# Customizing a board

What a world creator can change on a board view (the **Chess Board View** prefab, [Prefabs](prefabs.md#chess-board-view), placed by hand or by **Tools > Chess Board > Create Board View**), without touching the code. On a placed instance the changes are overrides: deleting **Table**, **Board** or **Squares** and adding a model of your own work there as on an unpacked view.

## The board model and scale (step 12.2)

A view (`ChessBoardView (<board>)`) holds these children:

| Child | What it is | May it go? |
|---|---|---|
| **Table** | the placeholder table | yes |
| **Board**, **Squares** | the placeholder board: its frame, and the 64 squares as one mesh | yes |
| **Player Blocker** | the solid box keeping players without a seat out of the table ([Physical pieces](physical-pieces.md#walking-through-the-board-step-95a2)) | keep it; resize its `BoxCollider` to a table of your own |
| **Pieces**, **Square Targets**, **Highlights**, **Promotion Ghosts** | the pieces, desktop mode's click targets, the highlights and the promotion ghosts ([Promotion pieces](#chess-piece-sets-steps-123-1210-and-136)) | no: the view uses them |
| **Captured Places** | where captured pieces stand ([below](#captured-piece-places-step-124)) | move them freely; delete some for fewer places |

The view reads nothing from **Table**, **Board** or **Squares**, so delete them and put your own model in their place, as a child of the view so it scales with it:

- **The playing surface** at the view's y = 0 (the pieces stand on it).
- **a1** at the view's −x −z corner, files a to h along +x, ranks 1 to 8 along +z (White at −z), the board's centre at the view's origin.
- **Each square** `squareSize` wide (0.05 view units: the whole board 0.4 units across), so the model's squares lie under the square targets.
- **No colliders** on it, or triggers only, so players can walk up to the board and clicks reach the square targets and pieces. The square targets stay separate from the model: they take the clicks, so a model needs none of its own.

**Rebuilding** the view (**Create Board View** again, which later steps ask for) keeps these changes: a **Table**, **Board** or **Squares** deleted from the old view is left out of the new one, and the old view's children the builder didn't make (your model) move to the new one where they stood.

**Scaling:** scale the view's transform, uniformly (the same on x, y and z). Everything the view places (pieces, highlights, click targets, animations, captured pieces) is in its local space, so it all scales with it, and drops are read through the view's transform. `squareSize` isn't for resizing: the square targets and highlights are built for it.

- Give the board's UI (`ChessBoardUI (<board>)`, beside the view) the same scale and place, so its panels stay around the table.
- The table scales too: a view at 0.5 has a table half as tall, so lower the view (or use your own table) to stand it on the floor.
- Leave the view's groups (**Pieces**, **Square Targets**, **Highlights**) unmoved, unturned and unscaled: the view places their children in its own space. On Start it logs a warning naming the first object whose group isn't (`ChessBoardView.LocalSpaceProblem`).

## Chess piece sets (steps 12.3, 12.10 and 13.6)

The pieces' models come from **chess piece sets**: a `ChessPieceSet` behaviour on an object holding one template per colour and piece type (pawn, knight, bishop, rook, queen, king), and the promotion pieces (below). Any models will do, whatever way they face, whatever their size, wherever their origin and however many material slots they have: each is set up by moving, turning and scaling its template in the editor, where it can be seen.

**The world's sets.** The Chess Arbiter lists every set the world offers in **Piece Sets** (under **Pieces**); the first is the default. Each board syncs which of them White and Black wear, so every player sees the same pieces, and a reset puts both back to the first. The **Chess Arbiter** prefab comes with one, **Chess Pieces** (the prefab of that name, from the placeholder models in `Assets/Siggi/ChessBoard/Models/`, with the White Pieces and Black Pieces materials), nested under it and listed first ([Prefabs](prefabs.md)). The list may not be empty or have an empty entry: a build is cancelled while it does (with a dialog saying how to fix it). (Before step 13.6 an arbiter had two sets, **White Pieces** and **Black Pieces**, and before 14.1 Chess Pieces was built under the scene's arbiter; the builder replaces both with the nested prefab.)

**Making a set.** Drag the **Chess Pieces** prefab (`Assets/Siggi/ChessBoard/Prefabs/`) into the scene, right-click it and choose **Prefab > Unpack Completely**, so the set is entirely your world's own and a package update never changes it (a prefab variant would follow the package's set wherever you haven't overridden it). Then rename it and change its templates:

- It has a slot per piece type for each colour (**White Pawn** … **White King**, then **Black Pawn** … **Black King** two rows behind), and between them a row for White's promotion pieces (below), each holding a **Guide** (a green square frame, one square of the board) and a **Model** (the template). The slot stands for the square: its centre is the square's centre, its y = 0 the playing surface.
- For each White **Model**, put your mesh in its `MeshFilter` and your materials in its `MeshRenderer`, one per slot of the mesh. White's are all needed.
- Then move, turn and scale the **Model** under its slot until the piece stands upright on the guide at the size you want, its base on the guide's surface. That local position, rotation and scale is what every piece of that type gets on the board (in the board view's units, so they scale with the view).
- Set White's up facing Black (+z, the knight looking along the slot's blue axis).
- **Black's pieces** have three ways to go, per type:
  - **Only a different colour** (as in **Chess Pieces**): leave Black's **Model** without a mesh (no `MeshFilter`, or an empty one) and give its `MeshRenderer` Black's materials. Black then wears White's model turned to face White, in Black's materials.
  - **The same look for both sides:** leave Black's **Model** without a mesh and without materials (or delete it and its field). Black wears White's model and materials, turned to face White.
  - **A model of its own:** put Black's mesh and materials on its **Model** and set it up facing White (−z). It's used as you set it up, not turned.
- The promotion pieces fall back the same way: Black's promotion slots are optional, and without a mesh Black's ghosts wear White's.
- Keep the slots themselves unscaled and unturned so what you see in the editor is what the board shows: only each **Model**'s own transform is read. The set's position in the world doesn't matter.
- The set's own object must stay active; it hides its slots when the world starts, and the boards go on reading them.
- The set's fields (**White Pawn** … **Black Promotion Queen**) point at the templates: an unpacked copy's point at its own **Model**s; after duplicating a set of your own, check that they point at the copy's.
- Add it to the arbiter's **Piece Sets**. Its place in the list is its number for the methods below.

**Promotion pieces** (step 12.11). When a player drops (or click-places) a pawn on the last rank, see-through pieces float in a row above it, a knight, bishop, rook and queen, and clicking one promotes to it. They come from the set's promotion slots, **White Promotion Knight** … **White Promotion Queen** (and Black's, optional), set up exactly as the pieces are (mesh, materials, and the **Model**'s position, turn and size on its guide). **Chess Pieces** uses each type's regular mesh with one see-through green material, **Promotion Ghost** (in `Assets/Siggi/ChessBoard/Models/`), on every slot; give your set's promotion models a see-through material of your own, or the same one. A set missing a White one is named in the log when it's applied, as a missing piece template is. **Create Board View** adds White's promotion slots to a listed set that lacks them, copying each type's mesh and transform from the set's own White piece, and keeping everything else.

**Letting players choose.** No UI of ours changes a board's sets: give players your own (buttons, a pedestal per set, supporter-only sets), which does one of these from your UdonSharp script:

- **A player's preferred set:** set `arbiter.preferredPieceSet` (or call `arbiter.SetPreferredPieceSet(index)`, which refuses an index outside the list; `-1` for none) on that player's client. When they next join a board, it's put on both sides if the other seat is empty, otherwise on their colour alone: so with two players who each picked a set, White wears one's and Black the other's, synced to everyone.
- **A board's sides directly:** `board.SetPieceSet(color, index)` (`color` is `ChessRules.White` or `ChessRules.Black`) changes that side's set on that board for everyone, as the local player; it's refused unless they may take the board over (a seated player, anyone while a seat is empty, organizers; [Ownership](ownership.md)). `board.PieceSetIndex(color)` says which a side wears.
- **Only for one player:** `arbiter.SetPieceSetOverride(color, index)` shows that set for that colour on every board, on this client only, whatever the boards have synced (for example a player who finds a set hard to read); `-1` shows the boards' own again.

Only that side's pieces change, in place, at once: those on the board, in a hand, travelling, captured, promoted, and a fallen king.

## Captured-piece places (step 12.4)

Captured pieces stand on **places**: objects in the board view's **White Captured Places** and **Black Captured Places** fields (on `ChessBoardView (<board>)`), one list per colour of the pieces captured, filled in order: the first piece captured stands on the first place, the second on the second, and so on. **Create Board View** builds 16 per colour under **Captured Places** (**White 1** … **Black 16**), in two columns of 8 beside the board on the table, each colour's on the side of the player who captures it (White's pieces by Black, to Black's right).

- **Move them anywhere:** each place is just a position, read in the world, so it may be at another height than the board (a captured piece's trip arcs from the board to it), on a shelf or on the table, inside the view or elsewhere in the scene. Put the place where the piece's base should stand, as the square's centre on the playing surface is for a piece on the board: the piece set's offset, turn and size apply there too. The place's own rotation isn't used.
- **Fewer places:** delete places (or leave entries empty); the rest are filled in order, and captured pieces beyond the last place are hidden until a takeback or a replay step frees a place for them. 16 per colour is enough for every piece a side can lose.
- **Your own objects:** put any objects in the two fields instead, in the order you want them filled.
- **Rebuilding** the view keeps your places: the old view's **Captured Places** moves to the new one, and the fields keep pointing at the same objects.

Every player sees the same pieces in the same places, late joiners too: the order comes from the game's moves. A game from a position with pieces missing starts with those pieces in the first places (queen, rooks, bishops, knights, pawns), and a promoted pawn that's captured goes to its place as a pawn.

## Letting players move the 2D view (step 12.16)

A 2D board view ([2D board view](board-2d.md)) stays where it's placed unless it's in a holder. **Tools > Chess Board > Create Movable 2D Board View** puts the selected view in one (or builds a new view in one): players then move it by the bar under it and resize it by the cube at its bottom right ([Moving and resizing](board-2d.md#moving-and-resizing-step-1216)).

- **Allowing it:** tick or untick **Movable** on the holder (`<view> Holder`, `ChessBoard2DViewHolder`); off, the bar and cube are hidden and nothing can be picked up. **Min Size** and **Max Size** limit resizing (shares of the size it has in the scene).
- **Everyone or each player:** the view's own **Synced** decides both what it shows and where it is: on, a move or resize is seen by everyone, late joiners included; off, each player's stays their own. The grip's `VRCObjectSync` follows the tick by itself before every build.
- **Laying it out:** move the holder to place it all; or move the **Grip** and **Corner** relative to the view in the editor (the view keeps the place it has from the grip, and the corner sizes it from where it is), without scaling them. Scale the view itself, not the holder, to change its starting size.
- **Putting it back:** call the holder's `PutBack()` from a button of your own (as `SendCustomEvent("PutBack")`) to bring it back where you placed it, at its size there.

## Languages (step 13.5)

The chess objects' text comes from **Chess Language**, an `I18n` at the scene's root with the package's `Languages/en.json` (see [Architecture](architecture.md#player-text-step-135)). Each player sees the language of their VRChat client when a pack has it, English otherwise; a world whose own main `I18n` switches language (`SetLanguageGlobally`) switches the chess text too.

- **Adding a language:** write a pack with your language's `languageCode` (`de`, `ja`, …) and `languageName`, and the keys of `en.json` translated, keeping each `${name}` placeholder (they may move within the sentence), and add it to Chess Language's **Language Packs**. A key the pack leaves out shows in English. No code changes.
- **Changing some English texts:** add a pack with `languageCode` `en` and only the keys to change, after the package's: a pack with a code already loaded overrides only the strings it has.
- **Keep `en.json` first:** the first pack is the fallback for missing keys.
- Text that goes into a PGN export (tags, results, SAN) stays English, as PGN requires.

## Text and font (step 12.17)

Every text on the boards' UIs, the 2D views, the settings menu and the history wall is TextMeshPro (`TextMeshProUGUI`, and `TMP_InputField` for the PGN box's fields), so it stays sharp up close.

- **The font** is TextMeshPro's default, **LiberationSans SDF**, from TMP Essential Resources (`Assets/TextMesh Pro/`). The package doesn't ship them: when a world without them places one of the chess objects, TMP's importer pops up to import them (the VRChat SDK asks too). The objects point at the font by the GUID those resources always have (`8f586378b4e144a9851e7b34d9b748ee`), so they find it once imported.
- **Characters:** the font and its fallback (**LiberationSans SDF - Fallback**, a dynamic font asset) cover Latin, Greek and Cyrillic, so names such as `Siggі` or `Кот` and the board's own texts (`½-½`, `…`) all show. Characters outside them (Japanese, Chinese, Korean, emoji) show as `□` in Unity (in Play Mode too), with a warning naming the character in the log. In VRChat they show: the client brings its own TextMeshPro fallback fonts (checked with a Japanese player name from a pasted PGN, 2026-10-20).
- **Your own font:** give a text another font asset in its **Font Asset** field, or add a font asset with the characters you need to **LiberationSans SDF**'s **Fallback Font Assets** list.
