using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Entry
{
    /// <summary>
    /// 検証済みのImport内容を表示し、このウィンドウでの操作だけを承認処理に伝える。
    /// </summary>
    internal sealed class ApprovalWindow : EditorWindow
    {
        private string _summary;
        private Func<Task<string>> _confirm;
        private Action _close;

        /// <summary>
        /// Projectと保存予定の内容を変更する前に、利用者が確認できるEditorウィンドウを開く。
        /// </summary>
        internal static void Open(Fields plan, int fileCount, string project, string activeScene, Fields attribution, Func<Task<string>> confirm, Action close)
        {
            var window = CreateInstance<ApprovalWindow>();
            window._summary = window.BuildSummary(plan, fileCount, project, activeScene, attribution);
            window._confirm = confirm;
            window._close = close;
            window.titleContent = new GUIContent("PLATEAU Import の承認");
            window.minSize = new Vector2(470, 350);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = new Color(0.12f, 0.13f, 0.14f);
            root.style.color = Color.white;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 14;
            root.style.paddingBottom = 12;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            var body = new Label(_summary ?? "承認内容が失効しました。新しい計画を準備してください。") { enableRichText = false };
            body.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(body);
            root.Add(scroll);

            var status = new Label { enableRichText = false };
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 8;
            root.Add(status);
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 8;
            root.Add(actions);

            actions.Add(new Button(Close) { text = "閉じる" });
            var approval = new Button { text = "この計画を承認" };
            approval.SetEnabled(_confirm != null);
            approval.clicked += () => Confirm(approval, status);
            actions.Add(approval);
        }

        private async void Confirm(Button approval, Label status)
        {
            // Domain Reload後に復元されたウィンドウは元の承認要求との関連を失っている。
            if (_confirm == null) return;

            approval.SetEnabled(false);
            status.text = "計画とデータの現在の状態を確認しています。";
            try
            {
                string failure = await _confirm();
                if (failure == null)
                {
                    Close();
                }
                else
                {
                    status.text = $"承認できませんでした（{failure}）。閉じて計画を再確認してください。";
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                status.text = "計画の確認中にエラーが発生しました。閉じて計画を再確認してください。";
            }
        }

        private void OnDestroy()
        {
            _close?.Invoke();
        }

        private string BuildSummary(Fields plan, int count, string project, string scene, Fields attribution)
        {
            var source = Json.Field<Fields>(plan, "source");
            var selection = Json.Field<Fields>(plan, "selection");
            var output = Json.Field<Fields>(plan, "output");
            var grids = Json.Field<object[]>(selection, "gridCodes");
            var settings = Json.Field<object[]>(selection, "featureSettings").Cast<Fields>().Select(feature =>
            {
                string type = Json.Field<string>(feature, "featureType");
                string lods = string.Join(", ", Json.Field<object[]>(feature, "lods"));
                string texture = Enabled(feature, "includeTexture");
                string packing = Enabled(feature, "enableTexturePacking");
                string collider = Enabled(feature, "doSetMeshCollider");
                string attributes = Enabled(feature, "doSetAttrInfo");
                return $"{type}  LOD {lods}　テクスチャ {texture}　パッキング {packing}　Collider {collider}　属性 {attributes}　粒度 {Json.Field<string>(feature, "meshGranularity")}";
            });

            string scenePath = Json.Field<string>(output, "scenePath");
            string assetFolder = scenePath.Substring(0, scenePath.LastIndexOf('/'));
            string origin = Json.Field<string>(attribution, "sourceUrl") ?? "Editorで許可したローカルDataset";
            string license = Json.Field<string>(attribution, "licenseId") ?? "未確認";
            string terms = Json.Field<string>(attribution, "licenseUrl") ?? "未確認";
            string remote = Json.Field<string>(source, "kind") == "REMOTE" ? "公式配布元から選択GMLと参照ファイルを取得し、ProjectのLibraryに保存します。\n" : "";
            return $"対象Project  {project}\nProject Fingerprint  {Json.Field<string>(plan, "projectFingerprint")}\n現在のScene  {scene}\n\n" +
                   $"取得元  {Json.Field<string>(source, "kind")} / {Json.Field<string>(source, "worldFlowDatasetId")}\nDataset版  {Json.Field<string>(source, "datasetVersion") ?? "未確認"}\n出典  {origin}\nライセンス  {license}\n利用条件  {terms}\n\n" +
                   $"平面直角座標系  第{Json.Field<int>(selection, "coordinateZoneId")}系\n予定GML  {count}件\nメッシュ  {string.Join(", ", grids)}\n\n{string.Join("\n", settings)}\n\n" +
                   $"出力Scene  {scenePath}\n生成Assetの保存先  {assetFolder}/\n{remote}実行時に新規Additive Sceneへ切り替え、照合が完了した生成物を保存します。\n\n" +
                   $"Plan Hash  {Json.Field<string>(plan, "planHash")}\n有効期限（UTC）  {Json.Field<string>(plan, "expiresAt")}";
        }

        private string Enabled(Fields settings, string name) => Json.Field<bool>(settings, name) ? "あり" : "なし";
    }
}
