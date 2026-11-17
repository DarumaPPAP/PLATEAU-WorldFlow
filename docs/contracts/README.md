# JSON契約・Schema

通常の操作は[利用ガイド](../README.md)を参照してください。このディレクトリはAIとUnityの間で交換するデータ形式の技術的な正本です。

| 用途 | 契約 | Schema |
| --- | --- | --- |
| SDKの依存診断・導入 | 下記Setupの原則 | [Inventory](dependency-inventory.schema.json)・[Install Plan](dependency-install-plan.schema.json)・[Install Result](dependency-install-result.schema.json) |
| Dataset・メッシュ・概略PNG | [Read-only](readonly.md) | [Request](readonly-request.schema.json)・[Response](readonly-response.schema.json)・[Preview](preview-manifest.schema.json) |
| 地点・範囲・品質の計画 | [Planning](planning.md) | [Planning Result](planning-result.schema.json) |
| CityGML Import | [Import](import.md) | [Import Plan](import-plan.schema.json)・[Job](import-job.schema.json)・[Result](import-result.schema.json)・[Evidence](import-evidence.schema.json) |

## Setupの不変条件

- 依存診断は読み取り専用。変更前に対象Project、Package ID、固定Tag/SHA、Versionと影響を提示します。
- `DependencyInstallPlan`のProject、Revision、Hash、有効期限を照合し、**Unity Editorでの明示承認後**だけPackageを追加します。
- JSONの`approved`だけでは承認を成立させません。応答不明・Domain Reload後は既存Jobを照合し、重複実行しません。
- `DependencyInventory`は要求Versionと実解決Version、直接/間接依存、取得元を区別します。
- 第三者パッケージの自動ダウングレード・無断置換を行わず、認証情報を公開しません。

具体的な型・必須項目・enumはSchemaを正本として扱います。
