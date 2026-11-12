using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class ProjectSetupTests
    {
        private string _project;
        private string Manifest => Path.Combine(_project, "Packages", "manifest.json");
        private string Lock => Path.Combine(_project, "Packages", "packages-lock.json");

        [SetUp]
        public void CreateProject()
        {
            _project = Path.Combine(Path.GetTempPath(), "WorldFlowSetupTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_project, "Packages"));
            File.WriteAllText(Manifest, "{\"dependencies\":{}}");
            File.WriteAllText(Lock, "{\"dependencies\":{}}");
        }

        [TearDown]
        public void RemoveProject()
        {
            Directory.Delete(_project, true);
        }

        [Test]
        public void RefusingAPlanLeavesBothFilesByteIdentical()
        {
            var store = new JobStore(_project);
            var before = new ProjectSnapshot(_project);
            var context = Context(store, before);
            var plan = InstallPlan.Create(context);

            string outcome = new InstallGate().Run(plan, () => context, false, () => false, () => store.Begin(plan, before), _ => Assert.Fail("UPM called"));

            Assert.That(outcome, Is.EqualTo("APPROVAL_DECLINED"));
            Assert.That(store.HasPending(), Is.False);
            Assert.That(new ProjectSnapshot(_project).SameFiles(before), Is.True);
        }

        [Test]
        public void ReopenedStorePreservesPendingJobAndPreventsAnotherReservation()
        {
            var before = new ProjectSnapshot(_project);
            var store = new JobStore(_project);
            var plan = InstallPlan.Create(Context(store, before));
            store.Begin(plan, before);
            var original = store.ReadPending();

            var reopened = new JobStore(_project);
            var diagnostic = new SdkDiagnostic(reopened.Fingerprint, before, null, true, true, null, "ENVIRONMENT");

            Assert.That(reopened.Fingerprint, Is.EqualTo(store.Fingerprint));
            Assert.That(reopened.TryReconcile(reopened.ReadPending(), plan, before, (false, diagnostic.Status, diagnostic.Version, diagnostic.Inventory), true, false, true, true), Is.False);
            Assert.That(reopened.HasPending(), Is.True);
            Assert.That(Json.Field<string>(reopened.ReadPending(), "jobId"), Is.EqualTo(Json.Field<string>(original, "jobId")));
            Assert.Throws<IOException>(() => reopened.Begin(plan, before));
            Assert.That(new ProjectSnapshot(_project).SameFiles(before), Is.True);
        }

        [Test]
        public void ResolvedSdkWithoutCapabilityBecomesPartialAfterSettling()
        {
            var store = new JobStore(_project);
            var before = new ProjectSnapshot(_project);
            var plan = InstallPlan.Create(Context(store, before));
            store.Begin(plan, before);
            store.ObserveRequest(null);
            var job = store.ReadPending();
            var diagnostic = (true, "UNKNOWN", DependencyCatalog.Version, "{}");

            Assert.That(store.TryReconcile(job, plan, before, diagnostic, true, false, true, false), Is.False);
            Assert.That(store.HasPending(), Is.True);
            Assert.That(store.TryReconcile(job, plan, before, diagnostic, true, false, true, true), Is.True);

            string id = Json.Field<string>(job, "jobId");
            var result = Json.Parse(File.ReadAllText(Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Setup", id, "result.json")));
            Assert.That(Json.Field<string>(result, "state"), Is.EqualTo("PARTIAL"));
            var diagnostics = Json.Field<object[]>(result, "diagnostics");
            Assert.That(Json.Field<string>((Dictionary<string, object>)diagnostics[0], "code"), Is.EqualTo("IMPORT_CAPABILITY_UNCONFIRMED"));
            Assert.That(store.HasPending(), Is.False);
        }

        [Test]
        public void ProjectPathBindsFingerprintEvenWithCopiedIdentity()
        {
            var original = new JobStore(_project);
            string copy = Path.Combine(_project, "clone");
            const string relativeStore = "Library/PLATEAUWorldFlow/Setup";
            Directory.CreateDirectory(Path.Combine(copy, relativeStore));
            File.Copy(Path.Combine(_project, relativeStore, "project-id"), Path.Combine(copy, relativeStore, "project-id"));

            Assert.That(new JobStore(copy).Fingerprint, Is.Not.EqualTo(original.Fingerprint));
        }

        [Test]
        public void ExistingEmbeddedSdkIsIncompatible()
        {
            Directory.CreateDirectory(Path.Combine(_project, "Packages", DependencyCatalog.PackageId));
            var snapshot = new ProjectSnapshot(_project);
            var inventory = new SdkDiagnostic(new JobStore(_project).Fingerprint, snapshot, null, true, true, null, "ENVIRONMENT");

            Assert.That(snapshot.HasSdk, Is.True);
            Assert.That(inventory.Status, Is.EqualTo("INCOMPATIBLE"));
        }

        [TestCase(false, "FAILED")]
        [TestCase(true, "PARTIAL")]
        public void ConfirmedUpmFailureReportsWhetherProjectFilesChanged(bool changed, string expected)
        {
            var store = new JobStore(_project);
            var snapshot = new ProjectSnapshot(_project);
            var plan = InstallPlan.Create(Context(store, snapshot));
            store.Begin(plan, snapshot);
            store.ObserveRequest("NETWORK_UNAVAILABLE");
            var reopened = new JobStore(_project);
            var job = reopened.ReadPending();
            if (changed)
            {
                File.WriteAllText(Lock, "{\"dependencies\":{}}\n");
            }
            var current = new ProjectSnapshot(_project);
            var diagnostic = new SdkDiagnostic(reopened.Fingerprint, current, null, true, true, null, "ENVIRONMENT");
            Assert.That(reopened.TryReconcile(job, plan, current, (diagnostic.ResolvedPinnedSdk, diagnostic.Status, diagnostic.Version, diagnostic.Inventory), true, false, false, false), Is.True);

            string id = Json.Field<string>(job, "jobId");
            var result = Json.Parse(File.ReadAllText(Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Setup", id, "result.json")));
            Assert.That(Json.Field<string>(result, "state"), Is.EqualTo(expected));
            Assert.That(reopened.HasPending(), Is.False);
        }

        [TestCase("fatal: could not resolve host", "NETWORK_UNAVAILABLE")]
        [TestCase("git-lfs smudge error", "GIT_LFS_FAILED")]
        [TestCase("fatal: repository unavailable", "UPM_ADD_FAILED")]
        public void UpmErrorIsClassifiedWithoutIncludingOriginalMessage(string message, string expected)
        {
            Assert.That(Bootstrap.ClassifyUpmFailure(message), Is.EqualTo(expected));
        }

        private InstallContext Context(JobStore store, ProjectSnapshot snapshot)
        {
            return new InstallContext(store.Fingerprint, snapshot.ManifestHash, snapshot.LockHash, DateTimeOffset.UtcNow, !snapshot.HasSdk);
        }
    }
}
