# Read-only API

Unity Projectに紐づく**依存状態・Dataset・地域メッシュ・範囲プレビュー**を、Sceneに変更を加えず照会するAPIです。形式の正本は[Request Schema](readonly-request.schema.json)と[Response Schema](readonly-response.schema.json)です。

## 操作一覧

| 操作 | 主な入力 | 主な結果 |
| --- | --- | --- |
| `dependencies.inspect` | Project ID | SDK/依存関係の現在の診断結果 |
| `datasets.list` | 取得元、参照元、最大件数 | Dataset ID、年度、地物、出典、打切り状態 |
| `coverage.inspect` | Dataset、メッシュコード | メッシュ別の存在・不在・未確認、Extent、LOD |
| `areas.plan` | Coverage ID、選択メッシュ、CRS、Revision | 読み取り専用の範囲案 |
| `areas.preview` | Plan ID・Revision | 概略図のメッシュ・凡例・精度説明 |
| `areas.preview_png` | Preview ID、画像サイズ | PNGのProject相対パスとSHA-256・サイズ |

## リクエストとレスポンス

共通Request（`schemaVersion: 1`）：

```json
{
  "schemaVersion": 1,
  "requestId": "f2e34ed1-e954-4f06-9e0d-7b1e36134807",
  "projectFingerprint": "1111111111111111111111111111111111111111111111111111111111111111",
  "operation": "datasets.list",
  "input": {"source": "LOCAL", "sourceReference": "approved-local", "maxResults": 20}
}
```

- `requestId`は小文字UUID、`projectFingerprint`は対象Projectの64文字SHA-256です。異なるProjectの観測は使用しません。
- Responseには`schemaVersion`、`requestId`、`projectFingerprint`、`operation`、`status`、`checkedAt`、`result`、`evidence[]`、`diagnostics[]`、`warnings[]`を含みます。時刻はUTC、証跡やエラーに秘密URL・ローカル絶対パスを入れません。
- 未取得値は`null`、空配列は**照会が正常終了し0件と確認した場合のみ**使用します。配列は安定順にソートし、重複を除きます。

| `status` | 意味 |
| --- | --- |
| `READY` | 確認済み結果と証跡が揃った |
| `PARTIAL` | 一部だけ確定。未確認の理由・範囲を残す |
| `UNAVAILABLE` | 正常に照会して不在を確認した |
| `UNKNOWN` | 未照会・通信失敗等で分からない |
| `BLOCKED` | Project不一致・依存不足等で実行禁止 |
| `FAILED` | 実行時の失敗が確定した |

## Datasetと範囲の扱い

- `datasets.list`は最大20件。`truncated`を返し、Datasetが存在するだけでは任意のメッシュ/LODの利用可否を保証しません。
- `coverage.inspect`のメッシュは`availableGridCodes`、`unavailableGridCodes`、`unverifiedGridCodes`を区別します。要求コードは正規化前最大500件。通信障害・走査中断を「不在」扱いしません。
- メッシュの`extent`は`south/west/north/east/crs`。CRS/地物/LODが確認できない場合は`null`で保持し、別CRSを数値だけで比較しません。
- `areas.plan`は同一EditorのCoverageに紐づく一時的なPlanであり、Importの実行権限ではありません。期限・Project・Dataset/Revisionを照合し、Reloadや観測更新後は再照会します。
- 投影面積を実測できない場合、`areaMethod=NOT_COMPUTED`、面積は`null`。異なるCRSや未確認セルを使った重複面積を捏造しません。

## 画像と証跡

`areas.preview`は地図タイルや建物そのものではなく、**メッシュ範囲の概略図**です。図内に`SCHEMATIC / NOT A MAP`を表示し、不明セルの位置は推定しません。

`areas.preview_png`は縦横各1024px以下、`Library/PLATEAUWorldFlow/Preview/<artifactId>.png`へ保存します。Responseの`artifactId`、`relativePath`、`sha256`、`byteLength`、`deliveryStatus`を[PNG Manifest](preview-manifest.schema.json)と照合します。現在の配送方式は`ARTIFACT_ONLY`です（チャットへの画像自動表示は保証しません）。

## ツール入口・検証

- UnityMCP：`worldflow_readonly`（RequestをJSON文字列で渡す）
- 公式Unity CLI：`worldflow readonly`（`--request`でJSON文字列を渡す）。非成功ステータスをJSONで検査したい場合は`--status-json`を使用します。
- 共通のContract Fixtureは[Read-only Golden](../../tests/fixtures/readonly/readonly-golden.json)、プレビューは[Manifest例](../../tests/fixtures/readonly/preview-manifest.json)です。PythonとC#の両方でSchemaと意味を検証します。

実際の入力必須項目・エラーコード・状態遷移の詳細は上記JSON Schemaと`Editor/Core/ReadOnly`の実装を正本とします。
