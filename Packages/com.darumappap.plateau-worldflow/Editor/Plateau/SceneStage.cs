using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PLATEAU.CityInfo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// Importで所有するScene内の一時オブジェクトを永続化し、Sceneを再読込して参照を確認する。
    /// </summary>
    internal sealed class SceneStage
    {
        private readonly Dictionary<Object, Object> _assets = new Dictionary<Object, Object>();
        private readonly string _scenePath;
        private readonly string _folder;
        private readonly string _project;

        internal SceneStage(string scenePath)
        {
            _scenePath = scenePath;
            _folder = scenePath.Substring(0, scenePath.LastIndexOf('/'));
            _project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// 承認済みフォルダだけに生成Mesh、Material、TextureおよびSceneを保存し、再読込結果を返す。
        /// </summary>
        internal bool SaveAndVerify(Scene scene, Scene previous, int zone, SceneSave sceneSave)
        {
            if (DestinationExists(_folder))
            {
                throw new IOException("ASSET_DESTINATION_CONFLICT");
            }

            EnsureFolder(_folder);
            var roots = scene.GetRootGameObjects();
            foreach (var filter in Components<MeshFilter>(roots))
            {
                filter.sharedMesh = Persist(filter.sharedMesh);
            }

            foreach (var collider in Components<MeshCollider>(roots))
            {
                collider.sharedMesh = Persist(collider.sharedMesh);
            }

            foreach (var renderer in Components<Renderer>(roots))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(PersistMaterial).ToArray();
            }

            int meshCount = Components<MeshFilter>(roots).Count(filter => filter.sharedMesh != null);
            int objectCount = Components<Transform>(roots).Count();
            var priorMaterials = Components<Renderer>(roots).SelectMany(renderer => renderer.sharedMaterials).ToArray();
            int textureCount = Textures(priorMaterials).Count();
            if (DestinationExists(_scenePath) || !sceneSave.SaveNew(scene, _scenePath))
            {
                throw new IOException("SCENE_SAVE_FAILED");
            }

            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }

            if (!EditorSceneManager.CloseScene(scene, true))
            {
                throw new IOException("SCENE_CLOSE_FAILED");
            }

            var reopened = EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Additive);
            var reopenedRoots = reopened.GetRootGameObjects();
            var models = Components<PLATEAUInstancedCityModel>(reopenedRoots).ToArray();
            var meshes = Components<MeshFilter>(reopenedRoots).ToArray();
            var colliders = Components<MeshCollider>(reopenedRoots).ToArray();
            var materials = Components<Renderer>(reopenedRoots).SelectMany(renderer => renderer.sharedMaterials).ToArray();
            var textures = Textures(materials).ToArray();
            return reopened.isLoaded && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(_scenePath)) && objectCount == Components<Transform>(reopenedRoots).Count()
                && meshCount > 0 && meshes.Length == meshCount && meshes.All(mesh => IsPersistent(mesh.sharedMesh)) && colliders.All(collider => IsPersistent(collider.sharedMesh))
                && models.Length == 1 && models[0].GeoReference.ZoneID == zone && materials.Length > 0 && materials.Length == priorMaterials.Length && materials.All(IsPersistent)
                && textures.Length == textureCount && textures.All(IsPersistent);
        }

        private IEnumerable<T> Components<T>(IEnumerable<GameObject> roots) where T : Component
        {
            return roots.SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        private IEnumerable<Texture> Textures(IEnumerable<Material> materials)
        {
            return materials.Where(material => material != null).SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture)).Where(texture => texture != null);
        }

        private bool IsPersistent(Object asset)
        {
            return asset != null && (AssetDatabase.Contains(asset) || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)));
        }

        private Material PersistMaterial(Material original)
        {
            // SDKが参照した既存Assetはそのまま読み取り、新規のMaterialだけを複製して保存する。
            if (original == null || IsPersistent(original)) return original;
            // 複数Rendererが共有する生成Materialには同じ保存Assetを割り当てる。
            if (_assets.TryGetValue(original, out var existing)) return (Material)existing;

            var material = Object.Instantiate(original);
            foreach (string property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture != null && !IsPersistent(texture))
                {
                    if (!(texture is Texture2D image))
                    {
                        throw new InvalidDataException("TEXTURE_PERSISTENCE_UNVERIFIED");
                    }
                    material.SetTexture(property, Persist(image));
                }
            }

            SaveAsset(original, material);
            return material;
        }

        private T Persist<T>(T original) where T : Object
        {
            // 永続化済みの参照や欠落した参照には新規Assetを割り当てない。
            if (original == null || IsPersistent(original)) return original;
            // 同じ生成Assetが他のObjectから参照される場合は、同じ複製に結び付ける。
            if (_assets.TryGetValue(original, out var existing)) return (T)existing;

            var clone = Object.Instantiate(original);
            SaveAsset(original, clone);
            return clone;
        }

        private void SaveAsset(Object original, Object clone)
        {
            string path = $"{_folder}/Generated-{_assets.Count:D6}.asset";
            if (DestinationExists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                throw new IOException("ASSET_DESTINATION_CONFLICT");
            }

            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssetIfDirty(clone);
            _assets.Add(original, clone);
        }

        private void EnsureFolder(string folder)
        {
            // 既存の正常な親フォルダはImport用に再利用できる。
            if (AssetDatabase.IsValidFolder(folder)) return;

            int separator = folder.LastIndexOf('/');
            if (separator < 0 || DestinationExists(folder))
            {
                throw new IOException("ASSET_DESTINATION_CONFLICT");
            }

            string parent = folder.Substring(0, separator);
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
            if (AssetDatabase.GUIDToAssetPath(guid) != folder)
            {
                throw new IOException("ASSET_FOLDER_CREATION_FAILED");
            }
        }

        private bool DestinationExists(string relativePath)
        {
            string path = Path.Combine(_project, relativePath);
            return Directory.Exists(path) || File.Exists(path) || File.Exists(path + ".meta");
        }
    }
}
