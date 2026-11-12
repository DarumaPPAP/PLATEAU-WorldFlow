using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class ReadonlyTests
    {
        [Test]
        public void FakeSourceMatchesSharedGolden()
        {
            var repository = Environment.GetEnvironmentVariable("WORLDFLOW_REPOSITORY");
            if (string.IsNullOrEmpty(repository))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReadonlyTests).Assembly);
                repository = Path.GetFullPath(Path.Combine(package.resolvedPath, "..", ".."));
            }

            var golden = Json.Parse(File.ReadAllText(Path.Combine(repository, "tests", "fixtures", "phase1", "readonly-golden.json"), Json.Utf8));
            var dataset = Json.Field<Dictionary<string, object>>(golden, "dataset");
            var cells = Json.Field<object[]>(golden, "cells").Cast<Dictionary<string, object>>().Select(cell =>
            {
                var extent = Json.Field<Dictionary<string, object>>(cell, "extent");
                return new CoverageCell(new GridCode((string)cell["code"]), new Extent((int)extent["south"], (int)extent["west"], (int)extent["north"], (int)extent["east"], (string)extent["crs"]),
                    Json.Field<object[]>(cell, "featureTypes")?.Cast<string>(), Json.Field<object[]>(cell, "availableLods")?.Cast<int>());
            });
            var entry = new Dataset((string)dataset["datasetId"], (string)dataset["title"], Json.Field<object[]>(dataset, "featureTypes").Cast<string>(), null, null, null, null, null);
            var observed = new Observation(entry, cells, Json.Field<object[]>(golden, "missing").Cast<string>().Select(code => new GridCode(code)));
            var now = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
            var coordinator = new Coordinator(new string('1', 64));
            var source = new Catalog("LOCAL", "local-01", now, now.AddMinutes(5), new[] { observed });
            CoverageSnapshot snapshot = null;
            foreach (Dictionary<string, object> test in Json.Field<object[]>(golden, "cases"))
            {
                var request = Json.Field<Dictionary<string, object>>(test, "request");
                var at = Json.Field<string>(test, "at");
                var checkedAt = at == null ? now : DateTimeOffset.ParseExact(at, Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                var actual = coordinator.Execute(Json.Write(request), source, checkedAt, snapshot, out var inspected);
                Assert.That(actual, Is.EqualTo(Json.Write(test["response"])), Json.Write(request));
                snapshot = inspected ?? snapshot;
            }
        }

        [TestCase(-91, 139, 36, 140, "EPSG:4326")]
        [TestCase(35, 141, 36, 140, "EPSG:4326")]
        [TestCase(36, 139, 36, 140, null)]
        [TestCase(double.NaN, 139, 36, 140, null)]
        [TestCase(35, 139, 36, 140, "D:/private")]
        public void InvalidExtentIsRejected(double south, double west, double north, double east, string crs)
        {
            Assert.Throws<ArgumentException>(() => new Extent(south, west, north, east, crs));
        }
    }
}
