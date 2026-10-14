# Customizing a board

What a world creator can change on a board built by **Tools > Chess Board > Create Board View**, without touching the code.

## The board model and scale (step 12.2)

A view (`ChessBoardView (<board>)`) holds these children:

| Child | What it is | May it go? |
|---|---|---|
| **Table** | the placeholder table | yes |
| **Board**, **Squares** | the placeholder board: its frame, and the 64 squares as one mesh | yes |
| **Player Blocker** | the solid box keeping players without a seat out of the table ([Physical pieces](physical-pieces.md#walking-through-the-board-step-95a2)) | keep it; resize its `BoxCollider` to a table of your own |
| **Pieces**, **Square Targets**, **Highlights**, **Promotion Picker** | the pieces, desktop mode's click targets, the highlights and the picker | no: the view uses them |

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

## Chess piece sets (step 12.3)

The pieces' models come from a **chess piece set**: a `ChessPieceSet` behaviour on an object holding one template per piece type and colour. Any models will do, whatever way they face, whatever their size, wherever their origin and however many material slots they have: each is set up by moving, turning and scaling its template in the editor, where it can be seen.

**The default set.** **Create Board View** puts a set named **Chess Pieces** under the Chess Arbiter (from the placeholder models in `Assets/Siggi/ChessBoard/Models/`) and assigns it to the arbiter's **Default Piece Set** (under **Pieces**). Every board wears it unless the board has a set of its own. The field must not be empty: a build is cancelled while it is (with a dialog saying how to fill it).

**Making a set.** Duplicate **Chess Pieces** (or build your own the same way) and change its templates:

- It has a slot per piece (**White Pawn** … **Black King**), each holding a **Guide** (a green square frame, one square of the board) and a **Model** (the template). The slot stands for the square: its centre is the square's centre, its y = 0 the playing surface.
- For each **Model**, put your mesh in its `MeshFilter` and your materials for that colour in its `MeshRenderer`, one per slot of the mesh. White and Black may use different meshes.
- Then move, turn and scale the **Model** under its slot until the piece stands upright on the guide at the size you want, its base on the guide's surface. That local position, rotation and scale is what every piece of that type and colour gets on the board (in the board view's units, so they scale with the view). Black's knights usually face White (−z): the default set turns Black's models half round.
- Keep the slots themselves unscaled and unturned so what you see in the editor is what the board shows: only each **Model**'s own transform is read. The set's position in the world doesn't matter.
- The set's own object must stay active; it hides its slots when the world starts, and the boards go on reading them.
- The set's fields (**White Pawn** … **Black King**) point at the templates: after duplicating a set, check that they point at the copy's own **Model**s.

**Assigning a set.** Put it in the arbiter's **Default Piece Set** for every board, or in a board view's **Piece Set** (on `ChessBoardView (<board>)`) for that board only; an empty **Piece Set** uses the arbiter's.

**Changing sets at run time,** for example pieces only a world's supporters see: from your own UdonSharp script, call `ChessBoardView.SetChessPieces(set)` to give one board a set (`null` goes back to the arbiter's), or `ChessArbiter.SetDefaultChessPieces(set)` to change the default for every board without a set of its own (`null` is refused). Both change only what the local player sees: nothing is synced, so each player can see their own set. The pieces change in place, at once.
