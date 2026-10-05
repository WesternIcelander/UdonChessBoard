# UdonSharp Port Plan

Plan for porting the Java ChessBoard library to UdonSharp, to run in VRChat worlds. Nothing has been written yet; this records the design agreed so far.

## Project layout

This file will live at the root of the Unity project:

```
<Unity project>/
  Assets/            UdonSharp scripts, prefabs, scenes
  Packages/
  ProjectSettings/
  JavaChessBoard/    copy of the Java library (reference implementation)
    pom.xml
    src/main/java/io/siggi/chessboard/...
  PLAN.md
```

Unity only imports what's under `Assets/` and `Packages/`, so `JavaChessBoard/` sits alongside without being imported. Java paths in this plan (e.g. `MoveFinderPiece.java`) are under `JavaChessBoard/src/main/java/io/siggi/chessboard/`.

## Goals

- A chess library written entirely in UdonSharp, for use in VRChat worlds. Running in VRChat is the primary target, not a later adaptation; there is no separate plain-.NET version.
- The Java library stays as the reference implementation for behaviour (move generation, notation, FEN/PGN), not as code to mirror structurally.
- Where the port has an equivalent of a Java name (fields, options, concepts), keep the Java name, e.g. `prohibitSelfCheck`.
- Main use is live play. PGN import is rare: loading a saved game into replay mode.

## Udon constraints that shape the design

UdonSharp compiles only part of C#. The ones that matter here:

- No custom classes except `UdonSharpBehaviour` subclasses, which are components on GameObjects (no `new`; they must be instantiated or pre-placed).
- No interfaces, no generic collections (`List<T>`, `Dictionary<K,V>`); only arrays and VRChat's `DataList`/`DataDictionary`.
- No `try`/`catch`/`finally`/`throw`.
- Recursion requires `[RecursiveMethod]`.
- No file or stream I/O.
- No static fields, no delegates or lambdas.
- Supported: user enums, static methods, extension methods, properties, `out`/`ref`, inheritance between behaviours with virtual methods, `uint`.
- `Regex` is exposed. It is .NET's regex engine, not Java's (see Consequences).
- `System.Text.StringBuilder` is available from VRChat SDK 3.7.1, so text building (PGN and FEN output, move notation) can follow the Java. The project's minimum SDK version is therefore 3.7.1.
- The Udon VM is far slower than normal C#, and calls into *another* behaviour (field reads, method calls) are much more expensive than local array access.

### Consequences

- No exceptions: errors become return codes / `bool` + `out` values (e.g. PGN import reports "failed at move N").
- Regex: the Java patterns in `PGN` and `MovePredicates` need two changes for .NET.
  - `TOKEN_PATTERN` uses possessive quantifiers (`*+`), which .NET doesn't support; use atomic groups `(?>...)`.
  - Java's `matcher.matches()` requires the whole string to match; in .NET add `^...$` anchors.
  - With no static fields, compiled `Regex` objects live in instance fields of the behaviour that uses them.
  - Regex is used only when parsing PGN or FEN, never in normal play, so its speed doesn't matter. Import already spreads its work across frames with a time budget.
  - Normal play must stay regex-free. Input needs no parsing (see Player input), but notation *output* does in the Java: `RegularMove.notation` calls `MovePredicates.parse` (regex) on up to four candidate spellings to choose the shortest unambiguous one. The port should work that out directly instead (see Legality checking).
- No I/O: PGN and FEN are string in, string out.
- No static lookup tables: use `const`s, computed lookups, or instance fields.
- No lambdas: `removeIf`, `Predicate<Move>` and `Lazy` become plain loops and flags.
- `Square`, `Piece`, `PieceType`, `PieceColor`, `File`, `Rank` become plain C# enums or ints, with `-1`/`None` in place of `null`. Their Java fields (`Square.x`, `Piece.color`, ...) become static helper methods.

## Architecture

- **Moves are packed `int`s**, not objects. A move list is a preallocated `int[]` plus a count. No allocation, no pooling, and every field read is a shift and mask.
- **Pure logic as static methods.** Move encoding, square maths, move generation and attack checks take the board's arrays as parameters. Static methods can be called across classes, so the rules live in one place and every board calls them directly, with no cross-behaviour calls. With no static fields, they hold no state of their own: everything comes in through parameters.
- **Behaviours only for long-lived things**: each board's game state, UI, networking, the PGN importer. There are few of them and they live for the whole session, so the cross-behaviour cost doesn't matter.
- **Scratch boards** (what `ScratchBoard` does in Java) are a small fixed set of extra piece arrays inside the board script, since legality checking only nests a few levels deep.

Pooled `UdonSharpBehaviour` move objects were considered and rejected: each field read is a cross-behaviour call, and there's no `finally` to guarantee objects are returned. Revisit only if packed ints turn out to be too much of a hassle in practice.

### Logical board and physical board

Each board is split into two behaviours that never reach into each other's domain.

- **`ChessBoard` (logical):** game state, rules, synced fields, ownership, logging. It never touches the world: no transforms, no pickups, no GameObjects.
- **`ChessBoardView` (physical):** the pieces, buttons, highlights and other objects in the world, and the events from them (pickups, drops, button presses). It holds no game state and no synced fields of its own.

**Logical → physical: change notification.**
- Udon has no C# events or delegates, so the board keeps a reference to its listener(s) and calls a method on them when its state changes: `OnBoardChanged()`.
- So the board doesn't depend on the concrete view class, listeners derive from an abstract `ChessBoardListener` base behaviour with virtual methods (UdonSharp supports inheritance with virtual methods; interfaces aren't available). The board holds an array of them, so a move-list display or clock can listen too.
- The view then reads what it needs from the board through public getters: piece on each square, last move, mode (game/replay), result, whose turn, pending requests, which buttons apply.
- A "last change" value (move played, reset, entered replay, loaded game...) lets the view animate a move rather than snapping every piece.
- The same path serves remote updates: `OnDeserialization` updates the board's state, and the board notifies its listeners exactly as for a local change.

**Physical → logical: input forwarding.**
- The view turns world events into calls on the board: `PiecePickedUp(square)`, `PieceDropped(from, to)`, `KingDroppedOnCastleSpace(side)`, and one method per button (resign, offer draw, claim draw, enter/leave replay, step, request takeback, accept/decline, reset, load PGN).
- The board decides everything: whether the local player may act, ownership, legality, castling and promotion handling, replay free moves, activity timestamps. It returns a result, e.g. accepted, rejected, or "promotion choice needed".
- On rejection, the board notifies listeners as usual and the view puts the piece back from the board state. The view never decides a piece's position itself.
- Promotion: when the board answers "promotion choice needed", the view shows the piece picker and calls again with the chosen piece.

**Benefits:** the logical board can be driven entirely by test behaviours with no world objects (see Testing), and the world presentation can be redesigned without touching the rules.

The same split applies to the instance-wide objects: the archive's history wall and the settings menu are views of `GameArchive` and `ChessArbiter`.

### Physical pieces

- **Other players see a piece while it's being carried.**
- **32 piece objects per board**, one per piece. A side never has more than 16 pieces, since promotion turns a pawn into another piece rather than adding one. Each object swaps its mesh to match the piece the board says it is, so promotion needs no spare queens. Captured pieces are hidden or set beside the board.
- **Carrying is synced with `VRCObjectSync`** on each piece. It handles ownership on grab, interpolation and late joiners. This is presentation-only networking, owned by the view; the board's synced fields stay the single source of truth for the game.
- **At rest, positions come from the board.** After every `OnBoardChanged()`, every client puts each piece on the square the board says. The player who dropped the piece (its sync owner) computes the same position, so all clients agree. To check in testing: that `VRCObjectSync` doesn't fight a non-owner's local snap.
- **Grabbing is gated by the board.** Grabbing a piece takes ownership of the *piece*, which the board's ownership rules don't cover. So the view sets each piece's `VRCPickup.pickupable` for the local player from a board getter ("may the local player touch pieces?"), updated on every change. Bystanders can't grab pieces during a game, but anyone can on a reset board. The pieces' `OnOwnershipRequest` asks the board too, as a second check.
- **Mapping:** the view keeps an array from square to piece object, rebuilt from board state. A move updates it; a reset or load rebuilds it.

## Move encoding (32-bit int)

| Bits | Width | Field |
|---|---|---|
| 0–5 | 6 | from square (king's square when castling) |
| 6–11 | 6 | to square (rook's starting square when castling) |
| 12–14 | 3 | moving piece type (1–6 = pawn…king) |
| 15–17 | 3 | promotion type (0 = none) |
| 18–20 | 3 | captured type (0 = no capture) |
| 21 | 1 | castle |
| 22–25 | 4 | castling rights before the move |
| 26–29 | 4 | en passant before the move: 1 valid bit + 3-bit file |
| 30–31 | 2 | spare |

- Only piece *types* are stored. The mover is the side to move; a captured piece is always the other colour.
- Always mask after shifting (`(move >> shift) & mask`). Then `int` and `uint` behave the same: `>>` on a negative `int` copies the sign bit, but the mask discards it. Bits 30–31 are currently unused anyway.
- The en passant rank isn't stored: it is rank 6 if White is moving, rank 3 if Black is.

### Derived, not stored

```csharp
static bool IsEnPassantCapture(int move) =>
    PieceType(move) == PAWN && PrevEnPassantValid(move) && To(move) == PrevEnPassantSquare(move);

static bool IsDoublePush(int move) =>
    PieceType(move) == PAWN && Math.Abs((To(move) >> 3) - (From(move) >> 3)) == 2;
```

- **En passant capture**: the en passant square is always empty, so a pawn landing on it must be capturing en passant. The captured pawn is on the *from* rank, *to* file.
- **Double push**: sets the new en passant square to the square between from and to.

### Castling

- Castle bit set; *from* = king's square, *to* = rook's starting square ("king takes own rook"). The side follows from whether the rook is right or left of the king. This supports Chess960 and configurable rook files, including castles where the king doesn't move.
- Destinations are fixed: king to g/c, rook to f/d on the back rank.
- Keeping the king in *from* means "the piece on *from* matches the piece-type field" holds for every move, so generic code (highlighting, notation matching) needs no castling special case.
- Undo doesn't need from/to: castling is only legal while the king and rook are unmoved, so their squares are the board's stored starting files. The Java board already stores the rook files (`leftRookFile`/`rightRookFile`, set by `FEN.java`); the port adds a `kingFile`, set by the FEN loader.

### Undo

- Restore the previous castling rights and en passant (bits 22–29) wholesale, instead of the Java's "removes right to castle" and "cleared en passant" fields. This removes the special cases.
- The halfmove clock (fifty-move rule, ~7 bits) isn't in the move, and doesn't need its own history: at any ply it's the number of plies since the last capture or pawn move, which can be read off the move history. If there's none since the start, add the starting position's halfmove clock. So the board syncs the starting halfmove clock and move number as plain ints, and receivers never have to parse the starting FEN.

## Legality checking

The Java checks each candidate by copying the board and generating all of the opponent's replies as `Move` objects; one legal-move list costs roughly a thousand or more moves. Replace with a direct attack check:

1. Play the candidate on the piece array.
2. Check whether the king's square is attacked (look outward for sliders, knights, pawns, the enemy king).
3. Undo; keep the move only if legal.

This also removes the recursion (`getAllLegalMoves` → `isInCheck` → `getAllLegalMoves`).

Notation generation (`RegularMove.notation`) should reuse the already-computed legal move list and disambiguate directly, with no parsing. Find the other legal moves with the same piece type and *to* square. If there are none, no prefix is needed. Otherwise add the from-file if that makes it unique, else the from-rank, else both. Pawn captures always take the from-file.

## Player input

Players pick pieces up and drop them on a square; they never type moves. The view forwards the drop to the logical board as a from/to pair, which the board matches against the legal move list by comparing from and to squares. No notation parsing is involved. Two cases need handling:

- **Promotion**: one from/to pair matches up to four moves (queen, rook, bishop, knight), so the UI has to ask which piece.
- **Castling**: castle moves store the *rook's* square in *to*, so the board translates the drop. The king can castle by being dropped:
  - on its castling destination (g1/c1), when that's unambiguous, or
  - on its own rook (the usual Chess960 convention).
- **Ambiguous castling in Chess960.** If castling moves the king only one square, a normal king move can end on the same square (e.g. king f1, rook h1: castling and `Kg1` both put the king on g1). Then dropping on the destination means the normal move, and castling uses a **castle drop space**: an extra drop target outside the board, the size of a board square, adjacent to the king's castling destination on the outer side of the back rank. The view shows it only while that king is held and the castle is legal, and forwards a drop there as the castle.
  - The same applies when castling doesn't move the king at all (it already stands on g1/c1): dropping on its own square would just put it back, so the castle drop space is used there too.
  - The castle drop space is purely a view concept; the board receives it as "castle on that side".

## Seats, game start and modes

### Board lifecycle

| State | How it's entered | Notes |
|---|---|---|
| Reset | initial; reset (button, timers, organizer) | empty board; "Load PGN" and "Load it here" available |
| Lobby | someone presses Join White or Join Black | seats being filled, mode chosen |
| In game | Start pressed | moves allowed |
| Ended | checkmate, king capture, stalemate, resignation, draw | replay and continuing still available |

Replay mode is entered from a game (in progress or ended) or by loading/importing a game, as described under Replay mode.

- **Leaving the reset state happens on the first join.** That's when the settings snapshot is taken and the inactivity timers start applying.
- **The game ID is created when Start is pressed**, since that's when the game itself begins.

### Seats

- Two buttons on each board: **Join White** and **Join Black**. Pressing one records the player's display name in that seat (synced).
- **The same player may join both seats**, e.g. to practise or analyse alone. Everything that's limited to "the player of that colour" then works for both colours.
- The ownership rule ("while both players are active...") refers to the seated players. With only one seat filled, the rule is about that player alone.
- **There's no leaving a seat.** Once a player has joined, the only way to leave is resetting the board, which clears both seats.

### Starting

- A **Start** button starts the game once both seats are filled.
- **Mode selection** happens in the lobby, before Start: Standard Chess or Chess960 (Fog of War later).
- **Clock settings** are also chosen in the lobby (see Clocks).
- **Any seated player can change any lobby setting** (mode, clock) and press Start, unless an organizer has forced that setting in the settings UI. Forcing works the same way for every lobby setting: a "forced" bit plus the forced value, as instance defaults with per-board overrides, included in the snapshot.

### Clocks

- **Time control:** base time plus an increment per move (e.g. 5+3), or no clock. Shown in the lobby; recorded in the PGN as the standard `[TimeControl "300+3"]` tag (seconds), or `"-"` for no clock.
- **Who chooses:** the players, in the lobby, unless organizers have forced a time control in the settings UI. Forcing is a setting like any other: a "clock forced" bit plus the forced base and increment, as instance defaults with per-board overrides, included in the snapshot.
- **Synced state:** each side's remaining time (milliseconds, `int`) and the server time when the current turn began (`double`, `Networking.GetServerTimeInSeconds()`). Every client computes the running clock from those, so nothing needs syncing while a player thinks.
- **On each move:** the mover's remaining time is reduced by the time since their turn began, the increment is added, and the turn start resets. This is part of the same serialization as the move.
- **Per-move clock history:** the remaining time after each move is stored alongside the move history (`int[]`). This lets a takeback restore the clocks at that point, and lets the PGN include standard `[%clk 0:04:58]` comments.
- **Flag fall:** when a side's time reaches zero, every client can see it. The board's current owner ends the game (once, as with the automatic reset). FIDE rules: the side out of time loses, unless the opponent has no way to checkmate, in which case it's a draw. That exception is a draw rule, so under "No draws" a timeout is always a loss.
- **A player leaving doesn't stop their clock**: their time runs out as normal.
- **The clock doesn't pause in replay.** Replay pauses moves, not time: the player to move keeps using their time while the board is in replay.
- Time forfeit is a new row in Game endings.

### Modes

#### Standard Chess

The standard starting position.

#### Chess960

- When Start is pressed, the starting position is generated randomly from the 960 legal Chess960 positions (bishops on opposite colours, king between the rooks; same setup for both sides). The player pressing Start generates it and syncs the result like any other state.
- The king and rook starting files are set from the generated position; castling uses the Chess960 rules already supported by the move encoding.
- PGN: `[Variant "Chess960"]`, `[SetUp "1"]` and the starting `FEN`.

#### Fog of War (later)

**Deferred: not part of the first version.** The first version offers Standard Chess and Chess960. The rules core still includes the `prohibitSelfCheck` option described below, so Fog of War can be added later without reworking it. Everything else in this subsection is a sketch for that later work.

- **`prohibitSelfCheck` (in the first version):** a flag passed to the rules code, with the same name and meaning as the Java's `Board.prohibitSelfCheck` (default `true`). When `false`, legal-move generation skips the attack-check filter and the castling check conditions, and check/checkmate detection reports nothing. Published perft values only cover normal rules, so test the `false` setting against the Java with `prohibitSelfCheck = false`.

Each player sees only their own pieces, plus the opponent's pieces on squares their own pieces could move to (i.e. could capture).

- **Rules:** there is no check. Moving into check, staying in check, and castling out of or through check are allowed, and **the game is won by capturing the king**. This is what the Java's `Board.prohibitSelfCheck` field is for: set to `false`, it skips all of the check-related filtering, including in `MoveFinderKing`'s castling checks.
- In the port: the legal move list skips the attack-check filter, check/checkmate detection is off, and a king capture ends the game. The move encoding needs nothing new (captured type = king).
- **Notation:** no `+`/`#` suffixes; the final move is the king capture (e.g. `Bxe8`).
- **Visibility is per viewer**, computed locally from the board state: the view hides (doesn't render) pieces the local player can't see, including while they're being carried. A player seated as both colours sees everything.
- Hidden information is only hidden on screen: the board's synced state still contains every piece, so a modified client could reveal it. That's an accepted limitation in VRChat.
- PGN: a custom tag, e.g. `[Variant "Fog of War"]`.

## Replay mode

Players can enter replay mode at any time to review the game.

- **Replay is always shared.** The board is one physical object: when it's in replay mode, everyone sees the replay. During a live game this pauses play, since no game moves can be made until the board leaves replay, but the clock keeps running (see Clocks).
- Entering replay changes synced state, so it needs ownership. While both players are active, only they (or an organizer) can do that (see Ownership), so a bystander can't hijack a live game's board.
- **Free manipulation:** in replay mode players can step through the game, play moves, or move pieces anywhere, including illegal moves.
- **Separate state:** replay never touches the game state. It works on a separate replay board (its own 64-square array) plus the ply it started from, so leaving replay always returns to the game exactly as it was.
- **Stepping** through the game uses the move history: undoing and redoing packed moves gives any earlier position, without the starting FEN.
- **Free moves** are plain piece placement on the replay board: no move generation, no packed moves, no legality checks. The replay board is just an array.
- **Custom flag:** any free move or deviation from the game's moves marks the replay position as *custom*. Stepping back onto a position from the game history clears the flag.

### Continuing from an earlier position (takeback)

- From a replay position that is part of the game history, players can continue the game from there. The move history is truncated to that ply, and the game resumes with that side to move.
- Not allowed from a custom position.
- **Both players must agree.** One player requests continuing from the shown position; the other accepts or declines. The pending request (requesting player and ply) is synced state. A request is cancelled if the replay position changes before it's accepted.
- **Works whether or not the game has ended.**
  - If the game had not ended, it is first logged as an incomplete game (PGN result `*`), with its full move history before truncation.
  - If the game had ended, it was already logged at its end; nothing more is logged at this point.
  - Either way, the continued game is logged in full when it ends.
- **Linking games:** every game gets a unique ID when it starts (see Game IDs). A game continued from replay gets a new ID, and its PGN references the game it came from in custom tags.
- **Instance setting:** whether takeback is allowed at all is configurable by organizers, so it can be turned off for tournaments or other events. It lives on the settings object (see Instance-wide objects).

### Game IDs

- Each game gets a unique ID when it starts, written to its PGN as the custom tag `[GameID "..."]`.
- **Format:** `yyyyMMddHHmmss-xxxxxx`: the time the game started, in UTC, then a random 6-digit number (zero-padded), e.g. `20261005143027-048213`. The suffix makes collisions unlikely between boards starting in the same second, and between instances. The format may be made configurable later for tournaments.
- **Continued games:** a game continued from a replay position gets a new ID and references the original with the custom tag `[ContinuedFrom "<GameID>/<ply>"]`, e.g. `[ContinuedFrom "20261005143027-048213/3"]`. A logging bot can use this to link the two games.
- **Ply numbering:** the number of half-moves played from the original game's starting position. 0 is before any move, 1 is after White's first move, 2 after Black's first, 3 after White's second, and so on. For a game whose starting FEN has Black to move, 1 is after Black's first move; it's always a count of half-moves.
- Custom tags are allowed by the PGN standard; other tools ignore tags they don't know.
- **Imported games:** import also reads `GameID`, if present (e.g. a PGN exported from one of these boards), so a game continued from an import can reference it. If the PGN has none, the continued game has no `ContinuedFrom` tag.

### Continuing from an imported game

- Allowed, like any other game history position.
- The players seated at the board play on, whatever the PGN's `White`/`Black` tags say. Names change, and someone may want to play on from another person's game.
- **What import keeps:** the game history (starting FEN, if one was given, and the moves), plus the `White`, `Black` and `Result` tags, which are shown in the UI during replay. Nothing else from the PGN is stored.
- **On continuing**, the game history up to that point (including the starting FEN) is kept, and the imported `White`, `Black` and `Result` are dropped.
- Every PGN the board writes is built from scratch from the board's own state: current players, date, result, and the `FEN` tag when the game didn't start from the standard position.

## Instance-wide objects

Besides the boards, the world has two shared objects, each one behaviour with its own synced fields.

### Settings: `ChessArbiter`

An arbiter is the official who enforces the rules at a chess event, which matches what this object does.

- Holds the rules settings: whether takebacks are allowed, each draw rule (agreement, threefold, fifty-move, fivefold, seventy-five-move, dead position; "No draws" is all draw rules off), the inactivity times, and an optional forced time control.
- Defaults come from the world's configuration in the editor. Only organizers can change settings in-world.

#### Organizers (`SimpleAuth`)

Authorization comes from the `SimpleAuth` abstract class (`SiggiUSTools` namespace), placed in the Unity project:

```csharp
public abstract class SimpleAuth : UdonSharpBehaviour {
    public abstract bool IsAuthorized(VRCPlayerApi who);
}
```

- A player for whom `IsAuthorized` returns `true` is an **organizer**: they can change settings (instance-wide and per board), make a board re-snapshot its settings, and reset any board.
- `ChessArbiter` has a `SimpleAuth` field set in the editor, pointing at whatever concrete implementation the world uses.
- **Being authorized changes nothing by itself.** An organizer joining the instance doesn't switch on tournament rules or any other settings; settings only change when an organizer changes them in the settings UI.
- **Where it's checked:**
  - The settings UI only shows its controls to the local player if they're authorized.
  - `ChessArbiter.OnOwnershipRequest` only allows a transfer to an authorized player, so a non-organizer can't take over the settings.
- If the organizer who owns the arbiter leaves, VRChat hands it to a random player without asking (as with boards). That player owns the synced settings but can't change them, because the UI's local check refuses. Nothing on the arbiter needs its owner to act.
- `IsAuthorized` should give the same answer for a given player on every client, since the ownership check runs on both the requesting client and the current owner.
- **Built-in implementations** shipped with the chess system; the world creator assigns one, or their own:
  - **`FirstJoinerAuth`:** the first player in the instance is the organizer. Rights don't pass on when they leave (that's what `InstanceMasterAuth` is for). Late joiners can't tell who joined first from who's present now, so the first joiner's identity is a synced field on this behaviour: the first player sets it to themselves when they arrive and find it empty. It stores the display name, so the organizer keeps their rights after a crash and rejoin, consistent with how players keep their seats.
  - **`InstanceMasterAuth`:** whoever is currently instance master. Rights move with the master role when the master leaves. Provided as an option because it's common in VRChat game worlds, but **not the default and not recommended**: when the master leaves, VRChat picks the new master, unpredictably, and nobody can choose who it will be. (Preferring the instance owner was considered and dropped: an absent owner can't be told apart from no owner, which made the rules muddier than they're worth.)
  - **`NoOneAuth`:** nobody is authorized; the editor defaults apply unchanged.
  - Any other rules (lists of names, group roles, etc.) are left to the world creator's own subclass.
- **Default: `FirstJoinerAuth`.** The arbiter prefab includes a `FirstJoinerAuth` component and its `SimpleAuth` field points to it, since that's most likely what someone building their own tournament world wants.
- **An empty `SimpleAuth` field is an error**, since it can only happen if someone clears it on purpose (choosing nobody should be done with `NoOneAuth`).
  - **Build error:** an editor script implements the VRChat SDK's build callback (`IVRCSDKBuildRequestedCallback`), finds any `ChessArbiter` in the scene with an empty field, logs an error naming the object, and returns `false` to cancel the upload. This is a plain C# editor script in an `Editor` folder, not UdonSharp.
  - **In-game fallback:** for cases that bypass that check (e.g. Play Mode testing), the arbiter checks the field on `Start`. If it's empty it logs an error, shows a message in the settings UI, and behaves like `NoOneAuth`.
- Boards hold a reference to it and read settings when they need them (rarely, so the cross-behaviour cost doesn't matter).

#### Per-board overrides

- Each setting has an instance-wide default, and any board can override any setting, e.g. a tournament board with no takebacks next to casual boards.
- **Encoding:** each setting is one bit, so a full set of settings is one `int`. The arbiter holds:
  - the instance defaults (`int`),
  - per board: an override mask and override values (`int[]` each, indexed by board number).
  - A board's effective settings are `(defaults & ~mask) | (values & mask)`: overridden bits come from the board, the rest from the defaults.
  - The two inactivity times are numbers, not on/off, so they're stored as separate values: instance defaults plus per-board `int[]` overrides (in seconds), with a bit in the override mask saying whether the board overrides each one.
- **Stored on the arbiter, not the board.** Overrides must survive a board reset, which clears all of the board's state, and changing them only needs ownership of the arbiter. Boards are given their board number in the editor.
- The settings menu (a view of the arbiter) lets an organizer pick "all boards" or a specific board, and set each setting to on, off, or "use default" for a board.
- **Snapshot:** a board copies its effective settings into its own synced state when it leaves the reset state (someone joins a seat, or a game is loaded or imported), and uses that copy until it's reset. Changing settings doesn't alter the rules of a board already in use.
- **Re-snapshot on request:** if a mistake was made, an organizer can tell a board, or all boards, to take a new snapshot. The organizer takes ownership of the board (see Ownership), the board re-copies its effective settings, and serializes.

### Previous games: `GameArchive`, shown on the history wall

- Stores the games played in this instance: every game that gets logged (finished, or unfinished when reset or continued from an earlier position).
- Per game: game ID, continued-from reference, mode, time control, players, result, starting FEN, the packed move history and the per-move clock history.
- **No notation stored.** The player loading a game from the archive recomputes it locally (using the fill-in code, time-budgeted), and it's synced to everyone else as part of the board state when the game is loaded onto the board.
- **Storage:** Udon can't sync arrays of arrays, so games are stored flattened: one `int[]` with every game's moves back to back, plus per-game arrays of offsets, IDs, names and results. 
- **Capacity: at most 200 games.** When full, adding a game drops the oldest. At 4 bytes per move, 200 games of 80 plies is about 64 KB of moves, plus IDs, names and results: well under the ~280 KB manual-sync limit.
- **Bandwidth is the real constraint.** A manual-sync serialization sends *all* of a behaviour's synced fields, so with one archive behaviour, every game added would resend the whole archive: ~70 KB at ~11 KB/s is about 6 seconds of the sender's Udon bandwidth, competing with board updates. To avoid that, split the archive into **pages**: several `GameArchive` page behaviours (e.g. 10 pages of 20 games). Adding a game only reserializes the page it goes into (~7 KB). The history wall reads across all pages. Late joiners still receive everything, which is VRChat's job.
- **Adding a game:** when a game ends, the board's owner takes ownership of the archive, appends the game and serializes. Two boards ending at the same moment could each take ownership and overwrite the other's addition. Handle that by having each board check, after the next sync, that its game ID is in the archive, and retry if not.
- **Loading a game onto a board:** a player picks a game on the history wall. A "Load it here" button then appears on every board currently in its reset state, visible to that player; pressing one loads the game onto that board, which enters replay mode with it. No parsing is involved: the packed moves are copied straight from the archive. The players and result are shown the same way as for an imported PGN, and continuing from a position works the same way, including the `ContinuedFrom` reference.
- **A game can only be loaded onto a board in its reset state.** A board with a game in progress refuses. So does a board showing another loaded or imported game; it has to be reset first.

### Reset

- Resetting a board clears it completely, including both seats. The previous game remains available from the archive, so the board doesn't need to keep it.
- **Who can reset:**
  - a seated player, at any time, including mid-game;
  - an organizer, at any time;
  - anyone else, once the board has been idle for the reset-available time (see Inactivity).
- A game in progress that's reset is logged and archived as unfinished (`*`), if it had any moves. So a player who resets instead of resigning doesn't record a loss; that's accepted.

## Networking

Board state is synced directly as `[UdonSynced]` fields on the board behaviour (manual sync mode), sent with `RequestSerialization()`. Only manual sync is used; no continuous sync.

### Limits

From VRChat's [network details](https://creators.vrchat.com/worlds/udon/networking/network-details/):

- Udon scripts can send about **11 KB per second**.
- Manual sync: about **280,496 bytes per serialization**.
- Continuous sync: about 200 bytes per serialization (not used here).

Implications:

- A board's synced state is a few KB at most (a long game's move history is ~1–2 KB, plus notation), far under the per-serialization limit.
- Every serialization sends all of a behaviour's synced fields, not just the changed ones. So the board resends its whole state on every move *and* on every activity touch (pickups). That's still small, but it's why activity updates are throttled, e.g. at most one every few seconds while pieces are being handled.
- The archive is the one large object, so it's split into pages (see `GameArchive`).

No FEN or PGN goes over the network, so no client parses anything to stay in sync. Late joiners receive the last serialized state automatically.

### Synced fields (per board)

| Field | Type | Notes |
|---|---|---|
| pieces | `byte[64]` | piece per square, 0 = empty |
| side to move | `byte` | |
| castling rights | `byte` | 4 bits |
| en passant | `sbyte` | square, or -1 |
| halfmove clock, move number | `int` | current values |
| starting halfmove clock, move number | `int` | from the starting position; see Undo |
| king / rook starting files | `byte` ×3 | for castling and undo (Chess960) |
| move history | `int[]` | full packed moves, 4 bytes each |
| notation | `string` | each move's SAN, space-separated |
| starting position | `string` | FEN, copied into the PGN `FEN` tag when not the standard start; never parsed by receivers |
| replay mode | `bool` | whether the board is showing replay or the live game |
| replay index | `int` | ply of the game history the replay started from or is showing |
| replay board | `byte[64]` | the position shown in replay mode |
| replay custom | `bool` | replay position has left the game history; blocks continuing |
| takeback request | `byte` + `int` | requesting colour and ply, or none |
| draw offer | `byte` | offering colour, or none |
| game ID | `string` | see Game IDs |
| continued from | `string` | `<GameID>/<ply>`, or empty; see Game IDs |
| replay source White / Black / Result | `string` ×3 | from an imported PGN or an archived game, for display in replay; cleared when play continues |
| white / black player | `string` ×2 | display names, so a rejoining player keeps their seat; see Ownership |
| last activity | `double` | server time of the last piece touch, see Inactivity |
| result | `byte` | in progress, or ending + winner; see Game endings |
| lifecycle state | `byte` | reset, lobby, in game, ended; see Seats, game start and modes |
| mode | `byte` | Standard, Chess960 (Fog of War later) |
| time control | `int` ×2 | base and increment in seconds; base `-1` = no clock |
| clocks | `int` ×2 + `double` | each side's remaining ms, and server time the current turn began |
| clock history | `int[]` | mover's remaining ms after each move |
| game settings | `int` + `int` ×2 | settings snapshot (rule bits, and the two inactivity times) taken when the board left the reset state; see Per-board overrides |

- The move history is the full packed ints, not a 2-byte compact form. A long game is a couple of KB, well within manual sync limits, and receivers get the undo info with no reconstruction. This replaces the earlier 2-byte format, which was designed for receivers rebuilding moves themselves.
- Because every packed move carries its own undo info, a client can walk back from the current position to any earlier one by undoing moves. Replay and filling in notation therefore don't need the starting FEN to be parsed.
- Notation is a single space-separated string rather than a `string[]`; SAN never contains spaces, and a single string is the simplest synced type. Check that synced `string[]` works if a per-move array turns out more convenient.

### Ownership and sending

- Only the owner of a behaviour can change its synced fields. The player making a move takes ownership (`Networking.SetOwner`), applies the move, updates the fields, then calls `RequestSerialization()`.
- Receivers handle `OnDeserialization` by copying the fields into their local state and refreshing the board visuals. No move generation is needed to stay in sync.
- The two players are recorded in synced fields (white and black).
- **While both players are active, the board refuses to transfer ownership to anyone who isn't one of them**, except to organizers (below), or once the board has been idle long enough for reset (see Inactivity). A player is active if they are in the instance. The check goes in `OnOwnershipRequest`, which returns `false` to deny a transfer.
- **Either player may take ownership at any time**, not only the player to move, since either player can enter replay mode or touch pieces. Ownership flips between them as they act; whoever acts last owns the board and sends the update.
- **When the owner leaves, VRChat gives ownership to a random remaining player without calling `OnOwnershipRequest`** (confirmed). This is consistent with the rule: if the owner was one of the two players, they are leaving, so both players are no longer active.
- The owner isn't always a player: the instance master owns the board by default, and someone pressing reset takes ownership. If such a non-player owner leaves while both players are active, the automatic transfer can again land on a non-player. That's harmless as long as game logic never treats "is the owner" as "may move": a move is valid only if made by the player assigned to that colour, and players can always request ownership back.
- **A player leaving doesn't end or reset the game.** People leave because of VRChat or SteamVR crashes and other technical trouble, and should be able to come back to their game. The board stops being touched, so the inactivity rules below apply: other players can reset it after the reset-available time, and it resets itself after the auto-reset time. No leave detection is needed.
- **Players are identified by display name**, not `playerId`, so a player who rejoins after a crash gets their seat back. `playerId` changes on rejoin; the display name doesn't.
- **Organizers** (see `SimpleAuth`) may always take ownership of any board, whatever its state. They then act on it directly, through the same code paths players use: reset, re-snapshot settings, and whatever dispute handling needs.
  - This was chosen over routing organizer actions through per-board request counters on `ChessArbiter`. Counters would need the board's owner to watch the arbiter, track which requests it had handled, and act on them, which is more moving parts for no gain.
  - Taking ownership mid-game is harmless: ownership never grants the right to move, and players take it back on their next action.
  - As between the two players, an organizer acting at the same moment as a player can overwrite that player's update (e.g. a reset landing just after a move). That's acceptable: the organizer's intent wins.

### Inactivity

- A board is inactive when nobody has touched anything on it. After the **reset-available time** (default 2 minutes) the reset button becomes available to everyone; after the **auto-reset time** (default 10 minutes) the board resets itself.
- Both times are settings on `ChessArbiter`, configurable in the in-world settings UI, with per-board overrides like the other settings. 
- **Either time can be "never"** (stored as `-1`; `0` would mean immediately), e.g. for tournaments where a crashed player may need longer to get back.
  - Reset-available "never": nobody else can reset the board, however long it's idle.
  - Auto-reset "never": the board never resets itself.
  - When both are set to times, the reset-available time must not exceed the auto-reset time.
- With both set to "never", a board whose player has left for good stays as it is until an organizer resets it. So organizers can always reset a board regardless of the timers. They take ownership of the board and reset it directly (see Ownership).
- **Shared clock:** the last activity time is a synced field holding VRChat server time (`Networking.GetServerTimeInSeconds()`), so every client agrees on how long the board has been idle without syncing a timer. The `double` seconds version avoids the wraparound of the `int` milliseconds one.
- **Touching anything on the board counts as activity**: picking up a piece (even dropping it back where it came from, or an illegal drop), stepping through a replay, pressing a board button. Each touch updates the synced timestamp: the player takes ownership and serializes. Throttle it if players fidget with pieces.
- **Exception to the ownership rule:** pressing reset means a non-player takes ownership while both players may still be active. `OnOwnershipRequest` allows the transfer once the board has been idle for at least the reset-available time, measured against the synced timestamp.
- **The automatic reset is done by the current owner only**, so it happens once. If the owner has left, VRChat has already passed ownership to someone else, who then does it.
- Applies whenever the board isn't in its reset state: a game in progress, an ended game, or a replay. A reset board has nothing to reset.
- A game ended by reset is logged as an unfinished game (PGN result `*`); see Game endings.

### Filling in missing notation

- Notation is synced, but every client also has code to compute it locally if any is missing (the string has fewer entries than the move history, or an entry is empty). It walks back to the position before each missing move and computes it as described under Legality checking, using the time-budgeted processing for long gaps.
- This keeps open the option of not syncing notation at all and having every client compute its own.

### Imported games

The importing player parses the PGN locally, then writes the resulting state (pieces, move history, notation, starting FEN, replay index) to the synced fields like any other update. Everyone else, including late joiners, can step through the replay. (Syncing only the final FEN was the original idea; it shows the end position but loses the game.)

## PGN

### Import (local, rare)

- Started with a "Load PGN" button on a board, which opens the text box to paste into.
- Runs only on the importing player. **The player doesn't take ownership of the board until the import has finished**; only then are the results written to the board's synced fields.
- Starts from the standard position, or from the PGN's `FEN` tag if present (e.g. an imported Chess960 game), then plays through every move in the PGN.
- Tags read: `FEN` (starting position), `White`, `Black`, `Result` for display in replay, and `GameID` so a continued game can reference it. All others are ignored. Comments in the move text are skipped too.
- Written as a state machine with its state in fields (text position, working board, moves so far). Each frame it processes moves until a time budget runs out, then pauses until the next frame, and stops once the PGN is done.
- Starting budget is 10 ms, but that may cost frames: at 90 Hz in VR a whole frame is ~11 ms. Benchmark it; the budget may need to be smaller, or the whole import may be fast enough to run in one go.
- Reports a failure state ("failed at move N") instead of throwing.
- **While importing**, the moves are played on a local working board as they are parsed. Nothing is displayed or synced until the import finishes; then the result is written to the board's synced fields in one update, and the board enters replay mode.
- The board isn't locked during an import. If its state changes before the import finishes (e.g. someone starts a game on it), the import is abandoned and the player told why. This avoids a lock that could get stuck if the importing player leaves mid-import.
- "Load PGN" is only offered on boards in the reset state, like loading from the archive.
- The Java's `BufferedReader.mark()/reset()` loop for reading multiple games goes away: input is a single pasted string.

### Export

- In the Java, `PGN.write` uses no regex itself, but it replays the whole game from the start and calls `move.notation(board)` for every move. That runs `MovePredicates.parse` (regex) on candidate spellings, and also regenerates legal moves and tests for check and checkmate. Export therefore costs about as much as import.
- The port instead records each move's notation (SAN) when the move is played, using the direct disambiguation described under Legality checking, and syncs it with the board state (see Networking). Export and logging are then plain string building with `StringBuilder`, with no replay.
- Computing notation needs legal-move generation (to disambiguate, and for the `+`/`#` suffix). Since it is synced, only the player making the move (or the importer) pays that cost; other clients compute it only to fill in missing entries.
- Castling is written as `O-O`/`O-O-O` directly. The Java produces `0-0` and has `PGN.write` swap the zeros for letters afterwards.
- Line wrapping (the Java's `LineWriter`) only splits on spaces; `string.Split(' ')` in C# is not regex-based.

### Input / output

- A text box players can open to copy the current PGN out, or paste one in.

### Logging

- Every completed game's PGN is written to the VRChat output log, so players can recover it later and a bot can capture every game in the instance.
- One log call per PGN with a distinctive prefix (e.g. `[ChessPGN]`), so a bot can find it among other log lines.
- Every client needs the full move list to produce the PGN — another reason to sync moves, not just positions.
- New work, not porting: the Java has check and checkmate but no game results. The port adds a synced game-result field (see Game endings).

## Game endings

| Ending | How | PGN `Result` | Logged |
|---|---|---|---|
| Checkmate | detected after a move (not in Fog of War) | `1-0` / `0-1` | yes |
| King capture | Fog of War only (later) | `1-0` / `0-1` | yes |
| Stalemate | detected after a move: no legal moves, not in check; always, regardless of draw settings | `1/2-1/2`, with `[Termination "stalemate"]` | yes |
| Other draws | per the enabled draw rules | `1/2-1/2` | yes |
| Resignation | resign button, pressed by a player for their own colour | `1-0` / `0-1` | yes |
| Time forfeit | a side's clock reaches zero (see Clocks) | `1-0` / `0-1`, or `1/2-1/2` if the opponent can't checkmate and draw rules allow | yes |
| Reset of an unfinished game | by a seated player or organizer at any time, anyone after the reset-available time, or automatic after the auto-reset time | `*` | yes, as unfinished |
| Continuing an unfinished game from an earlier position | takeback agreed by both players (see Replay mode) | `*` | yes, as unfinished, before the history is cut |

- The result is a synced field (in progress, or which ending and who won), set by whoever ends the game, like any other state change.
- Only the player assigned to a colour can resign for it. Pressing resign takes ownership, which players are always allowed to do.
- **Log once per client, on the transition.** Each client logs when it sees the result change from "in progress" to ended, either locally or in `OnDeserialization`. A late joiner whose first sync already shows an ended game doesn't log it. Each client in the instance writes its own log, so a logging bot gets every game.
- A reset after the game has already ended doesn't log it a second time.
- **Games with no moves aren't logged** (or archived), e.g. a board reset from the lobby or right after Start.
- **How the game ended is recorded in the `Termination` tag.** The PGN standard's suggested values (`normal`, `time forfeit`, `abandoned`, `unterminated`, ...) can't tell checkmate from resignation or one draw rule from another, so the board uses descriptive values. `Termination` is free text, so other tools accept them. Standard wording is used where it exists:

  | Ending | `Termination` |
  |---|---|
  | Checkmate | `checkmate` |
  | Resignation | `resignation` |
  | Time forfeit | `time forfeit` |
  | Stalemate | `stalemate` |
  | Draw by agreement | `draw agreement` |
  | Threefold / fivefold repetition | `threefold repetition` / `fivefold repetition` |
  | Fifty / seventy-five-move rule | `fifty-move rule` / `seventy-five-move rule` |
  | Dead position | `insufficient material` |
  | Reset of an unfinished game | `unterminated` |
  | Continued from an earlier position (unfinished) | `unterminated` |
  | King capture (Fog of War, later) | `king captured` |

### Draw rules

Each FIDE draw rule can be switched on or off in the instance settings menu (on the settings object). A **"No draws"** option, selected by default, prohibits draws entirely.

| Rule | FIDE behaviour | Needs |
|---|---|---|
| Agreement | one player offers, the other accepts | draw-offer button and synced pending offer, like takeback requests |
| Threefold repetition | drawn when a player claims it | repetition detection, claim button |
| Fifty-move rule | drawn when a player claims it (50 moves each without a capture or pawn move) | halfmove clock, claim button |
| Fivefold repetition | drawn automatically | repetition detection |
| Seventy-five-move rule | drawn automatically | halfmove clock |
| Dead position | drawn automatically when neither side can checkmate (e.g. king vs king, king and bishop vs king) | insufficient-material check |

- **Halfmove clock:** the Java's `halfMoves` just counts every move, which is not the fifty-move clock. The port resets it on captures and pawn moves; earlier values are derived from the move history (see Undo).
- **Repetition detection without hashing:** a repeat can only occur since the last capture, pawn move or castling-rights change, so at most the last halfmove-clock's worth of plies. Walk back through those by undoing moves on a scratch array and compare the 64 squares, side to move, castling rights and en passant. That's at most ~100 comparisons of 64 bytes, once per move, and needs no precomputed hash tables (which would need static fields).
- **Dead position:** check only the standard insufficient-material cases (K v K, K+B v K, K+N v K, K+B v K+B with same-coloured bishops). Detecting every dead position is impractical.
- **Claims:** for threefold and fifty-move, show a "Claim draw" button to the player to move when the condition holds. FIDE also lets a player claim with a move they're about to make; leave that out.
- **Draw result:** PGN `1/2-1/2`, logged like any other ending, with the rule recorded in `Termination`.
- **Stalemate is not one of these switches.** It always ends the game as a stalemate, whatever the draw settings, including "No draws".
- **Under "No draws"**, none of the rules above end a game. Apart from checkmate and stalemate, the only ways to end it are resignation or a reset. In dead positions and repetition or move-count situations, play simply continues.

## Bugs found in the Java

Fix these in the port. They're worth fixing in the Java too while it serves as the reference, or comparisons against it will disagree.

- **Capturing a rook doesn't remove the opponent's castling right.** `MoveFinderPiece.findMove` only sets the removal flags for the *moving* piece, and `MoveFinderPawn` always passes `false`.
- **Castling doesn't check the rook is there.** `MoveFinderKing.findCastleMove` checks the squares in between but never that the rook is still on its starting square, and `CastleMove` creates a rook of the castling colour on the destination. Combined with the bug above: after `Bxh8` capturing the rook, Black can still `O-O` and a rook appears on f8.

## Testing

Neither codebase has tests yet, and UdonSharp code only runs inside Unity, so tests can't run under a plain .NET test runner.

- A test behaviour, run in Unity Play Mode (with ClientSim), that executes checks and logs pass/fail.
- Perft counts from known positions (standard start, Kiwipete, Chess960 positions) against published values, which avoids depending on the Java and its castling bugs. Perft also gives the speed numbers needed to judge the Udon performance concerns above.
- **Networking needs real clients.** ClientSim runs a single local player, so ownership, sync, late joiners and the `VRCObjectSync` behaviour have to be tested with several clients, e.g. VRChat's Build & Test with multiple instances of the client.
- Round-trip `Siggi-vs-Nuka.pgn` (currently in the Java repository's root; bring it along into the Unity project) through the Java and the port and compare the output.

## Implementation steps

Ordered so that each step can be tested before the next builds on it: rules first with no world objects, then the logical board, then networking, then the world. Open decisions that block a step are noted on it.

### 1. Project setup and early checks

1. Create the Unity project with the VRChat Creator Companion, Worlds SDK 3.7.1 or later.
2. Copy the Java library into `JavaChessBoard/` and `Siggi-vs-Nuka.pgn` alongside; move this plan to the project root.
3. Add `SimpleAuth` (`SiggiUSTools`).
4. Fix the two castling bugs in the Java (see Bugs found in the Java), so it's a trustworthy reference.
5. **Early checks.** These are risky assumptions that could change the design, so test them before building on them:
   - Udon speed: a throwaway behaviour that generates moves in a loop, to get a feel for real costs.
   - `VRCObjectSync`: does it fight a non-owner snapping a piece into place? Test with two clients.
   - Whether synced `string[]` works (affects how notation is stored).

### 2. Core rules (static methods, no world)

1. Constants and helpers: piece types and colours, square ↔ file/rank, piece encoding in the `byte[64]` board.
2. Move encoding: pack/unpack helpers, and the derived en passant / double-push checks.
3. Make and unmake a move on the board arrays, including castling (Chess960-aware), en passant, promotion, castling rights and en passant restore.
4. Move generation per piece type, and the attack check.
5. Legal move list (play, attack check, undo), with the `prohibitSelfCheck` option; setting it to `false` skips the check filtering (for Fog of War later).
6. **Test:** a test behaviour running perft from published positions (standard start, Kiwipete, the other standard perft positions, some Chess960 positions) and checking the counts. Record the timings.

### 3. FEN

1. FEN parsing and writing, including Chess960 castling letters and the king/rook starting files.
2. **Test:** round-trip a set of FENs; compare against the Java's output for the same positions.

### 4. Notation, endings and draw detection

1. SAN generation with direct disambiguation and `+`/`#` suffix; `O-O`/`O-O-O`.
2. Checkmate and stalemate detection.
3. Halfmove clock (derived from history), repetition detection, the fifty/seventy-five-move rules, insufficient material.
4. **Test:** known positions for each ending and draw rule; SAN for ambiguous moves (two knights, two rooks, promotions, captures).

### 5. PGN

1. Export: tags (seven-tag roster, `FEN`, `GameID`, `ContinuedFrom`, how the game ended), move text with line wrapping, via `StringBuilder`.
2. Import: regex tokenizer (adapted for .NET), reading `FEN`/`White`/`Black`/`Result`/`GameID`, matching each SAN against legal moves; failure reporting.
3. Time-budgeted state machine for import.
4. **Test:** round-trip `Siggi-vs-Nuka.pgn` and compare with the Java's export; malformed PGNs fail with the right move number.

### 6. Logical board, single player (`ChessBoard`, no networking yet)

1. Board state fields; lifecycle (reset, lobby, in game, ended); Join White / Join Black; mode and clock selection; Start; Chess960 position generation.
2. Clocks: running time from the turn-start timestamp, increment, clock history, flag fall.
3. Input API: `PiecePickedUp`, `PieceDropped`, promotion choice, castling by drop, the buttons.
4. Game endings and result; resign; draw offers and claims per settings; time forfeit.
5. Replay mode: replay board, stepping, free moves, custom flag; takeback request/accept and truncation (restoring clocks).
6. Listener notification (`ChessBoardListener`, `OnBoardChanged`, last-change value) and the getters the view needs.
7. Game IDs and `ContinuedFrom`.
8. PGN logging on the "in progress → ended" transition, skipping games with no moves.
9. **Test:** a test behaviour (also a `ChessBoardListener`) that drives the board through scripted games, replays, takebacks and every ending, checking state and log output.

### 7. Settings (`ChessArbiter`)

1. Settings bits and inactivity times, instance defaults from the editor, per-board overrides, effective-settings calculation.
2. Board snapshot on leaving the reset state; re-snapshot.
3. `FirstJoinerAuth`, `InstanceMasterAuth`, `NoOneAuth`; default wiring; empty-field build check (editor script) and in-game fallback.
4. **Test:** effective settings for combinations of defaults and overrides; snapshot timing; the build check blocks an empty field.

### 8. Networking (`ChessBoard`, `ChessArbiter`)

1. Synced fields, manual sync, `RequestSerialization`, `OnDeserialization` → local state → listeners.
2. Ownership rules in `OnOwnershipRequest`: players, organizers, idle-reset exception.
3. Inactivity: shared server-time timestamp, throttled activity updates, reset-available and auto-reset (including "never").
4. Notation sync and fill-in for missing entries.
5. Imported games written in one update at the end; abandon if the board changed.
6. **Test with several clients:** moves both ways, late joiner, owner leaving mid-game, a player rejoining and reclaiming their seat, bystander denied, organizer reset, idle timers, log written once per client.

### 9. World presentation (`ChessBoardView`)

1. 32 piece objects with mesh swapping; square ↔ object mapping; placing pieces from board state.
2. `VRCPickup` + `VRCObjectSync`; per-player `pickupable` from the board; pieces' ownership check.
3. Forwarding pickups, drops and buttons to the board; snapping back on rejection.
4. Promotion picker, highlights (last move, legal targets), move animations, status display (turn, check, result, stalemate, replay source names).
5. **Test with several clients:** carrying is visible to others, rejected moves snap back everywhere, bystanders can't grab during a game.

### 10. Archive and history wall (`GameArchive`)

1. Paged storage (flattened arrays), capacity 200 with oldest dropped, appending on game end, the retry check for simultaneous additions.
2. History wall view; "Load it here" buttons on reset boards; loading into replay mode.
3. **Test:** fill past 200 games; two boards ending at once; late joiner sees the full archive; loaded games continue with `ContinuedFrom`.

### 11. Remaining UI

1. Settings menu (all boards / per board, on/off/default, timers with "never", forced time control), shown only to organizers.
2. PGN text box: copy current PGN, "Load PGN" on reset boards.

### 12. Final pass

1. Full multi-client session covering every feature, with a few boards at once.
2. Check the output log with a logging bot (or a script reading the log): one PGN per completed game, correct tags.
3. Performance: frame times during play, imports and archive loads, on lower-end hardware and in VR.

## Open decisions

None for the first version.

- **Deferred until Fog of War is implemented** (not needed for the first version):
  - Spectators: see everything, or nothing? Seeing everything is more fun to watch, but a spectator could tell a player what's hidden.
  - Replay during a live Fog of War game: replay is shared, so it would show every piece to everyone. Options: replay shows the fog from each viewer's side, or replay is disabled until the game ends. After the game ends, presumably everything is revealed.
  - With no check, a player can't be checkmated, and is almost never left without a move; if they are, is that stalemate (draw) or a loss? Do draw rules apply?
  - Can Chess960 and Fog of War be combined?

Items marked "to check" above (e.g. whether `VRCObjectSync` fights pieces being snapped into place) are verification work for implementation, not design decisions.

## Environment

- Development needs Unity with the VRChat Worlds SDK (installed via the VRChat Creator Companion), which includes UdonSharp. A standalone .NET SDK isn't needed.
- VRChat's planned successor to Udon, **Soba**, would remove most of the Udon constraints. As of August 2026 it had not entered closed beta and has no date, so this plan doesn't rely on it.
