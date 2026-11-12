using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class SetupTests
    {
        private string _fixtures;
        private static readonly DateTimeOffset ValidTime = DateTimeOffset.Parse("2026-11-11T00:05:00Z");
        private static readonly string Project = new string('1', 64);
        private static readonly string Manifest = new string('2', 64);

        [SetUp]
        public void LocateSharedFixtures()
        {
            var repository = Environment.GetEnvironmentVariable("WORLDFLOW_REPOSITORY");
            if (string.IsNullOrEmpty(repository))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SetupTests).Assembly);
                repository = Path.GetFullPath(Path.Combine(package.resolvedPath, "..", ".."));
            }

            _fixtures = Path.Combine(repository, "tests", "fixtures", "dependency-install-plan");
            Assert.That(Directory.Exists(_fixtures), Is.True, "Set WORLDFLOW_REPOSITORY to the checkout containing the shared fixtures.");
        }

        [Test]
        public void CanonicalBytesAndHashMatchSharedGoldenFixture()
        {
            var plan = Read("valid");
            Assert.That(plan.CanonicalBytes(), Is.EqualTo(File.ReadAllBytes(Path.Combine(_fixtures, "golden-canonical.txt"))));
            Assert.That(plan.Hash, Is.EqualTo(Digests.Sha256(plan.CanonicalBytes())));
        }

        [TestCase("approval-boolean")]
        [TestCase("non-integer-revision")]
        [TestCase("invalid-url")]
        [TestCase("hash-mismatch")]
        public void InvalidPlanIsRejected(string fixture)
        {
            Assert.Throws<FormatException>(() => Read(fixture));
        }

        [TestCase("\"__type\":\"hidden\",")]
        [TestCase("\"revision\":1,")]
        public void UnexpectedOrDuplicateRootFieldIsRejected(string prefix)
        {
            var json = File.ReadAllText(Path.Combine(_fixtures, "valid.json"), Encoding.UTF8);
            Assert.Throws<FormatException>(() => InstallPlan.Parse("{" + prefix + json.Substring(1)));
        }

        [TestCase("{\"revision\":01}")]
        [TestCase("{\"revision\":NaN}")]
        [TestCase("{\"revision\":1,}")]
        [TestCase("{\"items\":[1,]}")]
        [TestCase("{\"key\":\"\\ud800\"}")]
        public void MalformedJsonIsRejected(string json)
        {
            Assert.Throws<FormatException>(() => Json.Parse(json));
        }

        [TestCase("outside-allowlist")]
        [TestCase("different-project")]
        [TestCase("expired")]
        public void InvalidContextCannotReachAdd(string fixture)
        {
            var plan = Read(fixture);
            int calls = 0;
            var gate = new InstallGate();
            var outcome = gate.Run(plan, () => Context(), false, () => true, () => { }, _ => calls++);
            Assert.That(outcome, Is.EqualTo("PLAN_INVALID"));
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ValidPinnedPlanCannotReachAddInAnotherProject()
        {
            var plan = InstallPlan.Create(Context());
            var other = new InstallContext(new string('f', 64), Manifest, null, ValidTime, true);
            string outcome = new InstallGate().Run(plan, () => other, false, () => throw new AssertionException("Unexpected dialog"), () => Assert.Fail("Job created"), _ => Assert.Fail("UPM called"));
            Assert.That(outcome, Is.EqualTo("PLAN_INVALID"));
        }

        [Test]
        public void AnAppearingLockFileInvalidatesAnOtherwiseValidPlan()
        {
            var plan = InstallPlan.Create(Context());
            var context = new InstallContext(Project, Manifest, new string('f', 64), ValidTime, true);
            string outcome = new InstallGate().Run(plan, () => context, false, () => throw new AssertionException("Unexpected dialog"), () => Assert.Fail("Job created"), _ => Assert.Fail("UPM called"));
            Assert.That(outcome, Is.EqualTo("PLAN_INVALID"));
        }

        [Test]
        public void RefusalAndBatchNeverPersistOrAdd()
        {
            var gate = new InstallGate();
            int dialogs = 0;
            int writes = 0;
            int adds = 0;
            var plan = InstallPlan.Create(Context());
            Assert.That(gate.Run(plan, () => Context(), true, () => { dialogs++; return true; }, () => writes++, _ => adds++), Is.EqualTo("BATCH_MODE"));
            Assert.That(dialogs, Is.Zero);
            Assert.That(gate.Run(plan, () => Context(), false, () => { dialogs++; return false; }, () => writes++, _ => adds++), Is.EqualTo("APPROVAL_DECLINED"));
            Assert.That(dialogs, Is.EqualTo(1));
            Assert.That(writes, Is.Zero);
            Assert.That(adds, Is.Zero);
        }

        [Test]
        public void BaselineAndExpiryAreCheckedAfterDialog()
        {
            var gate = new InstallGate();
            int calls = 0;
            var plan = InstallPlan.Create(Context());
            var context = Context();
            Assert.That(gate.Run(plan, () => context, false, () =>
            {
                context = new InstallContext(Project, new string('3', 64), null, ValidTime, true);
                return true;
            }, () => { }, _ => calls++), Is.EqualTo("PLAN_INVALID"));
            context = Context();
            Assert.That(gate.Run(plan, () => context, false, () =>
            {
                context = new InstallContext(Project, Manifest, null, plan.Expires, true);
                return true;
            }, () => { }, _ => calls++), Is.EqualTo("PLAN_INVALID"));
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void NestedRequestCannotOpenAnotherDialogOrAdd()
        {
            var gate = new InstallGate();
            var plan = InstallPlan.Create(Context());
            int adds = 0;
            Assert.That(gate.Run(plan, () => Context(), false, () =>
            {
                Assert.That(gate.Run(plan, () => Context(), false, () => throw new AssertionException("Second dialog"), () => { }, _ => adds++), Is.EqualTo("BUSY"));
                return true;
            }, () => { }, _ => adds++), Is.EqualTo("DISPATCHED"));
            Assert.That(adds, Is.EqualTo(1));
        }

        private InstallPlan Read(string name)
        {
            return InstallPlan.Parse(File.ReadAllText(Path.Combine(_fixtures, name + ".json"), Encoding.UTF8));
        }

        private InstallContext Context()
        {
            return new InstallContext(Project, Manifest, null, ValidTime, true);
        }

    }
}
