using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class InstallPlan
    {
        private readonly Dictionary<string, object> _fields;
        private readonly Dictionary<string, object>[] _changes;

        internal string Id => Json.Field<string>(_fields, "planId");
        internal int Revision => Json.Field<int>(_fields, "revision");
        internal string Hash => Json.Field<string>(_fields, "planHash");
        internal string Project => Json.Field<string>(_fields, "projectFingerprint");
        internal string Manifest => Json.Field<string>(_fields, "baselineManifestHash");
        internal string Lock => Json.Field<string>(_fields, "baselineLockHash");
        internal string Source => Json.Field<string>(_changes[0], "sourceUri");
        internal DateTimeOffset Created { get; }
        internal DateTimeOffset Expires { get; }

        private InstallPlan(Dictionary<string, object> fields, Dictionary<string, object>[] changes, DateTimeOffset created, DateTimeOffset expires)
        {
            _fields = fields;
            _changes = changes;
            Created = created;
            Expires = expires;
        }

        internal static InstallPlan Create(InstallContext context)
        {
            var change = Json.Object(("packageId", DependencyCatalog.PackageId), ("sourceUri", DependencyCatalog.Source), ("versionOrCommit", DependencyCatalog.Commit), ("expectedVersion", DependencyCatalog.Version), ("reason", "未導入のPLATEAU SDKを追加"), ("licenseUrl", DependencyCatalog.License));
            var fields = Json.Object(("schemaVersion", 1), ("planId", Guid.NewGuid().ToString("N")), ("revision", 1), ("projectFingerprint", context.Project), ("createdAt", Digests.Timestamp(context.Now)), ("expiresAt", Digests.Timestamp(context.Now.AddMinutes(10))), ("baselineManifestHash", context.Manifest), ("baselineLockHash", context.Lock), ("changes", new[] { change }), ("warnings", new[] { "manifestとlockが変更され、再コンパイルとDomain Reloadが発生する場合があります。", "推移的な依存パッケージも変更される場合があります。" }));
            fields.Add("planHash", Digests.Sha256(Json.Utf8.GetBytes(Json.Write(fields))));
            return Parse(Json.Write(fields));
        }

        internal static InstallPlan Parse(string json)
        {
            var fields = Json.Parse(json);
            RequireKeys(fields, "schemaVersion", "planId", "revision", "planHash", "projectFingerprint", "createdAt", "expiresAt", "baselineManifestHash", "baselineLockHash", "changes", "warnings");
            var created = Date(fields, "createdAt");
            var expires = Date(fields, "expiresAt");
            if (Json.Field<int>(fields, "schemaVersion") != 1 || Json.Field<int>(fields, "revision") < 1 || expires <= created)
            {
                throw new FormatException("Invalid plan version, revision or time window.");
            }

            RequirePattern(fields, "planId", @"[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}");
            RequirePattern(fields, "planHash", "[a-f0-9]{64}");
            RequirePattern(fields, "projectFingerprint", "[a-f0-9]{64}");
            RequirePattern(fields, "baselineManifestHash", "[a-f0-9]{64}");
            if (fields["baselineLockHash"] != null)
            {
                RequirePattern(fields, "baselineLockHash", "[a-f0-9]{64}");
            }

            var entries = Json.Field<object[]>(fields, "changes");
            var warnings = Json.Field<object[]>(fields, "warnings");
            if (entries == null || entries.Length == 0 || warnings == null)
            {
                throw new FormatException("Invalid plan arrays.");
            }

            var changes = new Dictionary<string, object>[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var change = entries[i] as Dictionary<string, object>;
                RequireKeys(change, "packageId", "sourceUri", "versionOrCommit", "expectedVersion", "reason", "licenseUrl");
                RequirePattern(change, "packageId", @"[a-z0-9]+(?:\.[a-z0-9][a-z0-9_-]*){2,}");
                RequirePattern(change, "sourceUri", @"https://[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+/[A-Za-z0-9._/-]+#[A-Za-z0-9._-]+");
                RequirePattern(change, "versionOrCommit", @"(?:v?[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?|[a-f0-9]{40})");
                RequirePattern(change, "expectedVersion", @"[A-Za-z0-9][A-Za-z0-9._+-]{0,127}");
                RequirePattern(change, "licenseUrl", @"https://[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+/[A-Za-z0-9._/-]+");
                RequireText(change["reason"]);
                if (!Json.Field<string>(change, "sourceUri").EndsWith("#" + Json.Field<string>(change, "versionOrCommit"), StringComparison.Ordinal))
                {
                    throw new FormatException("Git reference mismatch.");
                }
                changes[i] = change;
            }

            foreach (var warning in warnings)
            {
                RequireText(warning);
            }

            var plan = new InstallPlan(fields, changes, created, expires);
            if (plan.Hash != Digests.Sha256(plan.CanonicalBytes()))
            {
                throw new FormatException("Plan hash mismatch.");
            }
            return plan;
        }

        internal byte[] CanonicalBytes()
        {
            var payload = new Dictionary<string, object>(_fields, StringComparer.Ordinal);
            payload.Remove("planHash");
            return Json.Utf8.GetBytes(Json.Write(payload));
        }

        internal string ToJson() => Json.Write(_fields);

        internal bool CanExecute(InstallContext context)
        {
            var change = _changes[0];
            return context.CanAdd && Project == context.Project && Manifest == context.Manifest && Lock == context.Lock && Created <= context.Now && context.Now < Expires && _changes.Length == 1 && Json.Field<string>(change, "packageId") == DependencyCatalog.PackageId && Source == DependencyCatalog.Source && Json.Field<string>(change, "versionOrCommit") == DependencyCatalog.Commit && Json.Field<string>(change, "expectedVersion") == DependencyCatalog.Version && Json.Field<string>(change, "licenseUrl") == DependencyCatalog.License;
        }

        private static DateTimeOffset Date(Dictionary<string, object> fields, string key)
        {
            if (!DateTimeOffset.TryParseExact(Json.Field<string>(fields, key), Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                throw new FormatException("Invalid plan timestamp.");
            }
            return date;
        }

        private static void RequireKeys(Dictionary<string, object> fields, params string[] keys)
        {
            if (fields == null || fields.Count != keys.Length || Array.Exists(keys, key => !fields.ContainsKey(key)))
            {
                throw new FormatException("Unexpected or missing JSON property.");
            }
        }

        private static void RequirePattern(Dictionary<string, object> fields, string key, string pattern)
        {
            var value = Json.Field<string>(fields, key);
            int limit = key == "packageId" || key == "versionOrCommit" ? 128 : 512;
            if (value == null || value.Length > limit || !Regex.IsMatch(value, @"\A" + pattern + @"\z", RegexOptions.CultureInvariant))
            {
                throw new FormatException("Invalid plan property.");
            }
        }

        private static void RequireText(object value)
        {
            if (!(value is string text) || text.Length == 0 || text.Length > 512 || Array.Exists(text.ToCharArray(), char.IsControl))
            {
                throw new FormatException("Invalid plan text.");
            }
        }
    }
}
