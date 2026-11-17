using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// Sceneの保存経路が衝突や未ロードのSceneを検出し、既存成果物を変更しないことを確認する。
    /// </summary>
    internal sealed class SceneStageTests
    {
        [Test]
        public void RefusesOccupiedOutputBeforeSavingAnyAsset()
        {
            string fixture = $"Library/WorldFlowSceneStageTests/{Guid.NewGuid():N}";
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", fixture));
            Directory.CreateDirectory(root);
            string sentinel = Path.Combine(root, "City.unity");
            File.WriteAllText(sentinel, "fixture");
            try
            {
                var stage = new SceneStage(fixture + "/City.unity");
                var exception = Assert.Throws<IOException>(() => stage.SaveAndVerify(default, default, 9, null));
                Assert.That(exception.Message, Is.EqualTo("ASSET_DESTINATION_CONFLICT"));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("fixture"));
                Assert.That(Directory.GetFileSystemEntries(root), Is.EqualTo(new[] { sentinel }));
            }
            finally
            {
                File.Delete(sentinel);
                Directory.Delete(root);
            }
        }

        [Test]
        public void RefusesAnUnloadedSceneBeforeWritingItsDestination()
        {
            string path = $"Assets/WorldFlowSceneStageTest-{Guid.NewGuid():N}.unity";
            var exception = Assert.Throws<InvalidOperationException>(() => new SceneSave().SaveNew(default, path));
            Assert.That(exception.Message, Is.EqualTo("SCENE_SAVE_REQUIRES_NEW_SCENE"));
            Assert.That(File.Exists(Path.Combine(Application.dataPath, "..", path)), Is.False);
        }
    }
}
