using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine.SceneManagement;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 新規のImport Sceneを保存するときだけ、UnityのScene Template設定の遅延初期化を避ける。
    /// </summary>
    internal sealed class SceneSave
    {
        private readonly EditorSceneManager.SceneSavedCallback _templateSaved;

        internal SceneSave()
        {
            // Unity 6000.3のSceneTemplateServiceは、新規SceneにもsceneSavedでテンプレート一覧を照会する。
            var service = typeof(SceneTemplateAsset).Assembly.GetType("UnityEditor.SceneTemplate.SceneTemplateService", throwOnError: false);
            var method = service?.GetMethod("OnSceneSaved", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Scene) }, null);
            if (method == null || method.ReturnType != typeof(void))
            {
                throw new InvalidOperationException("SCENE_TEMPLATE_CALLBACK_UNVERIFIED");
            }
            _templateSaved = (EditorSceneManager.SceneSavedCallback)Delegate.CreateDelegate(typeof(EditorSceneManager.SceneSavedCallback), method);
        }

        /// <summary>
        /// WorldFlow所有の未保存Sceneを保存する。他のsceneSaved購読者は通常通り呼び出す。
        /// </summary>
        internal bool SaveNew(Scene scene, string path)
        {
            if (!scene.IsValid() || !scene.isLoaded || !string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("SCENE_SAVE_REQUIRES_NEW_SCENE");
            }

            void HandleOtherScene(Scene saved)
            {
                if (saved != scene)
                {
                    _templateSaved(saved);
                }
            }

            EditorSceneManager.sceneSaved -= _templateSaved;
            EditorSceneManager.sceneSaved += HandleOtherScene;
            try
            {
                return EditorSceneManager.SaveScene(scene, path);
            }
            finally
            {
                EditorSceneManager.sceneSaved -= HandleOtherScene;
                EditorSceneManager.sceneSaved += _templateSaved;
            }
        }
    }
}
