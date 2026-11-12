using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class JobStore
    {
        private const string RelativeRoot = "Library/PLATEAUWorldFlow/Setup";
        private readonly string _root;
        private readonly string _pending;
        internal string Fingerprint { get; }

        internal JobStore(string project)
        {
            _root = Path.Combine(project, RelativeRoot);
            _pending = Path.Combine(_root, "pending.json");
            Directory.CreateDirectory(_root);
            var identityFile = Path.Combine(_root, "project-id");
            if (!File.Exists(identityFile))
            {
                try
                {
                    WriteNew(identityFile, Guid.NewGuid().ToString("N"));
                }
                catch (IOException) when (File.Exists(identityFile))
                {
                    // The first writer owns the persistent project identifier.
                }
            }
            var identity = File.ReadAllText(identityFile);
            if (!Guid.TryParseExact(identity, "N", out _))
            {
                throw new FormatException("Invalid project identity.");
            }
            Fingerprint = Digests.Sha256(Json.Utf8.GetBytes(project + "\n" + identity));
        }

        internal void WriteInventory(SdkDiagnostic diagnostic) => WriteAtomic(Path.Combine(_root, "inventory.json"), diagnostic.Inventory);

        internal void WritePlan(InstallPlan plan) => WriteAtomic(Path.Combine(_root, "plan.json"), plan.ToJson());

        internal void RemovePlan() => File.Delete(Path.Combine(_root, "plan.json"));

        internal void Begin(InstallPlan plan, ProjectSnapshot snapshot)
        {
            if (snapshot.ManifestHash != plan.Manifest || snapshot.LockHash != plan.Lock)
            {
                throw new InvalidOperationException("The package files changed before reserving the job.");
            }
            var job = Json.Object(("jobId", Guid.NewGuid().ToString("N")), ("plan", Json.Parse(plan.ToJson())), ("manifestEntries", EntryHashes(snapshot.Manifest)), ("lockEntries", EntryHashes(snapshot.Lock)), ("stage", "DISPATCH_PENDING"));
            WriteNew(_pending, Json.Write(job));
        }

        internal Dictionary<string, object> ReadPending()
        {
            try
            {
                return Json.Parse(File.ReadAllText(_pending));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        internal bool HasPending()
        {
            // Treat malformed or inaccessible job records as errors, never as permission to dispatch.
            return ReadPending() != null;
        }

        internal void ObserveRequest(string failure)
        {
            var job = ReadPending() ?? throw new InvalidOperationException("Missing job for completed UPM request.");
            job["stage"] = failure == null ? "ADD_SUCCEEDED" : "ADD_FAILED";
            job["failure"] = failure;
            WriteAtomic(_pending, Json.Write(job));
        }

        internal static string GetObservedFailure(Dictionary<string, object> job)
        {
            switch (Json.Field<string>(job, "failure"))
            {
                case "GIT_LFS_FAILED": return "GIT_LFS_FAILED";
                case "NETWORK_UNAVAILABLE": return "NETWORK_UNAVAILABLE";
                case "GIT_UNAVAILABLE": return "GIT_UNAVAILABLE";
                case "UPM_ADD_FAILED": return "UPM_ADD_FAILED";
                default: return null;
            }
        }

        internal void CancelBeforeDispatch(InstallPlan plan, ProjectSnapshot current)
        {
            var job = ReadPending();
            var stored = InstallPlan.Parse(Json.Write(job["plan"]));
            if (stored.Hash != plan.Hash || stored.Id != plan.Id)
            {
                throw new InvalidOperationException("The pending job does not match the approved plan.");
            }
            Complete(job, plan, current, null, "PLAN_INVALIDATED_BEFORE_DISPATCH");
        }

        internal void Complete(Dictionary<string, object> job, InstallPlan plan, ProjectSnapshot current, SdkDiagnostic diagnostic, string failure)
        {
            string id = Json.Field<string>(job, "jobId");
            if (!Guid.TryParseExact(id, "N", out _) || plan.Project != Fingerprint)
            {
                throw new FormatException("Invalid persisted job identity.");
            }

            bool changed = current.ManifestHash != plan.Manifest || current.LockHash != plan.Lock;
            bool resolved = diagnostic?.ResolvedPinnedSdk == true;
            string state = diagnostic == null ? "CANCELLED" : resolved && failure == null ? "COMPLETED" : changed || resolved ? "PARTIAL" : "FAILED";
            var directory = Path.Combine(_root, id);
            Directory.CreateDirectory(directory);
            var manifestChanges = EntryDiff(Json.Field<Dictionary<string, object>>(job, "manifestEntries"), EntryHashes(current.Manifest));
            var lockChanges = EntryDiff(Json.Field<Dictionary<string, object>>(job, "lockEntries"), EntryHashes(current.Lock));
            var evidence = Json.Object(("manifestBefore", plan.Manifest), ("manifestAfter", current.ManifestHash), ("manifestDependencyDiff", manifestChanges), ("lockBefore", plan.Lock), ("lockAfter", current.LockHash), ("lockDependencyDiff", lockChanges), ("pinnedSdkResolved", resolved));
            WriteAtomic(Path.Combine(directory, "changes.json"), Json.Write(evidence));
            var evidencePaths = new List<string> { $"{RelativeRoot}/{id}/changes.json" };
            if (diagnostic != null)
            {
                WriteAtomic(Path.Combine(directory, "inventory.json"), diagnostic.Inventory);
                evidencePaths.Add($"{RelativeRoot}/{id}/inventory.json");
            }
            string location = failure == "COMPILE_OR_CAPABILITY_FAILED" ? "COMPILE" : diagnostic == null ? "PLAN" : "UPM";
            var diagnostics = failure == null ? Array.Empty<object>() : new[] { SdkDiagnostic.Diagnostic(failure, location, state == "FAILED") };
            var package = Json.Object(("packageId", DependencyCatalog.PackageId), ("state", diagnostic == null ? "NOT_ATTEMPTED" : resolved ? "ADDED" : "FAILED"), ("resolvedVersion", diagnostic?.Version));
            var result = Json.Object(("schemaVersion", 1), ("jobId", id), ("planId", plan.Id), ("planHash", plan.Hash), ("projectFingerprint", Fingerprint), ("state", state), ("packages", new[] { package }), ("evidencePaths", evidencePaths), ("diagnostics", diagnostics));
            WriteAtomic(Path.Combine(directory, "result.json"), Json.Write(result));
            File.Delete(_pending);
            UnityEngine.Debug.Log($"[WorldFlow] Setup {state} job={id} plan={plan.Hash} manifestChanged={current.ManifestHash != plan.Manifest} lockChanged={current.LockHash != plan.Lock}.");
        }

        private static Dictionary<string, object> EntryHashes(Dictionary<string, object> dependencies)
        {
            return dependencies?.ToDictionary(pair => Digests.Sha256(Json.Utf8.GetBytes(pair.Key)), pair => (object)Digests.Sha256(Json.Utf8.GetBytes(Json.Write(pair.Value))), StringComparer.Ordinal) ?? Json.Object();
        }

        private static Dictionary<string, object> EntryDiff(Dictionary<string, object> before, Dictionary<string, object> after)
        {
            return Json.Object(("added", after.Keys.Count(key => !before.ContainsKey(key))), ("removed", before.Keys.Count(key => !after.ContainsKey(key))), ("changed", before.Keys.Count(key => after.TryGetValue(key, out var value) && !Equals(before[key], value))));
        }

        private static void WriteNew(string path, string json)
        {
            var bytes = Json.Utf8.GetBytes(json);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes, 0, bytes.Length);
            file.Flush(true);
        }

        private static void WriteAtomic(string path, string json)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteNew(temporary, json);
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
