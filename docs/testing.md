# Testing

Neither codebase has tests yet, and UdonSharp code only runs inside Unity, so tests can't run under a plain .NET test runner.

- A test behaviour, run in Unity Play Mode (with ClientSim), that executes checks and logs pass/fail.
- Perft counts from known positions (standard start, Kiwipete, Chess960 positions) against published values, which avoids depending on the Java. Perft also gives the speed numbers needed to judge the Udon performance concerns (see [Udon constraints](udon-constraints.md)).
- **Networking needs real clients.** ClientSim runs a single local player, so ownership, sync, late joiners and the `VRCObjectSync` behaviour have to be tested with several clients, e.g. VRChat's Build & Test with multiple instances of the client.
- Round-trip `Siggi-vs-Nuka.pgn` (currently in the Java repository's root; bring it along into the Unity project) through the Java and the port and compare the output.
