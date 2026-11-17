using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using DarumaPPAP.PlateauWorldFlow.Plateau;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Tests.Plateau
{
    /// <summary>
    /// 関連ZIPをJob専用の依存フォルダへ展開し、範囲外パスを拒否することを検証する。
    /// </summary>
    internal sealed class RemoteStagingTests
    {
        [TestCase("codes.xml")]
        [TestCase("codelists/codes.xml")]
        [TestCase("city_2025/codelists/codes.xml")]
        [TestCase("city_2025_codelists/codelists/codes.xml")]
        public void ExpandsKnownLayoutsWithoutChangingDestination(string entry)
        {
            string root = CreateFixture();
            try
            {
                string path = CreateArchive(root, entry);
                var staging = new RemoteStaging();
                staging.Extract(root, path, "codelists", "city_2025", CancellationToken.None);
                Assert.That(File.ReadAllText(Path.Combine(root, "codelists", "codes.xml")), Is.EqualTo("<codes />"));
                Assert.That(() => staging.Extract(root, path, "codelists", "city_2025", CancellationToken.None), Throws.TypeOf<IOException>());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase("../escaped.xml")]
        [TestCase("C:/escaped.xml")]
        [TestCase("codelists/CON.xml")]
        public void RejectsUnsafeArchiveEntry(string entry)
        {
            string root = CreateFixture();
            try
            {
                string path = CreateArchive(root, entry);
                Assert.That(() => new RemoteStaging().Extract(root, path, "codelists", "city_2025", CancellationToken.None), Throws.TypeOf<InvalidDataException>());
                Assert.That(Directory.GetFileSystemEntries(root), Is.EqualTo(new[] { path }));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private string CreateFixture()
        {
            return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "WorldFlowRemote-" + Guid.NewGuid().ToString("N"))).FullName;
        }

        private string CreateArchive(string root, string entry)
        {
            string path = Path.Combine(root, "fixture.zip");
            using var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("<codes />");
            return path;
        }
    }
}
