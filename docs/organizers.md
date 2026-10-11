# Organizers (`SimpleAuth`)

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
- **Implementation:** the three built-ins are in `Assets/Siggi/ChessBoard/Auth/` (namespace `SiggiChessBoard`); the arbiter's field is `ChessArbiter.simpleAuth`, and the arbiter prefab is `Assets/Siggi/ChessBoard/ChessArbiter.prefab`.
  - `FirstJoinerAuth` (manual sync, on the arbiter's object so it shares the arbiter's owner) keeps the name in the synced `firstJoiner` (`""` until set). In `OnPlayerJoined` for the local player, a client that owns the object and finds the name empty sets it to its own display name and serializes: the first player owns every object when they arrive, and a late joiner isn't the owner. A late joiner sees nobody authorized until the name arrives by serialization.
  - `InstanceMasterAuth` is `who.isMaster`; `NoOneAuth` is always `false`. All three refuse an invalid player.
- **An empty `SimpleAuth` field is an error**, since it can only happen if someone clears it on purpose (choosing nobody should be done with `NoOneAuth`).
  - **Build error:** an editor script implements the VRChat SDK's build callback (`IVRCSDKBuildRequestedCallback`), finds any `ChessArbiter` in the scene with an empty field, logs an error naming the object, and returns `false` to cancel the upload. This is a plain C# editor script in an `Editor` folder, not UdonSharp.
    - Implementation: `ChessArbiterBuildCheck` in `Assets/Siggi/ChessBoard/Editor/`. It runs for world builds (`VRCSDKRequestedBuildType.Scene`), which the SDK requests for both Build & Test and upload, and checks every `ChessArbiter` (inactive ones included) in every loaded scene. Each error names the arbiter by its hierarchy path and has the arbiter as its context object, so clicking it in the Console selects it. Since the SDK's own popup only names the callback ("aborted at the request of 'ChessArbiterBuildCheck'"), the check first shows its own dialog, written for someone who doesn't program: which arbiters have the empty field, what the field is for, and how to fill it (the three built-ins, with `NoOneAuth` for nobody). It also selects and pings the first such arbiter.
  - **In-game fallback:** for cases that bypass that check (e.g. Play Mode testing), the arbiter checks the field on `Start`. If it's empty it logs an error, shows a message in the settings UI, and behaves like `NoOneAuth`.
    - Implementation: on `Start` (on every client, late joiners included) an empty field sets `ChessArbiter.simpleAuthMissing`, which the settings menu will show a message for (step 11), and logs the error naming the arbiter object. Everything that asks who is an organizer (settings menu, ownership checks, boards) asks `ChessArbiter.IsOrganizer(who)`, never `simpleAuth` directly: it returns `simpleAuth.IsAuthorized(who)`, or `false` for everyone while the field is empty.
- Boards hold a reference to it and read settings when they need them (rarely, so the cross-behaviour cost doesn't matter).

