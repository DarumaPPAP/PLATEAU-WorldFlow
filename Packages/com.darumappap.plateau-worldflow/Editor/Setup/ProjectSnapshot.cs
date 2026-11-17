using System;
using System.Collections.Generic;
using System.IO;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class ProjectSnapshot
    {
        internal string ManifestHash { get; }
        internal string LockHash { get; }
        internal Dictionary<string, object> Manifest { get; }
        internal Dictionary<string, object> Lock { get; }
        internal bool HasEmbeddedSdk { get; }
        internal bool HasSdk => HasEmbeddedSdk || Manifest.ContainsKey(DependencyCatalog.PackageId) || Lock?.ContainsKey(DependencyCatalog.PackageId) == true;
        internal string SdkRequest => Json.Field<string>(Manifest, DependencyCatalog.PackageId);
        internal Dictionary<string, object> SdkLock => Json.Field<Dictionary<string, object>>(Lock, DependencyCatalog.PackageId);

        internal ProjectSnapshot(string project)
        {
            string packages = Path.Combine(project, "Packages");
            byte[] manifest = File.ReadAllBytes(Path.Combine(packages, "manifest.json"));
            ManifestHash = Digests.Sha256(manifest);
            Manifest = Dependencies(manifest);
            var lockFile = ReadLock(Path.Combine(packages, "packages-lock.json"));
            LockHash = lockFile == null ? null : Digests.Sha256(lockFile);
            Lock = lockFile == null ? null : Dependencies(lockFile);
            HasEmbeddedSdk = Directory.Exists(Path.Combine(packages, DependencyCatalog.PackageId));
        }

        internal bool SameFiles(ProjectSnapshot other) => ManifestHash == other.ManifestHash && LockHash == other.LockHash;

        private static Dictionary<string, object> Dependencies(byte[] bytes)
        {
            string text = Json.Utf8.GetString(bytes);
            var fields = Json.Parse(text.TrimStart('\uFEFF'));
            return Json.Field<Dictionary<string, object>>(fields, "dependencies") ?? throw new FormatException("Missing UPM dependencies.");
        }

        private static byte[] ReadLock(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

    }
}
