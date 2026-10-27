using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiChessBoard {
    // Wires the placed chess prefabs to each other (PLAN.md 14.11, docs/prefabs.md): fills each empty reference from
    // the one object of its kind in the same scene (a board's and a settings menu's arbiter, a history wall's archive,
    // the arbiter's archives, the menu's and a 2D view's boards), gives each board without a number of its own the next
    // free one, and sets each arbiter's Board Count to its highest board number plus one. Runs when a scene is saved,
    // when Play Mode is entered and before every VRChat build; a reference it can't fill (no candidate, or several) is
    // logged as an error and cancels the build, with a dialog. Its changes are undoable and saved with the scene. The
    // I18n fields are the I18n sanity check's (docs/prefabs.md, Chess Language). Plain editor C#, not Udon.
    [InitializeOnLoad]
    public class ChessSceneWiring : IVRCSDKBuildRequestedCallback {
        // Before ChessArbiterBuildCheck and ChessBoard2DViewSyncCheck, so they see the wired scene.
        public int callbackOrder => -10;

        // What one run did to a scene: the changes and the references it couldn't fill (each with its object).
        public class Result {
            public List<string> changes = new List<string>();
            public List<string> problems = new List<string>();
            public List<Object> problemObjects = new List<Object>();
            public List<string> warnings = new List<string>();
        }

        // While set, saving a scene doesn't wire it: for a test saving a scene to see Play Mode wire it.
        public static bool skipOnSave;

        static ChessSceneWiring() {
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnSceneSaving(Scene scene, string path) {
            if (skipOnSave || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Report(Wire(scene), false);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change) {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            foreach (Scene scene in LoadedScenes()) Report(Wire(scene), false);
        }

        // The same on demand, for the open scenes.
        [MenuItem("Tools/Chess Board/Wire Chess Objects")]
        static void WireOpenScenes() {
            int changes = 0;
            int problems = 0;
            foreach (Scene scene in LoadedScenes()) {
                Result result = Wire(scene);
                Report(result, false);
                changes += result.changes.Count;
                problems += result.problems.Count;
            }
            Debug.Log("[ChessSceneWiring] Wired the open scenes: " + changes + " change(s), " + problems
                + " reference(s) left empty.");
        }

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;
            Result all = new Result();
            foreach (Scene scene in LoadedScenes()) {
                Result result = Wire(scene);
                all.changes.AddRange(result.changes);
                all.problems.AddRange(result.problems);
                all.problemObjects.AddRange(result.problemObjects);
                all.warnings.AddRange(result.warnings);
            }
            return Report(all, true);
        }

        // Logs what a run changed and couldn't fill; with dialog (a build), also selects the first object it couldn't
        // wire and explains in a dialog. Whether nothing was left unfilled.
        public static bool Report(Result result, bool dialog) {
            foreach (string change in result.changes) Debug.Log("[ChessSceneWiring] " + change);
            foreach (string warning in result.warnings) Debug.LogWarning("[ChessSceneWiring] " + warning);
            for (int i = 0; i < result.problems.Count; i++) {
                Debug.LogError("[ChessSceneWiring] " + result.problems[i] + (dialog ? " Build cancelled." : ""),
                    result.problemObjects[i]);
            }
            if (result.problems.Count == 0) return true;
            if (!dialog) return false;
            GameObject first = ObjectOf(result.problemObjects[0]);
            if (first != null) {
                Selection.activeObject = first;
                EditorGUIUtility.PingObject(first);
            }
            string list = "";
            foreach (string problem in result.problems) list += "\n  • " + problem;
            EditorUtility.DisplayDialog("Chess: build cancelled", "Some chess objects couldn't be linked up "
                + "automatically:\n" + list + "\n\n"
                + "Each needs exactly one object of the kind it points at in the scene. Add the one that's missing "
                + "(the prefabs are in Assets/Siggi/ChessBoard/Prefabs), remove the extra ones, or drag the right one "
                + "into the empty field in the Inspector yourself. The first object is selected now. Then build again.",
                "OK");
            return false;
        }

        // Wires the chess objects in scene (inactive ones included); records each change for undo and marks the scene
        // dirty when anything changed.
        public static Result Wire(Scene scene) {
            Result result = new Result();
            if (!scene.isLoaded) return result;
            Undo.SetCurrentGroupName("Wire chess objects");
            List<ChessArbiter> arbiters = InScene<ChessArbiter>(scene);
            List<ChessBoard> boards = InScene<ChessBoard>(scene);
            List<InstanceGameArchive> instanceArchives = InScene<InstanceGameArchive>(scene);
            List<PersonalGameArchive> personalArchives = InScene<PersonalGameArchive>(scene);
            List<GameArchive> archives = InScene<GameArchive>(scene);
            HashSet<ChessBoard> wired = new HashSet<ChessBoard>();
            foreach (ChessBoard board in boards) if (board.arbiter != null) wired.Add(board);

            foreach (ChessBoard board in boards) {
                if (board.arbiter != null) continue;
                ChessArbiter arbiter = Single(arbiters, board, "Arbiter", "Chess Arbiter", result);
                if (arbiter != null) Set(board, "Arbiter", arbiter.gameObject.name, result, () => board.arbiter = arbiter);
            }

            foreach (ChessArbiter arbiter in arbiters) {
                if (!Empty(arbiter.archives)) {
                    foreach (GameArchive archive in archives) {
                        if (System.Array.IndexOf(arbiter.archives, archive) >= 0) continue;
                        result.warnings.Add(PathOf(arbiter.transform) + ": " + PathOf(archive.transform) + " isn't in "
                            + "its Archives, so the boards' games don't reach it. Add it there, or empty Archives to "
                            + "have every archive in the scene listed.");
                    }
                } else if (archives.Count > 0) {
                    GameArchive[] all = archives.ToArray();
                    Set(arbiter, "Archives", Names(all), result, () => arbiter.archives = all);
                }
            }

            foreach (GameHistoryWall wall in InScene<GameHistoryWall>(scene)) {
                if (wall.archive != null) continue;
                // The personal wall is the one with delete buttons.
                GameArchive archive = wall.deleteButtons != null && wall.deleteButtons.Length > 0
                    ? (GameArchive)Single(personalArchives, wall, "Archive", "Personal Game Archive", result)
                    : Single(instanceArchives, wall, "Archive", "Game Archive", result);
                if (archive != null) Set(wall, "Archive", archive.gameObject.name, result, () => wall.archive = archive);
            }

            NumberBoards(arbiters, boards, wired, result);

            foreach (ChessSettingsMenu menu in InScene<ChessSettingsMenu>(scene)) {
                if (menu.arbiter == null) {
                    ChessArbiter arbiter = Single(arbiters, menu, "Arbiter", "Chess Arbiter", result);
                    if (arbiter != null) Set(menu, "Arbiter", arbiter.gameObject.name, result, () => menu.arbiter = arbiter);
                }
                if (menu.arbiter == null) continue;
                ChessBoard[] onArbiter = ByNumber(boards, menu.arbiter);
                if (Empty(menu.boards)) {
                    if (onArbiter.Length > 0) {
                        Set(menu, "Boards", Names(onArbiter), result, () => menu.boards = onArbiter);
                    }
                    continue;
                }
                foreach (ChessBoard board in onArbiter) {
                    // An inactive board isn't played, so it's not missed.
                    if (!board.gameObject.activeInHierarchy || System.Array.IndexOf(menu.boards, board) >= 0) continue;
                    result.warnings.Add(PathOf(menu.transform) + ": " + PathOf(board.transform) + " isn't in its "
                        + "Boards, so Apply now doesn't reach it. Add it there, or empty Boards to have every board "
                        + "listed.");
                }
            }

            foreach (ChessBoard2DView view in InScene<ChessBoard2DView>(scene)) {
                if (view.lockedBoard != null || !Empty(view.boards)) continue;
                ChessBoard[] all = ByNumber(boards, null);
                if (all.Length == 0) {
                    AddProblem(result, view, "Boards", "there's no Chess Board in the scene");
                    continue;
                }
                Set(view, "Boards", Names(all), result, () => view.boards = all);
            }

            if (result.changes.Count > 0) EditorSceneManager.MarkSceneDirty(scene);
            return result;
        }

        // Gives each board without a number of its own (one another board on its arbiter already has, or a negative
        // one) the lowest free number, and sets each arbiter's Board Count to its highest board number plus one. Of
        // boards sharing a number, one whose number was set (not a prefab instance's untouched 0) keeps it, then one
        // wired before (its arbiter set before this run, in wired), so a table dropped in beside numbered ones takes a
        // new number; otherwise the first in the hierarchy keeps it.
        static void NumberBoards(List<ChessArbiter> arbiters, List<ChessBoard> boards, HashSet<ChessBoard> wired,
            Result result) {
            List<ChessArbiter> groups = new List<ChessArbiter>(arbiters);
            groups.Add(null);
            foreach (ChessArbiter arbiter in groups) {
                List<ChessBoard> own = new List<ChessBoard>();
                foreach (ChessBoard board in boards) if (board.arbiter == arbiter) own.Add(board);
                List<ChessBoard> keep = new List<ChessBoard>();
                foreach (ChessBoard board in own) if (NumberSet(board)) keep.Add(board);
                foreach (ChessBoard board in own) if (!NumberSet(board) && wired.Contains(board)) keep.Add(board);
                foreach (ChessBoard board in own) if (!NumberSet(board) && !wired.Contains(board)) keep.Add(board);
                HashSet<int> used = new HashSet<int>();
                List<ChessBoard> renumber = new List<ChessBoard>();
                foreach (ChessBoard board in keep) {
                    if (board.boardNumber >= 0 && used.Add(board.boardNumber)) continue;
                    renumber.Add(board);
                }
                int next = 0;
                foreach (ChessBoard board in renumber) {
                    while (used.Contains(next)) next++;
                    int number = next;
                    used.Add(number);
                    int old = board.boardNumber;
                    Set(board, "Board Number", number + " (was " + old + ", another board's)", result,
                        () => board.boardNumber = number);
                }
                if (arbiter == null || own.Count == 0) continue;
                int count = 0;
                foreach (ChessBoard board in own) count = Mathf.Max(count, board.boardNumber + 1);
                if (count == arbiter.boardCount) continue;
                int was = arbiter.boardCount;
                Set(arbiter, "Board Count", count + " (" + (count > was ? "raised" : "lowered") + " from " + was
                    + ", for board numbers 0 to " + (count - 1) + ")", result, () => arbiter.boardCount = count);
            }
        }

        // Whether a board's number was set on it: an override on a prefab instance, or any board that isn't one.
        static bool NumberSet(ChessBoard board) {
            if (!PrefabUtility.IsPartOfPrefabInstance(board)) return true;
            SerializedProperty number = new SerializedObject(board).FindProperty("boardNumber");
            return number != null && number.prefabOverride;
        }

        // The one candidate for owner's field, or null after recording the problem (none, or several by path).
        static T Single<T>(List<T> candidates, Component owner, string field, string kind, Result result)
            where T : Component {
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0) {
                AddProblem(result, owner, field, "there's no " + kind + " in the scene");
            } else {
                string paths = "";
                foreach (T candidate in candidates) paths += (paths.Length > 0 ? ", " : "") + PathOf(candidate.transform);
                AddProblem(result, owner, field, "there are " + candidates.Count + " " + kind + "s to choose from ("
                    + paths + ")");
            }
            return null;
        }

        static void AddProblem(Result result, Component owner, string field, string why) {
            result.problems.Add(PathOf(owner.transform) + ": its " + field + " is empty, and " + why + ".");
            result.problemObjects.Add(owner);
        }

        // Sets a field of behaviour through apply, undoably, copies it to the Udon data and records it as an override
        // on a prefab instance.
        static void Set(UdonSharpBehaviour behaviour, string field, string value, Result result, System.Action apply) {
            Object udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            Undo.RecordObjects(udon != null ? new Object[] { behaviour, udon } : new Object[] { behaviour },
                "Wire chess objects");
            apply();
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) {
                PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
                if (udon != null) PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            }
            EditorUtility.SetDirty(behaviour);
            result.changes.Add(PathOf(behaviour.transform) + ": " + field + " set to " + value + ".");
        }

        // The boards on arbiter (every board when null), by board number.
        static ChessBoard[] ByNumber(List<ChessBoard> boards, ChessArbiter arbiter) {
            List<ChessBoard> list = new List<ChessBoard>();
            foreach (ChessBoard board in boards) if (arbiter == null || board.arbiter == arbiter) list.Add(board);
            List<ChessBoard> sorted = new List<ChessBoard>(list);
            // Stable, so boards sharing a number stay in hierarchy order.
            sorted.Sort((a, b) => a.boardNumber != b.boardNumber
                ? a.boardNumber.CompareTo(b.boardNumber) : list.IndexOf(a).CompareTo(list.IndexOf(b)));
            return sorted.ToArray();
        }

        // Whether a list holds no object (empty, or only empty entries).
        static bool Empty(Object[] list) {
            if (list == null) return true;
            foreach (Object entry in list) if (entry != null) return false;
            return true;
        }

        static string Names(Component[] list) {
            string names = "";
            foreach (Component entry in list) names += (names.Length > 0 ? ", " : "") + entry.gameObject.name;
            return names;
        }

        // Every T in scene, inactive ones included, in hierarchy order.
        public static List<T> InScene<T>(Scene scene) where T : Component {
            List<T> all = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<T>(true));
            return all;
        }

        public static List<Scene> LoadedScenes() {
            List<Scene> scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) scenes.Add(scene);
            }
            return scenes;
        }

        static GameObject ObjectOf(Object o) {
            if (o is GameObject gameObject) return gameObject;
            return o is Component component ? component.gameObject : null;
        }

        public static string PathOf(Transform t) {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
