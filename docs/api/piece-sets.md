# Piece sets

A step-by-step guide to a chess piece set of the world's own: preparing the models, making the set from the package's **Chess Pieces**, setting up each piece, adding it to the world and checking it, then letting players choose it from the world's own scripts. Unlike the other API documents it's mostly editor work; only the last section is calls. Read the [API index](README.md) first for the numbers (colours, piece types) and how a board's change reaches every client. The design behind the sets is in [physical pieces](../physical-pieces.md#piece-sets-steps-123-1210-1211-and-136).

A **piece set** is a `ChessPieceSet` behaviour on an object holding one **template** per colour and piece type, plus the **promotion pieces** (the see-through pieces a player picks from when a pawn reaches the last rank). A template is an object with a `MeshFilter` and a `MeshRenderer`: the board view copies its mesh, its materials and its own local position, rotation and scale onto every piece of that colour and type. The arbiter lists the world's sets, and each board syncs which of them White and Black wear.

## 1. Preparing the models

- **One mesh per piece type:** pawn, knight, bishop, rook, queen, king. The promotion pieces may use the same meshes (the package's do) or meshes of their own. Black may use White's or its own (step 4).
- **Facing, size and origin don't matter.** The model may face any way, be of any size and have its origin anywhere, even off its base: the template's transform fixes all of that in the editor (step 3). It may have any number of material slots (sub-meshes).
- **Keep the mesh to the piece.** The view reads the mesh's **bounds** (its box in the mesh's own space) for two things: the `BoxCollider` fitted to each piece and each promotion ghost, which is the grab box a hand and a desktop click reach, and the lift of a checkmated or resigning king lying on the board, which keeps its lowest corner at the height it had standing. A stray vertex, a base plate or a hidden helper far from the piece makes the grab box too big and a fallen king float. Remove them before exporting.
- **No colliders of the model's own.** The view fits its own; colliders on the template aren't copied.
- **One object per piece.** Only the template's own `MeshFilter` is read, not its children's: a piece made of several objects is merged into one mesh first (in the modelling program, or one mesh with several material slots).
- **Import settings that matter** (select the model file, **Model** tab):
  - **Scale:** the board's squares are 0.05 units wide in the view's own space (the view's `squareSize`; a view scaled up scales the pieces with it), so a pawn is about 0.035 units tall. Either set **Scale Factor** so the model comes in at that size, or leave it and scale the template (step 3). Either works; scaling the template keeps the import as the modeller made it.
  - **A model from Blender lying on its back:** Blender's z-up comes into Unity as a −90° turn on the imported object, not in the mesh. The template takes the mesh alone, so the piece lies on its back on the guide: turn the template (90° about x), or tick **Bake Axis Conversion** before taking the mesh.
  - **Read/Write** isn't needed: the bounds are there without it.
- **Materials and shaders** that work in VRChat: Unity's built-in render pipeline, so **Standard** or a shader written for it (VRChat's own, **VRChat/Mobile/…**, or one made for VRChat worlds); a shader the build can't use shows as pink. A world built for Android (Quest and phones) as well wants light shaders there, such as VRChat's **VRChat/Mobile** ones, with a see-through one for the promotion pieces, and few transparent materials.
- **Keep it light.** A board shows 32 pieces, each its own renderer, and every board in the world shows a full set: 4 boards are 128 pieces. Keep each piece to a modest triangle count (a few thousand at most, far fewer for Android), few material slots, and share materials and textures between the pieces of a side.

## 2. Making the set

1. Drag the **Chess Pieces** prefab (`Assets/Siggi/ChessBoard/Prefabs/`) into the scene.
2. Right-click it in the Hierarchy and choose **Prefab > Unpack Completely**.
3. Rename it (the name is what the view's log names it by, below).

**Unpack it, don't make a prefab variant.** A variant follows the package's **Chess Pieces** wherever it doesn't override it, so a package update could change the world's pieces (a Black slot left to the fallback would take a mesh the package later gives its own default). An unpacked copy is the world's alone, and no update touches it. To reuse the set in other scenes, drag the unpacked set into the world's own folder of assets, making a prefab of the world's own.

**Or start from nothing:** add a `ChessPieceSet` to an empty object and give it a child per template (any layout; the slots below are a help, not a requirement), each with a `MeshFilter` and `MeshRenderer`, set up as step 3 says and assigned to the set's fields.

**Where it lives:** anywhere in the scene, under the **Chess Arbiter** or not; its position, rotation and scale in the world don't matter. **Keep the set's own object active**: on Start it hides its children (the slots, templates and guides), and the board views go on reading the hidden templates. Sets hidden this way cost nothing to render.

**What's in it.** Each slot is an object named after its template (**White Pawn** … **White King**; behind them **White Promotion Knight** … **White Promotion Queen**; behind those **Black Pawn** … **Black King**), holding a **Guide** (a green square frame, the size of one square of the board) and a **Model**, the template. The set's 20 fields point at the templates: **White Pawn** … **White King**, **White Promotion Knight** … **White Promotion Queen**, **Black Pawn** … **Black King** and **Black Promotion Knight** … **Black Promotion Queen** (`whitePawn` … `blackPromotionQueen`). **Chess Pieces** has no Black promotion slots: those fields are empty, and Black's promotion pieces are White's (step 5). After unpacking, check that each field points at this set's own **Model**: select the set and click each field, which highlights its object in the Hierarchy. The same check after duplicating a set of the world's own.

## 3. Setting up each White template

For each of White's six types and its four promotion pieces, select the slot's **Model**:

1. **Mesh:** drag the piece's mesh into its `MeshFilter`'s **Mesh**.
2. **Materials:** in its `MeshRenderer`, one material per slot of the mesh (**Materials** has as many entries as the mesh has sub-meshes).
3. **Transform:** move, turn and scale the **Model** until the piece stands upright on the **Guide**, its base on the guide's surface and centred in the frame, at the size wanted beside the guide's one square. White faces Black: **+z**, the slot's blue axis (with the **Move** tool in **Local** mode); for the knight, its head looks along it.

**What that transform becomes on the board.** The slot stands for a square: its centre is the square's centre, its y = 0 the playing surface. The **Model**'s local position, rotation and scale under its slot are what every piece of that type gets on the board: its offset from the square's centre, its turn and its size, in the board view's local units, so they scale with the view. The same offset applies on the captured-piece places beside the board. Only the **Model**'s own transform is read: **keep the slots themselves unscaled and unturned** (and the set too, if the slots are to look as the board will), or what the editor shows isn't what the board shows. A slot's position doesn't matter; they're spread out to be seen side by side.

The view lifts a selected piece half a square, tips a fallen king about its base, and turns each piece back to its template's turn whenever it's placed (so a piece a hand turned stands straight again on its square). None of that needs anything from the set.

## 4. Black's three ways

For each type on its own, Black's **Model** decides:

| Black's **Model** | Black's piece |
|---|---|
| **Materials only:** no mesh (no `MeshFilter`, or its **Mesh** empty), a `MeshRenderer` with Black's materials | White's mesh and transform, turned half round to face White, in Black's materials. How **Chess Pieces** does it, for a set whose sides differ only in colour. Black's materials are one per slot of **White's** mesh. |
| **Nothing:** no mesh and no materials (or no `MeshRenderer`, or the **Model** and its field gone) | White's mesh, transform and materials, turned to face White: the same look for both sides. |
| **A model of its own:** its own mesh and materials | Used exactly as set up, not turned: set it up on its guide **facing White, −z**. For a design whose Black pieces differ in shape. |

The half turn is about the vertical through the square's centre, so a White template's base point stays on the square for Black too, wherever its origin is.

## 5. The promotion pieces

When a player drops (or click-places) a pawn on the last rank, four see-through pieces float in a row above it, a knight, bishop, rook and queen, and clicking one promotes to it. They come from the promotion templates, set up exactly as the pieces are (mesh, materials, and the **Model**'s position, turn and size on its guide).

- **White's four are required** (as White's pieces are). **Black's are optional** and fall back the same way as Black's pieces (step 4): **Chess Pieces** has none, so Black's ghosts are White's, turned to face White.
- **A see-through material:** the package's **Promotion Ghost** (`Assets/Siggi/ChessBoard/Models/`, Standard, Fade, green), on every slot of the mesh, or the set's own. A solid material works but looks like a piece standing in the air.
- **The row floats over the board:** the ghosts' bases 1.5 squares above the playing surface over the promotion square's rank, 1 square apart, the row's middle kept within 2 squares of the board's centre (`ChessBoardView.GhostHeight`, `GhostSpacing`, `GhostMaxOffset`); the template's offset is added to that. A set much taller than the package's (whose king is about 1.4 squares) may want its promotion templates smaller than its pieces, so the row clears the pieces under it, and a much wider one so neighbours don't overlap.

## 6. Adding it to the world

Select the **Chess Arbiter** and, under **Pieces**, add the set to **Piece Sets** (`pieceSets`).

- **Its place in the list is its index**, the number the calls below and every board's sync use. **Index 0 is the default**: every board starts with it, and every reset puts both sides back to it. The arbiter prefab lists **Chess Pieces** alone; add the world's sets after it, or put one first to make it the default.
- **No empty entries.** A VRChat build or upload is cancelled while the list is empty or has an empty entry, with a dialog and an error naming the arbiter (the build check, `ChessArbiterBuildCheck`).
- **Reordering changes what an index means.** A world's script naming index 2 means whatever is third in the list; a board's synced index is read against the list of the world as built. Change the scripts with the list.

## 7. Checking it

In Play Mode (ClientSim), put the set on a board:

- **Through an override:** a test button of the world's own calling `arbiter.SetPieceSetOverride(ChessRules.White, index)` and the same for Black, which shows it on every board at once on this client ([below](#letting-players-choose)); `-1` clears it.
- **Through a preferred set and a join:** a button calling `arbiter.SetPreferredPieceSet(index)`, then joining a board as White with no one as Black: the board puts the set on both sides.

Then look at it from each side: every piece standing on its square, its base on the surface, White's facing Black and Black's facing White; a capture standing on its captured place as on a square; a pawn promoted (the four ghosts above it, clear of the pieces); a game resigned (the king lying on the board, not floating). Selecting a piece of the view in the Hierarchy during Play Mode shows its `BoxCollider`, the grab box, in the Scene view.

**What the view logs** (the Console, and the Editor log), when a side's set is put on its pieces (on Start, and on each change of set):

| Log line | Means |
|---|---|
| `[ChessBoardView] <view>: chess piece set <set>: no White knight template.` | A White field (here **White Knight**) is empty. Also `no White promotion rook template` for a promotion field. |
| `[ChessBoardView] <view>: chess piece set <set>: the White knight template has no mesh.` | Its `MeshFilter` is missing or its **Mesh** empty. |
| `[ChessBoardView] <view>: chess piece set <set>: the White knight template has no MeshRenderer.` | Its `MeshRenderer` is missing. |
| `[ChessBoardView] <view>: no chess piece set for White: add one to the Chess Arbiter's Piece Sets …` | The arbiter has no set at the index (an empty list or entry). |
| `[ChessBoardView] <view>: the board's piece set 3 for Black isn't in the Chess Arbiter's Piece Sets (2); showing the first.` | A board synced an index this client's list doesn't have (a warning). |

Only the first problem of a set is named; fix it and play again. The pieces of a type whose template is missing keep the model they had. Black's templates are optional, so nothing is logged for them. A set is only checked when it's put on a board, so put each of the world's sets on one.

**Troubleshooting:**

- **A piece floats above the board or is sunk into it:** its base isn't on the guide's surface: move its **Model** up or down. Under a scaled or turned slot, it may look right in the editor and not on the board: unscale and unturn the slot.
- **A piece faces the wrong way:** White's face +z, Black's own models −z; turn the **Model** about its y. A Black piece in White's mesh faces White by itself.
- **A piece lies on its back or stands on its head:** the model came in turned (Blender's z-up): turn the **Model** 90° or 180° about x.
- **Black looks like White:** Black's **Model** has no materials (it took White's, step 4): give its `MeshRenderer` Black's materials. A Black model of its own with no `MeshRenderer` keeps whatever materials the piece wore before.
- **Some of a piece in the wrong material, or missing:** the materials don't match the mesh's slots: one per sub-mesh, in order (and for Black's materials only, one per slot of White's mesh).
- **A pink material:** its shader doesn't work in this build (a shader for another render pipeline, or one that doesn't compile for the platform): use Standard or a VRChat shader.
- **A grab box far too big, or a fallen king floating:** the mesh has geometry far from the piece (step 1). Its bounds are the box; remove the stray parts and import again.
- **A piece far too big or tiny:** the import's scale and the template's multiply; set one of them.
- **The set's slots and guides show in the world:** its `ChessPieceSet` component is disabled, so its Start didn't run to hide them: enable it.
- **The board doesn't show the set:** it's not the set the arbiter lists at that index, or the board wears another index (a reset puts both sides back to 0).

## Letting players choose

No UI of the package's changes a board's sets: a world gives players its own (a button per set, a pedestal, sets for the world's supporters), calling these from its UdonSharp scripts.

| Call | Does |
|---|---|
| `arbiter.SetPreferredPieceSet(index)` | Makes the set at `index` the local player's preferred set; `-1` for none (the default). `false` (and nothing changed) for an index outside the list. Not synced: it's applied when the local player **next joins** a board (`JoinWhite`, `JoinBlack`, `Join`): on both sides when the other seat is empty, otherwise on the joined colour alone, in the same change as the join (told as `ChangeJoined`), whoever may change sets otherwise. Two players who each chose a set then play White in one's and Black in the other's. `arbiter.PreferredPieceSet()` (or the field `preferredPieceSet`) reads it. |
| `board.SetPieceSet(color, index)` | Makes `color` (`ChessRules.White` or `Black`) wear the set at `index` on this board, for everyone, as the local player: `InputAccepted` (applied here at once, sent, told as `ChangePieceSet`; the set it already wears is accepted with nothing told) or `InputRejected` for another colour, an index outside the list, or a player who may not take the board over: allowed for an organizer, a seated player, anyone while a seat is empty or its player has left the instance, anyone once the board is idle enough to reset, and on a loaded board only its loader and organizers ([ownership](../ownership.md)). `board.CanSetPieceSet(color, index)` asks the same without changing anything. |
| `arbiter.SetPieceSetOverride(color, index)` | Shows the set at `index` for `color` on **every board, on this client only**, whatever the boards have synced (a player who finds a set hard to read); `-1` shows the boards' own again. `false` for another colour or an index outside the list. Applied at once. `arbiter.PieceSetOverride(color)` reads it (`-1` for none). |

**Reading what a side wears:**

- `board.PieceSetIndex(color)`: the index the board has synced for a side (0 after a reset), what everyone sees unless they have an override; `-1` for another colour.
- `view.CurrentPieceSet(color)` (on a `ChessBoardView`): the `ChessPieceSet` this client shows for a side: the override if there is one, otherwise the board's, or the first for an index the list doesn't have.
- `arbiter.PieceSet(index)` (`null` outside the list), `arbiter.PieceSetCount()` and `arbiter.IsPieceSetIndex(index)` read the list; a set's own name is `set.gameObject.name`.

**Noticing a change:** a board's sets change with `ChangePieceSet` (`SetPieceSet` here or on another client), `ChangeJoined` (a join bringing a preferred set), `ChangeReset` and `ChangeLeft` (a reset, or the last seat of a lobby left, putting both back to 0), and with any update from another client, so a display of a board's sets compares `PieceSetIndex` with what it last drew in `OnBoardChanged` rather than looking at `lastChange` alone ([board input](board-input.md#being-told-of-changes)). `ChangePieceSet` moves nothing and doesn't end a promotion waiting for its piece. An override isn't told to listeners: it changes only when the world's own script sets it.

Only that side's pieces change, in place and at once: on the board, in a hand, travelling, captured, promoted, and a fallen king.

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// A pedestal for one piece set: pressing it makes the set the local player's choice, put on the boards they join
// from now on and, where they already sit, on their side at once.
public class PieceSetPedestal : UdonSharpBehaviour {
    public ChessArbiter arbiter;
    public ChessBoard[] boards;
    public int pieceSet = 1;

    public override void Interact() {
        if (!arbiter.SetPreferredPieceSet(pieceSet)) return;
        string me = Networking.LocalPlayer.displayName;
        for (int i = 0; i < boards.Length; i++) {
            ChessBoard board = boards[i];
            if (board.IsSeated(ChessRules.White, me)) board.SetPieceSet(ChessRules.White, pieceSet);
            if (board.IsSeated(ChessRules.Black, me)) board.SetPieceSet(ChessRules.Black, pieceSet);
        }
    }
}
```

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// A toggle showing the default set on every board for the local player alone (an easy-to-read set), and a label
// naming what one board's White wears.
public class PieceSetPanel : ChessBoardListener {
    public ChessArbiter arbiter;
    public ChessBoard board;
    public ChessBoardView view;
    public TMPro.TextMeshPro label;

    void Start() {
        board.AddListener(this);
        OnBoardChanged();
    }

    public void ToggleEasyToRead() {
        int index = arbiter.PieceSetOverride(ChessRules.White) == 0 ? -1 : 0;
        arbiter.SetPieceSetOverride(ChessRules.White, index);
        arbiter.SetPieceSetOverride(ChessRules.Black, index);
        OnBoardChanged();
    }

    public override void OnBoardChanged() {
        ChessPieceSet set = view.CurrentPieceSet(ChessRules.White);
        label.text = set != null ? "White: " + set.gameObject.name : "";
    }
}
```
