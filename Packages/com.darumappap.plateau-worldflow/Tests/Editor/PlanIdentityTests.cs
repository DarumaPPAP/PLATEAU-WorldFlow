using System;
using System.IO;
using NUnit.Framework;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// Pythonの契約検証と共有する固定Planを使い、承認対象と重複判定のハッシュを検証する。
    /// </summary>
    internal sealed class PlanIdentityTests
    {
        private readonly DateTimeOffset _now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        private readonly PlanIdentity _identity = new PlanIdentity();

        [Test]
        public void ValidatesSharedPlanAndRejectsExpiredOrDifferentProject()
        {
            var plan = ReadFixture();
            var wire = Json.Write(plan);
            var project = (string)plan["projectFingerprint"];
            Assert.That(_identity.Seal(plan), Is.EqualTo(wire));
            Assert.That(_identity.Accepts(wire, project, _now), Is.True);
            Assert.That(_identity.Accepts(wire, project, _now.AddMinutes(5)), Is.False);
            Assert.That(_identity.Accepts(wire, new string('8', 64), _now), Is.False);
        }

        [Test]
        public void ReissuedPlanKeepsScopeAndChangesImmutableHash()
        {
            var plan = ReadFixture();
            var oldHash = plan["planHash"];
            var scope = plan["operationKey"];
            var output = (Fields)plan["output"];
            string replacementId = "00000000-0000-4000-8000-000000000003";
            output["scenePath"] = ((string)output["scenePath"]).Replace((string)plan["importPlanId"], replacementId);
            plan["importPlanId"] = replacementId;
            plan["expiresAt"] = "2026-11-16T00:06:00Z";
            var selection = (Fields)plan["selection"];
            Array.Reverse((object[])selection["gridCodes"]);
            var feature = (Fields)((object[])selection["featureSettings"])[0];
            Array.Reverse((object[])feature["lods"]);

            var reissued = Json.Parse(_identity.Seal(plan));
            Assert.That(reissued["operationKey"], Is.EqualTo(scope));
            Assert.That(reissued["planHash"], Is.Not.EqualTo(oldHash));
            Assert.That(_identity.Accepts(Json.Write(reissued), (string)plan["projectFingerprint"], _now), Is.True);

            feature["doSetMeshCollider"] = false;
            var changed = Json.Parse(_identity.Seal(plan));
            Assert.That(changed["operationKey"], Is.Not.EqualTo(scope));
            changed["operationKey"] = scope;
            changed.Remove("planHash");
            changed["planHash"] = Digests.Sha256(Json.Utf8.GetBytes(Json.Write(changed)));
            Assert.That(_identity.Accepts(Json.Write(changed), (string)plan["projectFingerprint"], _now), Is.False);
        }

        [Test]
        public void RejectsResealedPlanIfDestinationOrApprovalFlagIsInvalid()
        {
            var plan = ReadFixture();
            var project = (string)plan["projectFingerprint"];
            var output = (Fields)plan["output"];
            var sealedPath = output["scenePath"];
            output["scenePath"] = "Assets/Scenes/SampleScene.unity";
            Assert.That(_identity.Accepts(_identity.Seal(plan), project, _now), Is.False);
            output["scenePath"] = sealedPath;
            plan["approved"] = true;
            Assert.That(_identity.Accepts(_identity.Seal(plan), project, _now), Is.False);
        }

        private Fields ReadFixture()
        {
            var repo = Environment.GetEnvironmentVariable("WORLDFLOW_REPOSITORY");
            if (string.IsNullOrEmpty(repo))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlanIdentityTests).Assembly);
                repo = Path.GetFullPath(Path.Combine(package.resolvedPath, "..", ".."));
            }

            return Json.Parse(File.ReadAllText(Path.Combine(repo, "tests", "fixtures", "import", "import-plan.json"), Json.Utf8));
        }
    }
}
