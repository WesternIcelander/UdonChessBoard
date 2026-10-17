# 2D board view

A flat board on a world-space canvas showing one board's position at a time, with left / right buttons to choose which board (step 12.13). It's for watching: spectators across the room, or a wall showing every game in turn. Nothing on it acts on a board: the selector is its only input.

## What it shows

- **The position the board shows**, replay included (`PieceAt`), White at the bottom, each piece as a sprite. The last move's two squares are tinted (`LastMove`), and a king in check is red (`Checks`, in the position shown, so not in a replay position without check).
- **Which board:** `Board N`, numbered from 1 as the settings menu numbers them (`boardNumber + 1`).
- **Its players and status lines** exactly as the board's UI writes them (the status display, [Physical pieces](physical-pieces.md#status-display-step-95c-on-chessboardui-since-121)): the seats with their clocks (`GG` after a flag fall, step 12.9), whose turn, check, the result, offers and requests, a loaded game's names, the replay position.

## Choosing the board

- **`boards`:** set in the Inspector, in the order the buttons step through (as the settings menu's). The builder fills it with every board in the scene by board number.
- **Stepping:** left and right step to the previous or next board that isn't reset (`LifecycleReset`), wrapping round and skipping reset ones. The board shown isn't left when it's reset. With no other board in use, both buttons do nothing. It starts on the first board.
- **Synced** (Inspector tick): on, the board chosen is a synced field and a press takes ownership of the view, so everyone sees the same board, late joiners included. Off, each player chooses for themselves and nothing is sent.

## Implementation

`ChessBoard2DView` (`Assets/Siggi/ChessBoard/ChessBoard2DView.cs`), a `ChessBoardListener`.

- **Listening, not polling** (decided in 12.13): the view listens to the board it shows only, moving its listener (`RemoveListener` on the old board, `AddListener` on the new) when it steps, so boards nobody looks at don't tell it of their changes. Other boards' lifecycles are read only when a button is pressed. It draws on the frame after a change (`Refresh`, public for tests), as the other views do, and brings its texts up to date every `ChessBoardUI.StatusInterval` while a clock runs and once a second otherwise, since the clocks run without the board changing. `OnNotationFilled` redraws the texts (the replay line may name a move it didn't know).
- **The texts** come from static forms of the UI's status methods, taking the board (`ChessBoardUI.PlayersTextFor`, `PlayerTextFor`, `StatusTextFor`, `EndingTextFor`, `ReplayTextFor`), so the two never differ. The UI's instance methods (`PlayersTextAt` and the rest) forward to them. No getter had to be added to `ChessBoard`: `PieceAt`, `LastMove`, `Checks` and the public state were enough.
- **Stepping:** `PressLeft` / `PressRight` call `Step(direction)`, which asks `StepTarget(inUse, current, direction)`, a static method over which boards are in use, so the rule is checked with any number of boards. `Show(index)` switches board (public for tests); `ShownIndex()` and `ShownBoard()` say which is shown.
- **Sync:** manual sync with one synced field, `syncedIndex`. With **Synced** on, a step takes ownership of the view (`Networking.SetOwner`; anyone may, there's no `OnOwnershipRequest`), sets the field and calls `RequestSerialization`; `OnDeserialization` shows the board it names. With it off the field is never sent and arriving data is ignored. Each switch is logged (`[ChessBoard2DView] <view>: showing Board N (<why>)`: on start, chosen here, chosen here and sent, or chosen by the owner) for comparing clients' logs. A late joiner starts on the first board and switches once the synced field arrives (about 12 s after joining in the 12.13 run). See [Networking](networking.md) and [Ownership](ownership.md).
- **Sprites:** `whiteSprites` and `blackSprites`, 6 each, pawn to king. The defaults are the PNGs in `Assets/Siggi/ChessBoard/Sprites2D/` (`PawnW.png` … `KingB.png`); swap them in the Inspector for another set. Each square has a background `Image` (`squareImages`) and a piece `Image` (`pieceImages`, aspect kept, disabled on an empty square). The square colours (`lightSquare`, `darkSquare`, `lastMoveTint`, `checkTint`) are Inspector fields.
- **Builder:** **Tools > Chess Board > Create 2D Board View** (`Editor/ChessBoard2DViewBuilder.cs`) builds the canvas (with `VRCUiShape` and a trigger collider over it, so the pointer hits it and players walk through it): the selector row (`<`, `Board N`, `>`), the 8×8 board of 100-pixel squares, and the players and status texts under it, at 0.001 scale (0.88 m wide). It sets the sprites' import type to Sprite when it isn't. A new view stands 2.5 m behind the boards (along +z), facing them; rebuilding replaces the selected view, or the scene's only one, where it stands, keeping its `boards` and **Synced**.
- **The debug view** uses the same sprites since 12.13, in place of its FEN letters (`pieceImages`, `whiteSprites`, `blackSprites`, set by its builder from the same folder through `ChessBoard2DViewBuilder.LoadPieceSprites`).

## Testing

`Board2DTest` (`Assets/Tests/Board2D/`), built by **Tools > Chess Board > Create 2D View Test** for the scene's 2D view; see [Testing](testing.md#2d-view-test).
