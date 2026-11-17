using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// Import予約の排他と破損時の停止を、テスト専用のProjectディレクトリで検証する。
    /// </summary>
    internal sealed class JournalTests
    {
        private string _project;
        private readonly string _fingerprint = new string('1', 64);
        private readonly DateTimeOffset _now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

        [SetUp]
        public void CreateFixture()
        {
            _project = Path.Combine(Path.GetTempPath(), "WorldFlowImportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
        }

        [TearDown]
        public void RemoveFixture()
        {
            Directory.Delete(_project, true);
        }

        /// <summary>
        /// 同じProjectで再予約しても同一Scopeは元Jobを返し、異なるScopeは実行中のJobと共存できない。
        /// </summary>
        [Test]
        public void RepeatedReservationReturnsOriginalJobAndOtherScopeIsBusy()
        {
            var journal = OpenJournal();
            var original = journal.Reserve(Guid.NewGuid().ToString("D"), new string('2', 64), new string('3', 64), _now);
            var reopened = OpenJournal();
            var repeated = reopened.Reserve(Guid.NewGuid().ToString("D"), new string('5', 64), new string('3', 64), _now);

            Assert.That(original.Created, Is.True);
            Assert.That(repeated.Created, Is.False);

            Assert.That(original.Snapshot["state"], Is.EqualTo("RESERVED"));
            Assert.That(repeated.Snapshot["jobId"], Is.EqualTo(original.Snapshot["jobId"]));
            Assert.That(repeated.Snapshot["planHash"], Is.EqualTo(original.Snapshot["planHash"]));
            Assert.Throws<InvalidOperationException>(() => Reserve(reopened, '4'));
        }

        /// <summary>
        /// 永続化した記録が破損していても、新しいJobで置き換えずに停止する。
        /// </summary>
        [Test]
        public void CorruptedJournalRejectsReservation()
        {
            var original = Reserve(OpenJournal(), '3');
            var record = Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Import", "jobs", (string)original["jobId"], "journal.json");
            File.WriteAllText(record, "{", Json.Utf8);

            Assert.Throws<FormatException>(() => Reserve(OpenJournal(), '3'));
            Assert.That(File.ReadAllText(record, Json.Utf8), Is.EqualTo("{"));
        }

        /// <summary>
        /// 別ProjectのJournalが混入した場合に、状態やJob IDを流用せず停止する。
        /// </summary>
        [Test]
        public void CopiedJournalDoesNotBelongToAnotherProject()
        {
            Reserve(OpenJournal(), '3');
            Assert.Throws<InvalidOperationException>(() => Reserve(OpenJournal(new string('5', 64)), '3'));
        }

        /// <summary>
        /// 独立したJournalインスタンスから同時に予約できるのは一件だけ。
        /// </summary>
        [Test]
        public async Task ConcurrentRequestsCreateOnlyOneJob()
        {
            var planId = Guid.NewGuid().ToString("D");
            var planHash = new string('2', 64);
            var key = new string('3', 64);
            var first = Task.Run(() => CompeteForReservation(planId, planHash, key));
            var second = Task.Run(() => CompeteForReservation(planId, planHash, key));
            var created = await Task.WhenAll(first, second);

            Assert.That(created[0] ^ created[1], Is.True);
            Assert.That(OpenJournal().Reserve(planId, planHash, key, _now).Created, Is.False);
        }

        /// <summary>
        /// 状態・Planの競合や検証の省略を拒否し、完了後の再送では元Jobを返す。
        /// </summary>
        [Test]
        public void StateUpdateRejectsConflictsAndPreservesCompletedReservation()
        {
            var journal = OpenJournal();
            var reserved = Reserve(journal, '3');
            var jobId = (string)reserved["jobId"];
            var planHash = (string)reserved["planHash"];
            Assert.Throws<InvalidOperationException>(() => Advance(journal, jobId, new string('8', 64), "RESERVED", "RUNNING"));
            Assert.Throws<InvalidOperationException>(() => Advance(journal, jobId, planHash, "RESERVED", "COMPLETED"));
            Advance(journal, jobId, planHash, "RESERVED", "RUNNING");
            Assert.Throws<InvalidOperationException>(() => Advance(journal, jobId, planHash, "RESERVED", "RUNNING"));
            Advance(journal, jobId, planHash, "RUNNING", "VERIFYING");
            Advance(journal, jobId, planHash, "VERIFYING", "COMPLETED");

            Assert.That(Reserve(OpenJournal(), '3')["state"], Is.EqualTo("COMPLETED"));
            Assert.That(Reserve(OpenJournal(), '4')["jobId"], Is.Not.EqualTo(jobId));
        }

        /// <summary>
        /// 中断記録を読み直しても、手動検査が必要なJobの再実行を許可しない。
        /// </summary>
        [Test]
        public void InterruptedJobBlocksNewReservationsUntilReconciled()
        {
            var journal = OpenJournal();
            var reserved = Reserve(journal, '3');
            var jobId = (string)reserved["jobId"];
            var planHash = (string)reserved["planHash"];
            Advance(journal, jobId, planHash, "RESERVED", "RUNNING");
            Advance(OpenJournal(), jobId, planHash, "RUNNING", "NEEDS_RECONCILIATION");
            Assert.That(Reserve(OpenJournal(), '3')["state"], Is.EqualTo("NEEDS_RECONCILIATION"));
            Assert.Throws<InvalidOperationException>(() => Reserve(OpenJournal(), '4'));
            Assert.Throws<InvalidOperationException>(() => Advance(OpenJournal(), jobId, planHash, "NEEDS_RECONCILIATION", "RUNNING"));
        }

        /// <summary>
        /// Jobの照会と成果物保存はPlan Hashに束縛し、別Planから変更や上書きができない。
        /// </summary>
        [Test]
        public void ArtifactsBelongToReservedJobAndDoNotOverwrite()
        {
            var journal = OpenJournal();
            Assert.That(journal.Find(new string('3', 64)), Is.Null);
            Assert.That(Directory.Exists(Path.Combine(_project, "Library")), Is.False);

            var reserved = Reserve(journal, '3');
            var jobId = (string)reserved["jobId"];
            var hash = (string)reserved["planHash"];
            Assert.That(journal.Find(new string('3', 64))["jobId"], Is.EqualTo(jobId));
            Assert.That(OpenJournal().Inspect(jobId, hash)["state"], Is.EqualTo("RESERVED"));
            Assert.Throws<InvalidOperationException>(() => journal.WriteArtifact(jobId, new string('8', 64), "plan.json", "{}"));
            journal.WriteArtifact(jobId, hash, "plan.json", "{\"scope\":1}");
            Assert.Throws<IOException>(() => journal.WriteArtifact(jobId, hash, "plan.json", "{\"scope\":2}"));
            Assert.That(File.ReadAllText(Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Import", "jobs", jobId, "plan.json")), Is.EqualTo("{\"scope\":1}"));
            Assert.That(OpenJournal().ReadArtifact(jobId, hash, "plan.json"), Is.EqualTo("{\"scope\":1}"));
            Assert.Throws<InvalidOperationException>(() => journal.ReadArtifact(jobId, new string('8', 64), "plan.json"));
        }

        /// <summary>
        /// Editor再起動に相当する別インスタンスで、実行中のJobを照合待ちへ変更する。
        /// </summary>
        [Test]
        public void ReopeningJournalRecoversInterruptedJobWithoutReleasingItsScope()
        {
            var journal = OpenJournal();
            journal.RecoverInterrupted(_now);
            Assert.That(Directory.Exists(Path.Combine(_project, "Library")), Is.False);
            var record = Reserve(journal, '3');
            string id = (string)record["jobId"];
            string hash = (string)record["planHash"];
            Advance(journal, id, hash, "RESERVED", "RUNNING");
            journal.RecoverInterrupted(_now.AddSeconds(2), id);
            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("RUNNING"));
            var reopened = OpenJournal();
            reopened.RecoverInterrupted(_now.AddSeconds(3));
            Assert.That(reopened.Find(new string('3', 64))["state"], Is.EqualTo("NEEDS_RECONCILIATION"));
            Assert.Throws<InvalidOperationException>(() => Reserve(reopened, '4'));
        }

        /// <summary>
        /// 同じJobとSceneの撮影だけを登録し、撮影後の改変を照会時に検出する。
        /// </summary>
        [Test]
        public void CaptureIntegrityIsScopedToJobAndScene()
        {
            var journal = OpenJournal();
            var reserved = Reserve(journal, '3');
            string id = (string)reserved["jobId"];
            string hash = (string)reserved["planHash"];
            string sceneGuid = new string('1', 32);
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+WcV0AAAAASUVORK5CYII=");
            Assert.Throws<InvalidDataException>(() => journal.WriteCapture(id, hash, "oblique", bytes, sceneGuid, _now));
            Advance(journal, id, hash, "RESERVED", "RUNNING");
            Advance(journal, id, hash, "RUNNING", "VERIFYING");
            var record = journal.WriteCapture(id, hash, "oblique", bytes, sceneGuid, _now);
            Assert.That(journal.MatchesCapture(id, hash, record, sceneGuid), Is.True);
            Assert.That(journal.MatchesCapture(id, hash, record, new string('2', 32)), Is.False);
            Assert.Throws<IOException>(() => journal.WriteCapture(id, hash, "oblique", bytes, sceneGuid, _now));

            string file = Path.Combine(_project, (string)record["relativePath"]);
            bytes[bytes.Length - 1] ^= 1;
            File.WriteAllBytes(file, bytes);
            Assert.That(journal.MatchesCapture(id, hash, record, sceneGuid), Is.False);
            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("VERIFYING"));
        }

        /// <summary>
        /// 手動確認して出力なしと確定した失敗Jobを保持し、別Planだけで同じ範囲を再予約できる。
        /// </summary>
        [Test]
        public void ManualReviewReleasesOnlyNewPlansAndPreservesHistory()
        {
            var journal = OpenJournal();
            var original = Reserve(journal, '3');
            string id = (string)original["jobId"];
            string hash = (string)original["planHash"];
            string planId = (string)original["importPlanId"];
            string scope = (string)original["operationKey"];
            Advance(journal, id, hash, "RESERVED", "RUNNING");
            Assert.Throws<InvalidOperationException>(() => journal.AllowRetryAfterManualReview(id, hash, _now.AddSeconds(2)));
            Advance(journal, id, hash, "RUNNING", "VERIFYING");
            Advance(journal, id, hash, "VERIFYING", "FAILED");
            Assert.That(journal.Find(scope)["jobId"], Is.EqualTo(id));

            string output = Path.Combine(_project, PlanIdentity.OutputRoot, planId);
            Directory.CreateDirectory(output);
            Assert.Throws<InvalidOperationException>(() => journal.AllowRetryAfterManualReview(id, hash, _now.AddSeconds(2)));
            Directory.Delete(output);

            journal.AllowRetryAfterManualReview(id, hash, _now.AddSeconds(2));
            Assert.That(journal.Find(scope), Is.Null);
            Assert.That(journal.Reserve(planId, hash, scope, _now.AddSeconds(3)).Created, Is.False);
            var retry = journal.Reserve(Guid.NewGuid().ToString("D"), new string('4', 64), scope, _now.AddSeconds(3));
            Assert.That(retry.Created, Is.True);
            Assert.That(OpenJournal().Find(scope)["jobId"], Is.EqualTo(retry.Snapshot["jobId"]));
            Assert.That(OpenJournal().Inspect(id, hash)["state"], Is.EqualTo("FAILED"));
        }

        /// <summary>
        /// 照合結果とEditor確認を要求し、元Jobの状態と生成物を一度だけ確定する。
        /// </summary>
        [TestCase(false, "FAILED")]
        [TestCase(true, "PARTIAL")]
        public void ReconciliationFinalizesOriginalJobAndReleasesOnlyOtherScopes(bool hasOutput, string expected)
        {
            var journal = OpenJournal();
            var job = InterruptedWithPlan(journal);
            string id = (string)job["jobId"];
            string hash = (string)job["planHash"];
            string scope = (string)job["operationKey"];
            string output = Path.Combine(_project, PlanIdentity.OutputRoot, (string)job["importPlanId"]);
            string asset = Path.Combine(output, "Generated-000000.asset");
            if (hasOutput)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(asset, "retained");
                File.WriteAllText(asset + ".meta", "retained metadata");
            }

            var review = journal.InspectReconciliation(id, hash, null, 0);
            Assert.That(review["nextState"], Is.EqualTo(expected));
            Assert.Throws<InvalidOperationException>(() => Reserve(journal, '5'));
            Assert.Throws<InvalidOperationException>(() => journal.ConfirmReconciliation(id, hash, review, null, 0, false, _now.AddSeconds(2)));
            Assert.Throws<InvalidOperationException>(() => journal.ConfirmReconciliation(id, new string('8', 64), review, null, 0, true, _now.AddSeconds(2)));
            Assert.Throws<InvalidOperationException>(() => OpenJournal(new string('8', 64)).ConfirmReconciliation(id, hash, review, null, 0, true, _now.AddSeconds(2)));
            Assert.Throws<InvalidOperationException>(() => journal.Advance(id, hash, "NEEDS_RECONCILIATION", expected, _now.AddSeconds(2)));
            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("NEEDS_RECONCILIATION"));

            Assert.That(journal.ConfirmReconciliation(id, hash, review, null, 0, true, _now.AddSeconds(2)), Is.EqualTo(expected));
            Assert.That(OpenJournal().ConfirmReconciliation(id, hash, review, null, 0, true, _now.AddSeconds(3)), Is.EqualTo(expected));
            Assert.That(journal.Reserve(Guid.NewGuid().ToString("D"), hash, scope, _now.AddSeconds(3)).Snapshot["jobId"], Is.EqualTo(id));
            string jobs = Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Import", "jobs");
            Assert.That(Directory.GetDirectories(jobs).Length, Is.EqualTo(1));
            var persisted = Json.Parse(File.ReadAllText(Path.Combine(jobs, id, "journal.json"), Json.Utf8));
            Assert.That(Json.Write(persisted["reconciliation"]), Is.EqualTo(Json.Write(review)));
            Assert.That(OpenJournal().Inspect(id, hash).Count, Is.EqualTo(9));
            if (hasOutput)
            {
                Assert.That(Directory.GetFiles(output).Length, Is.EqualTo(2));
                Assert.That(File.ReadAllText(asset), Is.EqualTo("retained"));
                Assert.Throws<InvalidOperationException>(() => journal.AllowRetryAfterManualReview(id, hash, _now.AddSeconds(3)));
            }
            else
            {
                journal.AllowRetryAfterManualReview(id, hash, _now.AddSeconds(3));
                Assert.That(journal.Find(scope), Is.Null);
            }
            Assert.That(Reserve(journal, '5')["jobId"], Is.Not.EqualTo(id));
        }

        /// <summary>
        /// 保存先が検査不能、所有不明、または確認内容から変化した場合は排他を維持する。
        /// </summary>
        [Test]
        public void ReconciliationRejectsUnknownAndChangedOutputs()
        {
            var journal = OpenJournal();
            var job = InterruptedWithPlan(journal);
            string id = (string)job["jobId"];
            string hash = (string)job["planHash"];
            var empty = journal.InspectReconciliation(id, hash, null, 0);
            string output = Path.Combine(_project, PlanIdentity.OutputRoot, (string)job["importPlanId"]);
            Directory.CreateDirectory(output);
            File.WriteAllText(output + ".meta", "folder metadata");
            string scene = Path.Combine(output, "City.unity");
            File.WriteAllText(scene, "scene");
            Assert.Throws<InvalidOperationException>(() => journal.ConfirmReconciliation(id, hash, empty, null, 0, true, _now.AddSeconds(2)));
            using (File.Open(scene, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                Assert.Throws<IOException>(() => journal.InspectReconciliation(id, hash, null, 0));
            }
            Directory.CreateDirectory(Path.Combine(output, "unexpected"));
            Assert.Throws<InvalidDataException>(() => journal.InspectReconciliation(id, hash, null, 0));
            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("NEEDS_RECONCILIATION"));
            Assert.Throws<InvalidOperationException>(() => Reserve(journal, '5'));
        }

        /// <summary>
        /// 帰属の検証に失敗したJobは出力先が空でも失敗確定できない。
        /// </summary>
        [Test]
        public void ReconciliationPreservesKnownOwnershipFailure()
        {
            var journal = OpenJournal();
            var job = InterruptedWithPlan(journal, ownershipUnverified: true);
            string id = (string)job["jobId"];
            string hash = (string)job["planHash"];

            Assert.Throws<InvalidDataException>(() => OpenJournal().InspectReconciliation(id, hash, null, 0));
            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("NEEDS_RECONCILIATION"));
            Assert.Throws<InvalidOperationException>(() => Reserve(journal, '5'));
        }

        private Dictionary<string, object> InterruptedWithPlan(Journal journal, bool ownershipUnverified = false)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(JournalTests).Assembly);
            string repo = Environment.GetEnvironmentVariable("WORLDFLOW_REPOSITORY") ?? Path.GetFullPath(Path.Combine(package.resolvedPath, "..", ".."));
            string wire = File.ReadAllText(Path.Combine(repo, "tests", "fixtures", "import", "import-plan.json"), Json.Utf8);
            var plan = Json.Parse(wire);
            string hash = (string)plan["planHash"];
            var job = journal.Reserve((string)plan["importPlanId"], hash, (string)plan["operationKey"], _now).Snapshot;
            string id = (string)job["jobId"];
            journal.WriteArtifact(id, hash, "plan.json", wire);
            journal.Advance(id, hash, "RESERVED", "NEEDS_RECONCILIATION", _now.AddSeconds(1), ownershipUnverified);
            return job;
        }

        /// <summary>
        /// Windowsの一時的な読み取り共有を待ち、同一Jobを照合待ちへ原子的に更新する。
        /// </summary>
        [Test]
        public async Task RecoveryRetriesWhenWindowsReaderDoesNotShareDelete()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Windows File.Replace sharing semantics are required.");
            }

            var journal = OpenJournal();
            var reserved = Reserve(journal, '3');
            string id = (string)reserved["jobId"];
            string hash = (string)reserved["planHash"];
            Advance(journal, id, hash, "RESERVED", "RUNNING");
            string directory = Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Import", "jobs", id);
            string path = Path.Combine(directory, "journal.json");
            string running = File.ReadAllText(path);

            using (var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => journal.RecoverInterrupted(_now.AddSeconds(2)));
                Assert.That(File.ReadAllText(path), Is.EqualTo(running));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }

            using var shortReader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pending = new ManualResetEventSlim(false);
            var recovery = Task.Run(() =>
            {
                pending.Set();
                journal.RecoverInterrupted(_now.AddSeconds(3));
            });
            try
            {
                Assert.That(pending.Wait(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(SpinWait.SpinUntil(() => Directory.GetFiles(directory, "*.tmp").Length == 1, TimeSpan.FromSeconds(3)), Is.True);
                Thread.Sleep(30);
                Assert.That(File.ReadAllText(path), Is.EqualTo(running));
            }
            finally
            {
                shortReader.Dispose();
                await recovery;
            }

            Assert.That(journal.Inspect(id, hash)["state"], Is.EqualTo("NEEDS_RECONCILIATION"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        private void Advance(Journal journal, string jobId, string hash, string before, string after)
        {
            journal.Advance(jobId, hash, before, after, _now.AddSeconds(1));
        }

        private bool CompeteForReservation(string planId, string planHash, string key)
        {
            try
            {
                return OpenJournal().Reserve(planId, planHash, key, _now).Created;
            }
            catch (IOException)
            {
                // 別の要求がProjectロックを保持していれば、この要求は予約を取得できない。
                return false;
            }
        }

        private Journal OpenJournal(string fingerprint = null)
        {
            return new Journal(_project, fingerprint ?? _fingerprint);
        }

        private Dictionary<string, object> Reserve(Journal journal, char scope)
        {
            return journal.Reserve(Guid.NewGuid().ToString("D"), new string('2', 64), new string(scope, 64), _now).Snapshot;
        }
    }
}
