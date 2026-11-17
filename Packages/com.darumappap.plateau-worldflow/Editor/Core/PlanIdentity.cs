using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// Import Planの内容と実効範囲を再計算し、提示時と実行時の照合に使う。
    /// </summary>
    internal sealed class PlanIdentity
    {
        internal const string OutputRoot = "Assets/PLATEAUWorldFlow/Imports";

        /// <summary>
        /// 事前照合済みの項目から、承認対象のハッシュと重複判定用のハッシュを付けたJSONを作る。
        /// </summary>
        internal string Seal(Fields draft)
        {
            var plan = new Fields(draft, StringComparer.Ordinal);
            plan["operationKey"] = ComputeScope(plan);
            plan.Remove("planHash");
            plan["planHash"] = Hash(plan);
            return Json.Write(plan);
        }

        /// <summary>
        /// 計画の形、対象Project、有効期限、保存先、二つのハッシュが一致することを確認する。
        /// </summary>
        internal bool Accepts(string document, string projectFingerprint, DateTimeOffset now)
        {
            // 入力と接続先の識別子がそろわなければ計画を照合できない。
            if (string.IsNullOrEmpty(document) || !WireValues.Hash(projectFingerprint)) return false;

            Fields plan;
            try
            {
                plan = Json.Parse(document);
            }
            catch (FormatException)
            {
                return false;
            }

            if (!HasFields(plan, "schemaVersion", "importPlanId", "revision", "projectFingerprint", "phase2", "source", "selection", "output", "expectedGmlManifestHash", "sdkVersion", "expiresAt", "operationKey", "planHash"))
            {
                return false;
            }

            string planId = Json.Field<string>(plan, "importPlanId");
            bool currentTarget = Json.Field<int>(plan, "schemaVersion") == 1 && WireValues.Uuid(planId) && Json.Field<int>(plan, "revision") > 0 && Json.Field<string>(plan, "projectFingerprint") == projectFingerprint && Json.Field<string>(plan, "sdkVersion") == "4.3.0";
            bool validUntil = DateTimeOffset.TryParseExact(Json.Field<string>(plan, "expiresAt"), Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiry) && now < expiry;
            string planHash = Json.Field<string>(plan, "planHash");
            string scopeHash = Json.Field<string>(plan, "operationKey");
            bool hashFields = WireValues.Hash(planHash) && WireValues.Hash(scopeHash) && WireValues.Hash(Json.Field<string>(plan, "expectedGmlManifestHash"));
            // 異なるProject・SDKや失効した計画では、元の承認内容を利用できない。
            if (!currentTarget || !validUntil || !hashFields) return false;

            var phase2 = Json.Field<Fields>(plan, "phase2");
            var source = Json.Field<Fields>(plan, "source");
            var selection = Json.Field<Fields>(plan, "selection");
            var output = Json.Field<Fields>(plan, "output");
            bool validPhase2 = HasFields(phase2, "planningSessionId", "planRevision", "planDigest") && WireValues.Uuid(Json.Field<string>(phase2, "planningSessionId")) && Json.Field<int>(phase2, "planRevision") > 0 && WireValues.Hash(Json.Field<string>(phase2, "planDigest"));
            bool validOutput = HasFields(output, "mode", "scenePath", "assetExport", "overwriteExisting") && Json.Field<string>(output, "scenePath") == $"{OutputRoot}/{planId}/City.unity" && Json.Field<string>(output, "mode") == "NEW_SCENE" && Json.Field<string>(output, "assetExport") == "NONE" && Equals(output["overwriteExisting"], false);
            // 解釈できない設定や許可範囲外の保存先をハッシュだけで受理しない。
            if (!validPhase2 || !validOutput || !IsValidSource(source) || !IsValidSelection(selection)) return false;

            plan.Remove("planHash");
            return Hash(plan) == planHash && ComputeScope(plan) == scopeHash;
        }

        private bool IsValidSource(Fields source)
        {
            // 指定外のパスや認証情報を計画の取得元に含めない。
            if (!HasFields(source, "kind", "worldFlowDatasetId", "sdkSourceId", "datasetVersion", "sourceFingerprint", "mappingStatus")) return false;

            string kind = Json.Field<string>(source, "kind");
            bool validSdkId = kind == "LOCAL" ? source["sdkSourceId"] == null : kind == "REMOTE" && WireValues.Id(Json.Field<string>(source, "sdkSourceId"));
            bool validVersion = source["datasetVersion"] == null || source["datasetVersion"] is string version && WireValues.Text(version);
            return validSdkId && validVersion && WireValues.Id(Json.Field<string>(source, "worldFlowDatasetId")) && WireValues.Hash(Json.Field<string>(source, "sourceFingerprint")) && Json.Field<string>(source, "mappingStatus") == "VERIFIED";
        }

        private bool IsValidSelection(Fields selection)
        {
            // 実際に適用するメッシュ・座標系・地物設定が明示済みの計画だけを取り扱う。
            if (!HasFields(selection, "gridCodes", "coordinateZoneId", "featureSettings", "qualityRequirementsStatus") || Json.Field<string>(selection, "qualityRequirementsStatus") != "VERIFIED") return false;

            int zone = Json.Field<int>(selection, "coordinateZoneId");
            var grids = Json.Field<object[]>(selection, "gridCodes");
            var features = Json.Field<object[]>(selection, "featureSettings");
            bool validGrids = grids != null && grids.Length > 0 && grids.Length <= WireValues.MaxGridCodes && grids.All(item => item is string code && WireValues.Grid(code)) && grids.Distinct().Count() == grids.Length;
            // 系番号が不正、または地物やメッシュが空の場合はSDKに渡せる選択がない。
            if (zone < 1 || zone > 19 || !validGrids || features == null || features.Length < 1 || features.Length > 64) return false;

            var featureTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in features)
            {
                var feature = item as Fields;
                // 地物ごとに適用する設定を一意に特定できなければ実行Scopeを決められない。
                if (!HasFields(feature, "featureType", "lods", "includeTexture", "enableTexturePacking", "doSetMeshCollider", "doSetAttrInfo", "meshGranularity")) return false;

                string type = Json.Field<string>(feature, "featureType");
                string granularity = Json.Field<string>(feature, "meshGranularity");
                var lods = Json.Field<object[]>(feature, "lods");
                bool validLods = lods != null && lods.Length > 0 && lods.Length <= 64 && lods.All(value => value is int lod && lod >= 0) && lods.Distinct().Count() == lods.Length;
                bool validGranularity = granularity == "PerAtomicFeatureObject" || granularity == "PerPrimaryFeatureObject" || granularity == "PerCityModelArea";
                bool validFlags = feature["includeTexture"] is bool texture && feature["enableTexturePacking"] is bool packing && (!packing || texture) && feature["doSetMeshCollider"] is bool && feature["doSetAttrInfo"] is bool;
                // 未指定の値や重複をSDKの既定値で補うと提示した設定が変わる。
                if (!WireValues.Id(type) || !featureTypes.Add(type) || !validLods || !validGranularity || !validFlags) return false;
            }

            return true;
        }

        private string ComputeScope(Fields plan)
        {
            var selection = new Fields(Json.Field<Fields>(plan, "selection"), StringComparer.Ordinal);
            selection["gridCodes"] = Json.Field<IEnumerable>(selection, "gridCodes").Cast<string>().OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var settings = Json.Field<IEnumerable>(selection, "featureSettings").Cast<Fields>();
            selection["featureSettings"] = settings.OrderBy(feature => Json.Field<string>(feature, "featureType"), StringComparer.Ordinal).Select(feature =>
            {
                var copy = new Fields(feature, StringComparer.Ordinal);
                copy["lods"] = Json.Field<IEnumerable>(feature, "lods").Cast<int>().OrderBy(lod => lod).ToArray();
                return copy;
            }).ToArray();

            var output = new Fields(Json.Field<Fields>(plan, "output"), StringComparer.Ordinal);
            output["scenePath"] = OutputRoot + "/City.unity";
            return Hash(Json.Object(("projectFingerprint", plan["projectFingerprint"]), ("source", plan["source"]), ("selection", selection), ("output", output), ("expectedGmlManifestHash", plan["expectedGmlManifestHash"]), ("sdkVersion", plan["sdkVersion"])));
        }

        private bool HasFields(Fields fields, params string[] names)
        {
            return fields != null && fields.Count == names.Length && names.All(fields.ContainsKey);
        }

        private string Hash(Fields fields)
        {
            return Digests.Sha256(Json.Utf8.GetBytes(Json.Write(fields)));
        }
    }
}
