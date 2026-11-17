using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarumaPPAP.PlateauWorldFlow.Import;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Entry
{
    /// <summary>
    /// 保存された都市モデルのメッシュだけをEditorプレビューで撮影し、PNGをJobに記録する。
    /// </summary>
    internal sealed class SceneCapture
    {
        private const int Resolution = 768;

        /// <summary>
        /// 保存Sceneの斜めと上方の2視点を撮影する。描画環境や一部視点の失敗は撮影状態へ記録する。
        /// </summary>
        internal (string Status, Fields[] Manifest) Record(Journal journal, string jobId, string planHash, string scenePath, string sceneGuid)
        {
            // 画像を生成できない環境でも、保存済みSceneのImport結果は成立する。
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return ("BLOCKED", Array.Empty<Fields>());

            var captures = new List<Fields>();
            var scene = default(Scene);
            try
            {
                scene = EditorSceneManager.OpenPreviewScene(scenePath);
                var roots = scene.GetRootGameObjects();
                var lowerLods = new HashSet<Renderer>();
                foreach (var group in roots.SelectMany(root => root.GetComponentsInChildren<LODGroup>()))
                {
                    var levels = group.GetLODs();
                    var nearest = levels.Length > 0 ? levels[0].renderers : Array.Empty<Renderer>();
                    lowerLods.UnionWith(levels.Skip(1).SelectMany(level => level.renderers).Except(nearest));
                }

                var meshes = roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>()).Select(filter => (Filter: filter, Renderer: filter.GetComponent<MeshRenderer>())).Where(item => item.Filter.sharedMesh != null && item.Renderer != null && item.Renderer.enabled && !lowerLods.Contains(item.Renderer)).ToArray();
                // Sceneを開けても描画対象を確定できなければ、背景だけの画像は作らない。
                if (meshes.Length == 0) return ("BLOCKED", Array.Empty<Fields>());

                var bounds = meshes[0].Renderer.bounds;
                foreach (var item in meshes)
                {
                    bounds.Encapsulate(item.Renderer.bounds);
                }
                float radius = bounds.extents.magnitude;
                if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0)
                {
                    return ("BLOCKED", Array.Empty<Fields>());
                }

                foreach (var view in new[] { ("oblique", new Vector3(0.9f, 0.65f, 1f)), ("overhead", new Vector3(0f, 1f, 0.08f)) })
                {
                    try
                    {
                        byte[] png = Render(meshes, bounds.center, radius, view.Item2);
                        captures.Add(journal.WriteCapture(jobId, planHash, view.Item1, png, sceneGuid, DateTimeOffset.UtcNow));
                    }
                    catch (Exception exception) when (CanBlockCapture(exception))
                    {
                        // 他の視点は独立に撮影・保存できる。
                    }
                }
            }
            catch (Exception exception) when (CanBlockCapture(exception))
            {
                // 保存SceneをGPUで開けなかった場合も、Import結果をそのまま返す。
            }
            finally
            {
                if (scene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            string status = captures.Count == 2 ? "READY" : captures.Count == 1 ? "PARTIAL" : "BLOCKED";
            return (status, captures.ToArray());
        }

        private byte[] Render((MeshFilter Filter, MeshRenderer Renderer)[] meshes, Vector3 center, float radius, Vector3 direction)
        {
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            try
            {
                var camera = preview.camera;
                camera.transform.SetPositionAndRotation(center + direction.normalized * (3f * radius), Quaternion.LookRotation(-direction, Vector3.up));
                camera.orthographic = true;
                camera.orthographicSize = radius * 1.05f;
                camera.nearClipPlane = Mathf.Max(0.01f, radius * 0.05f);
                camera.farClipPlane = Mathf.Max(1f, radius * 5f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.19f, 0.22f);
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                preview.lights[1].intensity = 0.55f;
                preview.lights[1].transform.rotation = Quaternion.Euler(30f, 140f, 0f);
                preview.ambientColor = new Color(0.5f, 0.5f, 0.5f);
                preview.BeginStaticPreview(new Rect(0, 0, Resolution, Resolution));
                try
                {
                    foreach (var item in meshes)
                    {
                        var mesh = item.Filter.sharedMesh;
                        var materials = item.Renderer.sharedMaterials;
                        for (int index = 0; index < mesh.subMeshCount && index < materials.Length; index++)
                        {
                            if (materials[index] != null)
                            {
                                preview.RenderMesh(mesh, item.Filter.transform.localToWorldMatrix, materials[index], index);
                            }
                        }
                    }
                    preview.Render(allowScriptableRenderPipeline: true, updatefov: false);
                }
                finally
                {
                    image = preview.EndStaticPreview();
                }

                var pixels = image.GetPixels32();
                if (pixels.Length == 0 || pixels.All(pixel => pixel.Equals(pixels[0])))
                {
                    throw new InvalidDataException("CAPTURE_HAS_NO_GEOMETRY");
                }
                return image.EncodeToPNG();
            }
            finally
            {
                if (image != null)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
                preview.Cleanup();
            }
        }

        private bool CanBlockCapture(Exception exception)
        {
            return exception is UnityException || exception is InvalidOperationException || exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException;
        }
    }
}
