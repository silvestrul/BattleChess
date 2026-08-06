using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BattleChess.Unity.Editor
{
    /// <summary>
    /// Builds the battlefield scene from a menu item.
    /// </summary>
    /// <remarks>
    /// A generated scene rather than a checked-in one. Unity scene files are
    /// large binary-ish YAML that merge badly and are miserable to review, and
    /// this scene holds nothing worth keeping — a camera and one component. Far
    /// better to regenerate it in a second than to version it.
    /// </remarks>
    public static class BattlefieldSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Battlefield.unity";

        [MenuItem("Battle Chess/Open Battlefield Scene %#b")]
        public static void OpenOrCreate()
        {
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            Create();
        }

        [MenuItem("Battle Chess/Rebuild Battlefield Scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(CameraRig));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -100f);

            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 300f;
            camera.farClipPlane = 500f;

            var controller = new GameObject("Battlefield", typeof(BattlefieldController));
            controller.GetComponent<BattlefieldController>().BattleName = "ford";

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"Battlefield scene created at {ScenePath}. Press Play.");
        }
    }
}
