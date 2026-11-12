# Phase 1 — Read-only Contract（v1設計）

## 0. 対象と整合

この文書はPhase 1で追加する**設計契約**であり、Phase 0の3つの依存Setup JSON Schemaを変更しない。CodexがPR-Aで `docs/contracts/` に新しいDraft 2020-12 Schemaと共有Fixtureを実装する。フィールド名はWorldFlow独自の設計名であり、PLATEAU SDK/MCP/CLIの既存型とは区別する。詳しくは[実装仕様](../architecture/phase1-readonly-poc.md)。

全操作に共通:
- `schemaVersion: 1`、UTF-8、UNKNOWN追加プロパティ禁止。現在時刻はUTC `YYYY-MM-DDTHH:mm:ssZ`。
- `requestId` はUUID等の不透明識別子、`projectFingerprint` はPhase 0既存の小文字SHA-256で実Projectと結合する。
- `status` は `READY | PARTIAL | UNAVAILABLE | UNKNOWN | BLOCKED | FAILED`。 `UNAVAILABLE`（照会した結果存在なし）と `UNKNOWN`（照会できず未確認）を区別。
- `diagnostics[]` は `{code, location, retryable}` とし、外部APIエラーメッセージ・絶対パス・認証情報は入れない。
- `evidence[]` は `{kind, source, checkedAt}` で、 `source` は秘密情報を含まない公式URLまたは `LOCAL_DATASET` 等の種別識別。
- 配列は意味的に安定したキーでソートし、重複排除する。未取得値は `null`、空リストは確認済みゼロ件に限る。
- `result` に操作別Payloadを格納。 `warnings[]` は管理された文言/コードのみ。旧Setup Schemaを流用しない。

### 共通Response例（概念上の形）

```json
{
  "schemaVersion": 1,
  "requestId": "f2e34ed1-e954-4f06-9e0d-7b1e36134807",
  "projectFingerprint": "1111111111111111111111111111111111111111111111111111111111111111",
  "operation": "datasets.list",
  "status": "READY",
  "checkedAt": "2026-11-12T00:00:00Z",
  "result": {"datasets": []},
  "evidence": [],
  "diagnostics": [],
  "warnings": []
}
```

注意: 上記は**構造説明のSample**であり、`datasets: []` の意味は「一覧取得を実行して0件と確認した場合」に限る。E2Eでリモート結果が空だと仮定しない。

## 1. 操作ごとのRequest / Result

### `dependencies.inspect`

- Request: `projectFingerprint`。
- Result: `{inventoryRef, dependencyStatus, inspectedAt}`。保存済みのPhase 0 Inventoryを安全化して返す。ファイルがなければ `UNKNOWN`、SDKなしの場合は `UNAVAILABLE` を扱う。
- SetupのPlan作成、インストールJob変更はしない。既存Setup診断の副作用が必要な場合はPhase 0のAPIを呼ぶ前に明示的に区別する。

### `datasets.list`

- Request: `{source: "REMOTE"|"LOCAL", sourceReference, maxResults}`。
- リモート: `sourceReference` は管理済みデフォルトサーバーまたは許可済み接続先の識別子（生Token/任意URL禁止）。
- ローカル: `sourceReference` は利用者が許可した既存Dataset Rootの識別子で、絶対パスを結果に出さない。
- Result: `{source, datasets: [{datasetId,title,featureTypes,version,publisher,licenseId,licenseUrl,sourceUrl}], truncated}`。確認できないフィールドは `null`。
- Datasetの存在だけを証明し、任意GridCodeでLODが使えるとは言わない。
- 上限超過は `truncated: true`。ネットワーク失敗時に空配列で成功を返さない。

### `coverage.inspect`

- Request: `{source, datasetId, gridCodes[], maxResults}`。 `gridCodes` 省略時は上限内で確認できたコードのみ取得（取得可能な全メッシュが上限を超えたら打切り明示）。
- Result: `{datasetId, availableGridCodes:[{code,extent,featureTypes,availableLods}], unavailableGridCodes, unverifiedGridCodes, truncated}`。
- `extent` は `{south,west,north,east,crs}`。数値は有限、緯度は -90..90、経度は -180..180。経度180度を跨ぐ境界はPhase 1で明示的に `BLOCKED` または分割表現し、勝手に補正しない。
- `availableLods` は存在が実証できた非負整数の配列、未確認は `null`。 `featureTypes` も未検証なら `null`。
- `unavailableGridCodes` は明示照会して存在しないと判断したコードのみ。接続失敗/権限不足等は `unverifiedGridCodes` に区別。
- 同一メッシュを重複返却しない。SDKメッシュコードの有効性検査とExtent取得が成立しない入力は拒否。

### `areas.plan`

- Request: `{datasetId, selectedGridCodes[], coverageSnapshotId, coordinateReference, planRevision}`。
- Result: `{planId,planRevision,coverageSnapshotId,selectedGridCodes,coveredGridCodes,missingGridCodes,unverifiedGridCodes,extent,overlap,estimatedAreaSquareMeters,areaMethod}`。
- `overlap` は `{areaIds, gridCodes, estimatedAreaSquareMeters}` の配列。元GridCode/Extent/CRSの不一致や重複で不確実な場合は `estimatedAreaSquareMeters=null`。
- `areaMethod` は `EXACT_PROJECTED|APPROX_PROJECTED|NOT_COMPUTED`。根拠なしの緯度経度×緯度経度を面積と称しない。
- このPlanは**Read-only選択案**。CityGML Importの承認/実行Planと混同しない。Plan変更時はRevision増分、古いPreviewを失効。

### `areas.preview`

- Request: `{planId,planRevision}`。
- Result: `{previewId,extent,coordinateReference,gridCells:[{code,extent,availability,selected}],legend,accuracyNote}`。
- `availability` は `AVAILABLE|UNAVAILABLE|UNVERIFIED`。地図タイルや建物形状を生成しない。描画座標へ変換したときの向き・Aspectを保持。
- 描画のみでSceneを変更しないことを保証するデータ契約。

### `areas.preview_png`

- Request: `{previewId,width,height}`。上限は各1024px。Previewを明示生成してからPNGを要求する。
- Result: `{artifactId,relativePath,mimeType:"image/png",width,height,sha256,byteLength,createdAt,sourceDatasetId,planId,planRevision,crs,deliveryStatus}`。
- `relativePath` は既存Project内 `Library/PLATEAUWorldFlow/Preview/<artifactId>.png` に限定。任意出力先は受け付けない。
- `deliveryStatus` は `INLINE_IMAGE|ARTIFACT_ONLY|UNAVAILABLE`。 `INLINE_IMAGE` はクライアントが画像として表示できることを**実E2Eで確認したときだけ**返す。JSONでbase64を返せたことだけでは視認性の証明としない。
- PNG bytesは `result` のJSON本文へ常時埋め込まず、Adapterがサポートする場合に限り別のメディア応答として返す。
- 対応Manifestは同じArtifact IDのJSONで保持し、認証情報やユーザー固有Pathを含めない。

## 2. エラーコード・安全性

最低限: `SDK_NOT_READY` / `DATASET_NOT_FOUND` / `COVERAGE_UNCONFIRMED` / `INVALID_GRID_CODE` / `CRS_UNVERIFIED` / `NETWORK_UNAVAILABLE` / `NATIVE_FAILURE` / `SOURCE_NOT_ALLOWED` / `PROJECT_MISMATCH` / `PLAN_STALE` / `LIMIT_EXCEEDED` / `CAPABILITY_UNAVAILABLE` / `ARTIFACT_WRITE_FAILED`。

- `BLOCKED`: ユーザー承認や依存が不足して操作できない。
- `UNKNOWN`: データSourceへ照会不能/キャッシュ不明/結果未取得。
- `UNAVAILABLE`: 有効な照会を完了して対象が存在しない。
- `PARTIAL`: 一部のDatasetやGridCodeのみ根拠つきで取得。未確定部分を列挙。
- `FAILED`: 実行時エラーが確定した。

リクエストは最大件数/タイムアウト、キャンセルと `requestId` を束ねる。無関係なファイルへ書き込むInputパスは拒否。Nativeリソースは正常/失敗/キャンセルすべてで解放する。

## 3. Codec / Contract Tests

- Python `jsonschema` とUnity C#の同一Fixture/Goldenを使う。現行の `Json.cs` の挙動と矛盾する契約を無断で追加しない。
- 同一Core結果に対してMCPとCLIの応答は**volatileなtimestamp/requestId/媒体パスを除いて意味的に同一**であること（生JSON文字列一致は不要）。
- 最低限のFixture: 正常/欠落Status、無効緯度経度、不明LODと0件の区別、重複コード、期限切れPlan、出典・Token非混入、出力先逸脱、PNG Manifest整合。
- Scheme/FixtureはPR-Aで作成してStatic CIに含める。Existing Setup JSONの`schemaVersion:1`はこの新規Schemaで再解釈しない。

## 4. コマンド命名

外部公開名は実API確認後に決定する。現時点の**提案**:
- UnityMCP: `worldflow_readonly` という単一Custom Tool + `operation` selector（6個のToolを乱立させない）。利用環境が対応すればToolを分割してもよいが、Test/使用性で根拠を示す。
- 公式Unity CLI/Pipeline: `unity command worldflow inspect`、`unity command worldflow datasets`、`unity command worldflow coverage`、`unity command worldflow plan`、`unity command worldflow preview`。CLI側命名は実Versionの `[CliCommand]` 登録仕様を検証してから固定。
- いずれも内部Coreの `operation` とSchemaは共通。CLI固有のstdout表現やMCPツール応答ラッパーはAdapterが担当。

関連: [APIエビデンス](../compatibility/phase1-api-evidence.md)。
