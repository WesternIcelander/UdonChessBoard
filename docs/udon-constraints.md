# Udon constraints that shape the design

UdonSharp compiles only part of C#. The ones that matter here:

- No custom classes except `UdonSharpBehaviour` subclasses, which are components on GameObjects (no `new`; they must be instantiated or pre-placed).
- No interfaces, no generic collections (`List<T>`, `Dictionary<K,V>`); only arrays and VRChat's `DataList`/`DataDictionary`.
- No `try`/`catch`/`finally`/`throw`.
- Recursion requires `[RecursiveMethod]`.
- No file or stream I/O.
- No static fields, no delegates or lambdas.
- Supported: user enums, static methods, extension methods, properties, `out`/`ref`, inheritance between behaviours with virtual methods, `uint`.
- `Regex` is exposed. It is .NET's regex engine, not Java's (see Consequences below).
- `System.Text.StringBuilder` is available from VRChat SDK 3.7.1, so text building (PGN and FEN output, move notation) can follow the Java. The code therefore needs SDK 3.7.1; the project's minimum is 3.10.4, which the `io.siggi.ustools` dependency (`SimpleAuth`, see [organizers](organizers.md)) requires.
- TextMeshPro (`TextMeshProUGUI`, `TMP_InputField`) is exposed for what the views use: `text`, `color`, `readOnly`. Not exposed: `TMP_Text.rectTransform` (use `GetComponent<RectTransform>()`), `TMP_InputField.lineType` and `multiLine` (checked by an editor script instead, see [Testing](testing.md#view-test)).
- The Udon VM is far slower than normal C#, and calls into *another* behaviour (field reads, method calls) are much more expensive than local array access. So each behaviour showing text reads the strings it shows from the shared `I18n` once into fields of its own, and fills in their `${name}` parts with the static `I18n.Format`, which is compiled into the caller (step 13.5, [Architecture](architecture.md#player-text-step-135)).

## Consequences

- No exceptions: errors become return codes / `bool` + `out` values (e.g. PGN import reports "failed at move N").
- Regex: the Java patterns in `PGN` and `MovePredicates` need two changes for .NET.
  - `TOKEN_PATTERN` uses possessive quantifiers (`*+`), which .NET doesn't support; use atomic groups `(?>...)`.
  - Java's `matcher.matches()` requires the whole string to match; in .NET add `^...$` anchors.
  - With no static fields, compiled `Regex` objects live in instance fields of the behaviour that uses them.
  - Regex is used only when parsing PGN or FEN, never in normal play, so its speed doesn't matter. Import already spreads its work across frames with a time budget.
  - Normal play must stay regex-free. Input needs no parsing (see [Player input](player-input.md)), but notation *output* does in the Java: `RegularMove.notation` calls `MovePredicates.parse` (regex) on up to four candidate spellings to choose the shortest unambiguous one. The port should work that out directly instead (see [Legality checking](legality-checking.md)).
- No I/O: PGN and FEN are string in, string out.
- No static lookup tables: use `const`s, computed lookups, or instance fields.
- No lambdas: `removeIf`, `Predicate<Move>` and `Lazy` become plain loops and flags.
- `public` doesn't mean API: a method must be public for another behaviour, a test, a UI event (`SendCustomEvent`) or a delayed event to call it, so public methods that aren't public API start with `_` (step 13.7). The API is exactly the public methods without one, besides Unity's and Udon's events, `ChessBoardListener`'s virtual methods and `I18n`'s `OnLanguageChange`. VRChat also refuses a network event (`SendCustomNetworkEvent`) for a method starting with `_`, so no client can call an internal method on another.
- `Square`, `Piece`, `PieceType`, `PieceColor`, `File`, `Rank` become plain C# enums or ints, with `-1`/`None` in place of `null`. Their Java fields (`Square.x`, `Piece.color`, ...) become static helper methods.
