# Customizing a board

What a world creator can change on a board built by **Tools > Chess Board > Create Board View**, without touching the code.

## The board model and scale (step 12.2)

A view (`ChessBoardView (<board>)`) holds these children:

| Child | What it is | May it go? |
|---|---|---|
| **Table** | the placeholder table | yes |
| **Board**, **Squares** | the placeholder board: its frame, and the 64 squares as one mesh | yes |
| **Player Blocker** | the solid box keeping players without a seat out of the table ([Physical pieces](physical-pieces.md#walking-through-the-board-step-95a2)) | keep it; resize its `BoxCollider` to a table of your own |
| **Pieces**, **Square Targets**, **Highlights**, **Promotion Ghosts** | the pieces, desktop mode's click targets, the highlights and the promotion ghosts ([Promotion pieces](#chess-piece-sets-steps-123-and-1210)) | no: the view uses them |
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

## Chess piece sets (steps 12.3 and 12.10)

The pieces' models come from **chess piece sets**: a `ChessPieceSet` behaviour on an object holding one template per piece type (pawn, knight, bishop, rook, queen, king). A set is one side's pieces, with no colour of its own: White and Black each wear a set, chosen and swapped independently, and any set can be worn by either side (both at once included). Any models will do, whatever way they face, whatever their size, wherever their origin and however many material slots they have: each is set up by moving, turning and scaling its template in the editor, where it can be seen.

**The default sets.** **Create Board View** puts two sets under the Chess Arbiter, **White Pieces** and **Black Pieces** (from the placeholder models in `Assets/Siggi/ChessBoard/Models/`, with the White Pieces and Black Pieces materials), and assigns them to the arbiter's **Default White Piece Set** and **Default Black Piece Set** (under **Pieces**). Every board's White wears the first and Black the second unless the board has a set of its own for that side. Neither field may be empty: a build is cancelled while one is (with a dialog saying how to fill it). (An arbiter from before step 12.10 had one combined set, **Chess Pieces**; the builder removes it.)

**Making a set.** Duplicate **White Pieces** (or build your own the same way) and change its templates:

- It has a slot per piece type (**Pawn** … **King**), and behind them a second row for the promotion pieces (below), each holding a **Guide** (a green square frame, one square of the board) and a **Model** (the template). The slot stands for the square: its centre is the square's centre, its y = 0 the playing surface.
- For each **Model**, put your mesh in its `MeshFilter` and your materials in its `MeshRenderer`, one per slot of the mesh.
- Then move, turn and scale the **Model** under its slot until the piece stands upright on the guide at the size you want, its base on the guide's surface. That local position, rotation and scale is what every piece of that type gets on the board (in the board view's units, so they scale with the view).
- Set it up as White's pieces, facing Black (+z, the knight looking along the slot's blue axis). When Black wears the set, the board turns its pieces half round, so its knights face White: one set serves either side with no setup per side.
- Light and dark versions of a design are two sets (duplicate the set and change the materials), so a world can offer, say, a supporter set in both colours, and let White wear one and Black the other.
- Keep the slots themselves unscaled and unturned so what you see in the editor is what the board shows: only each **Model**'s own transform is read. The set's position in the world doesn't matter.
- The set's own object must stay active; it hides its slots when the world starts, and the boards go on reading them.
- The set's fields (**Pawn** … **King**, **Promotion Knight** … **Promotion Queen**) point at the templates: after duplicating a set, check that they point at the copy's own **Model**s.

**Promotion pieces** (step 12.11). When a player drops (or click-places) a pawn on the last rank, see-through pieces float in a row above it, a knight, bishop, rook and queen, and clicking one promotes to it. They come from the second row of slots in the promoting side's set, **Promotion Knight** … **Promotion Queen**, set up exactly as the pieces are (mesh, materials, and the **Model**'s position, turn and size on its guide, as White's piece). The default sets use each type's regular mesh with one see-through green material, **Promotion Ghost** (in `Assets/Siggi/ChessBoard/Models/`), on every slot; give your set's promotion models a see-through material of your own, or the same one. Each side's ghosts come from its own set, so White's and Black's can look different. A set missing one of them is named in the log when it's applied, as a missing piece template is. **Create Board View** adds the promotion slots to the arbiter's default sets when they lack them (an arbiter from before step 12.11), copying each type's mesh and transform from the set's own piece, and keeping everything else; for a set of your own, add them by hand or let the builder add them by making it a default and running it.

**Assigning a set.** Put it in the arbiter's **Default White Piece Set** or **Default Black Piece Set** for every board, or in a board view's **White Piece Set** or **Black Piece Set** (on `ChessBoardView (<board>)`) for that board only. Each side is on its own: a view can set White's and follow the arbiter's default for Black.

**Changing sets at run time,** for example pieces only a world's supporters see: from your own UdonSharp script, call `ChessBoardView.SetChessPieces(color, set)` to give one board's side a set (`color` is `ChessRules.White` or `ChessRules.Black`; `null` goes back to the arbiter's default for that side), or `ChessArbiter.SetDefaultChessPieces(color, set)` to change a side's default for every board without a set of its own for that side (`null` is refused). `CurrentPieceSet(color)` says which set a side wears. Both change only what the local player sees: nothing is synced, so each player can see their own sets. Only that side's pieces change, in place, at once: those on the board, in a hand, travelling, captured, promoted, and a fallen king.

**Players choosing their own sets.** Since the sets are local, a world that lets each player pick the set their pieces wear syncs the choice itself and calls `SetChessPieces` on every client. For example, with 5 supporter sets, each of two players picks one, and White wears one player's choice and Black the other's:

- Each player's choice is a synced number in your own behaviour (an index into your array of sets), one per player (for example a behaviour per player, owned by that player, or a synced array keyed by player).
- A `ChessBoardListener` of your own, added to the board (`board.AddListener(this)`), notices who sits where: in `OnBoardChanged()`, read `board.whitePlayer` and `board.blackPlayer` (display names, `""` for an empty seat), and after a choice arrives (`OnDeserialization`) do the same.
- Then call `view.SetChessPieces(ChessRules.White, setChosenBy(whitePlayer))` and the same for Black, with `null` for an empty seat or a player who chose nothing (the arbiter's default). Calling it with the set already worn costs little, but you can remember what you last set and skip it.

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
