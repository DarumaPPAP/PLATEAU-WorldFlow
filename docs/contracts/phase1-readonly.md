# Phase 1 — Read-only Contract（v1）

## 0. 対象と整合

Phase 1の[Request Schema](readonly-request.schema.json)と[Response Schema](readonly-response.schema.json)はDraft 2020-12を使用し、Phase 0の依存Setup契約とは独立している。Coreは `datasets.list`、`coverage.inspect`、`areas.plan` を処理する。他の操作は後続PR向けにJSON形状のみを定義し、PR-AのCoreは `BLOCKED / CAPABILITY_UNAVAILABLE` を返す。フィールド名はWorldFlow独自の設計名であり、PLATEAU SDK/MCP/CLIの既存型とは区別する。詳しくは[実装仕様](../architecture/phase1-readonly-poc.md)。

全操作に共通:
- `schemaVersion: 1`、UTF-8、UNKNOWN追加プロパティ禁止。現在時刻はUTC `YYYY-MM-DDTHH:mm:ssZ`。
- Requestは `{schemaVersion, requestId, projectFingerprint, operation, input}`。操作別の入力は `input` に置く。識別子は小文字のハイフン付きUUID、`projectFingerprint` はPhase 0と同じ小文字SHA-256。呼び出し元が渡す実ProjectのFingerprintと照合する。
- `status` は `READY | PARTIAL | UNAVAILABLE | UNKNOWN | BLOCKED | FAILED`。 `UNAVAILABLE`（照会した結果存在なし）と `UNKNOWN`（照会できず未確認）を区別。
- `diagnostics[]` は `{code, location, retryable}` とし、外部APIエラーメッセージ・絶対パス・認証情報は入れない。
- `evidence[]` は `{kind, source, checkedAt}` で、 `source` は秘密情報を含まない公式URLまたは `LOCAL_DATASET` 等の種別識別。
- 配列は意味的に安定したキーでソートし、重複排除する。未取得値は `null`、空リストは確認済みゼロ件に限る。
- `result` に操作別Payloadを格納。 `warnings[]` は管理コードのみ。 `READY` は結果と証跡を必須とし、診断は空。 `PARTIAL` は確認できた結果と証跡、不完全な理由を返す。 `UNKNOWN` と `UNAVAILABLE` は理由を返し、後者は不在を確認した証跡を必須とする。 `BLOCKED` と `FAILED` では結果は `null`。安全な相関IDを読めない不正なRequest envelopeは例外として拒否する。

### 共通Response例（概念上の形）

```json
{
  "schemaVersion": 1,
  "requestId": "f2e34ed1-e954-4f06-9e0d-7b1e36134807",
  "projectFingerprint": "1111111111111111111111111111111111111111111111111111111111111111",
  "operation": "datasets.list",
  "status": "READY",
  "checkedAt": "2026-11-12T00:00:00Z",
  "result": {"source": "LOCAL", "datasets": [], "truncated": false},
  "evidence": [{"kind": "DATASET_QUERY", "source": "LOCAL_DATASET", "checkedAt": "2026-11-12T00:00:00Z"}],
  "diagnostics": [],
  "warnings": []
}
```

注意: 上記は**確認済みゼロ件を表す例**であり、`datasets: []` の意味は「一覧取得を実行して0件と確認した場合」に限る。E2Eでリモート結果が空だと仮定しない。

## 1. 操作ごとのRequest / Result

### `dependencies.inspect`

- Request: 共通の `projectFingerprint` と空の `input`。
- Result: `{inventoryRef, dependencyStatus, inspectedAt}`。保存済みのPhase 0 Inventoryを安全化して返す。ファイルがなければ `UNKNOWN`、SDKなしの場合は `UNAVAILABLE` を扱う。
- SetupのPlan作成、インストールJob変更はしない。既存Setup診断の副作用が必要な場合はPhase 0のAPIを呼ぶ前に明示的に区別する。

### `datasets.list`

- Request: `{source: "REMOTE"|"LOCAL", sourceReference, maxResults}`。
- リモート: `sourceReference` は管理済みデフォルトサーバーまたは許可済み接続先の識別子（生Token/任意URL禁止）。
- ローカル: `sourceReference` は利用者が許可した既存Dataset Rootの識別子で、絶対パスを結果に出さない。
- Result: `{source, datasets: [{datasetId,title,featureTypes,version,publisher,licenseId,licenseUrl,sourceUrl}], truncated}`。確認できないフィールドは `null`。
- Datasetの存在だけを証明し、任意GridCodeでLODが使えるとは言わない。
- リモートの `sourceUrl` は、公式メタデータに含まれる安全なDataset個別ページを確認できた場合だけ設定する。一般紹介ページやダウンロード先で補完しない。
- 最大20件。上限超過は `truncated: true`。照会できない場合は空配列で成功を返さない。

### `coverage.inspect`

- Request: `{source, sourceReference, datasetId, gridCodes[], maxResults}`。`sourceReference` は必須。取得元の区分と識別子の両方が観測元と一致する場合のみ照会できる。不一致の場合は `BLOCKED / SOURCE_NOT_ALLOWED`。 `gridCodes` 省略時は上限内で確認できたコードのみ取得（取得可能な全メッシュが上限を超えたら打切り明示）。
- Result: `{datasetId, availableGridCodes:[{code,extent,featureTypes,availableLods}], unavailableGridCodes, unverifiedGridCodes, truncated}`。
- `extent` は `{south,west,north,east,crs}`。数値は有限、緯度は -90..90、経度は -180..180。経度180度を跨ぐ境界はPhase 1で明示的に `BLOCKED` または分割表現し、勝手に補正しない。
- `availableLods` は存在が実証できた非負整数の配列、未確認は `null`。 `featureTypes` も未検証なら `null`。
- 明示的なGridCode指定と索引・ディレクトリの走査完了は独立して判定する。走査途中で終了したときは確認済みのメッシュと境界を維持し、網羅性のない地物・LOD一覧は `null` とする。走査できなかったメッシュは未確認として扱う。
- `unavailableGridCodes` は明示照会して存在しないと判断したコードのみ。接続失敗/権限不足等は `unverifiedGridCodes` に区別。
- 正規化前の要求は最大500コード。同一メッシュを重複返却しない。PR-Aは地域メッシュの形式のみ検査する。実SDKの妥当性検査とExtent取得、国土基本図の図郭コードの対応要否はPR-Bで確認する。全件が未確認である場合、`availableGridCodes` と `unavailableGridCodes` は `null`。一部を確認できた場合、配列はその応答で分類できたものを表し、`unverifiedGridCodes` と打切り状態を併せて読む。

### `areas.plan`

- Request: `{datasetId, selectedGridCodes[], coverageSnapshotId, coordinateReference, planRevision}`。
- Result: `{planId,planRevision,coverageSnapshotId,selectedGridCodes,coveredGridCodes,missingGridCodes,unverifiedGridCodes,extent,overlap,estimatedAreaSquareMeters,areaMethod}`。
- `overlap` は `{areaIds, gridCodes, estimatedAreaSquareMeters}` の配列。元GridCode/Extent/CRSの不一致や重複で不確実な場合は `estimatedAreaSquareMeters=null`。
- `areaMethod` は `EXACT_PROJECTED|APPROX_PROJECTED|NOT_COMPUTED`。根拠なしの緯度経度×緯度経度を面積と称しない。
- Coreは一度のCoverage応答の `requestId` を `coverageSnapshotId` とする。呼び出し元が対応するスナップショットを保持し、Project・Dataset・取得時刻・呼び出し元が設定した有効期限を照合する。有効期限の時刻ちょうどで失効する。呼び出し元は照会ごとに一意のIDを発行し、同じIDの観測を差し替えない。取得元や観測の更新時は旧スナップショットを失効させる。Coreに現行Catalogが渡された場合は、同じ観測に結合されたスナップショットだけを受理する。
- `extent` は確認済みの `coveredGridCodes` の外接矩形であり、欠落・未確認メッシュの境界は含めない。PR-Aは同じWGS84を確認できる場合のみ外接矩形を返す。複数Areaの入力と投影根拠を持たないため、`overlap` と面積は `null`、`areaMethod` は `NOT_COMPUTED`。
- このPlanは**Read-only選択案**。CityGML Importの承認/実行Planと混同しない。Plan変更時のRevision増分と古いPreviewの失効は、Planを保持する呼び出し元の責務。

### `areas.preview`

- Request: `{planId,planRevision}`。
- Result: `{previewId,extent,coordinateReference,gridCells:[{code,extent,availability,selected}],legend,accuracyNote}`。
- `availability` は `AVAILABLE|UNAVAILABLE|UNVERIFIED`。地図タイルや建物形状を生成しない。描画座標へ変換したときの向き・Aspectを保持。
- 描画のみでSceneを変更しないことを保証するデータ契約。
- PNGの「SCHEMATIC / NOT A MAP」は、データの利用範囲を示す概略図であることを表す。地形・道路・建物の描画は含まない。青緑の塗りは利用可能なメッシュ、黄色の枠は選択済みのメッシュ。利用不可・位置未確認のメッシュは位置を推定せず「COUNTS ONLY / NOT DRAWN」に件数を表示する。`CRS_UNVERIFIED` または `CRS: UNVERIFIED` の場合、表示座標の測地系は未確認。

### `areas.preview_png`

- Request: `{previewId,width,height}`。上限は各1024px。Previewを明示生成してからPNGを要求する。
- Result: `{artifactId,relativePath,mimeType:"image/png",width,height,sha256,byteLength,createdAt,sourceDatasetId,planId,planRevision,crs,deliveryStatus}`。
- `relativePath` は既存Project内 `Library/PLATEAUWorldFlow/Preview/<artifactId>.png` に限定。任意出力先は受け付けない。
- `deliveryStatus` は `INLINE_IMAGE|ARTIFACT_ONLY|UNAVAILABLE`。 `INLINE_IMAGE` はクライアントが画像として表示できることを**実E2Eで確認したときだけ**返す。JSONでbase64を返せたことだけでは視認性の証明としない。
- PNG bytesは `result` のJSON本文へ常時埋め込まず、Adapterがサポートする場合に限り別のメディア応答として返す。
- 対応Manifestは同じArtifact IDのJSONで保持し、認証情報やユーザー固有Pathを含めない。Manifestは[PNG Evidence Manifest Schema](preview-manifest.schema.json)に従い、応答のArtifact項目に取得元区分、参照ID、表示範囲を加える。

## 2. エラーコード・安全性

最低限: `SDK_NOT_READY` / `DATASET_NOT_FOUND` / `COVERAGE_UNCONFIRMED` / `INVALID_GRID_CODE` / `CRS_UNVERIFIED` / `NETWORK_UNAVAILABLE` / `NATIVE_FAILURE` / `SOURCE_NOT_ALLOWED` / `PROJECT_MISMATCH` / `PLAN_STALE` / `LIMIT_EXCEEDED` / `CAPABILITY_UNAVAILABLE` / `ARTIFACT_WRITE_FAILED`。

- `BLOCKED`: ユーザー承認や依存が不足して操作できない。
- `UNKNOWN`: データSourceへ照会不能/キャッシュ不明/結果未取得。
- `UNAVAILABLE`: 有効な照会を完了して対象が存在しない。
- `PARTIAL`: 一部のDatasetやGridCodeのみ根拠つきで取得。未確定部分を列挙。
- `FAILED`: 実行時エラーが確定した。

- PR-AのCoreは入力長と件数を制限し、同期処理の前後で呼び出し元のキャンセルを確認する。実照会のタイムアウト、キャンセル、Nativeリソースの解放はPR-Bで扱う。
- 公開IDとメタデータは文字種を限定し、代表的な秘密文字列を拒否する。URLは公式ホストのHTTPSに限定し、ユーザー情報、クエリ、フラグメント、パスのエンコードは受け付けない。任意の文字列に埋め込まれた秘密をすべて識別することはできないため、実データを接続する際は、公開可能と確認した値だけをCoreへ渡す。
- 無関係なファイルへの書き込みを指定できる入力は受け付けない。

## 3. Codec / Contract Tests

- Python `jsonschema` とUnity C#で[共有Golden](../../tests/fixtures/phase1/readonly-golden.json)を使う。型と構造はSchemaで、座標の順序、集合の分割と並び順、証跡の時系列、PNG IDとパスの一致はPythonの意味検証で確認する。PNGのGoldenは契約検証用の合成例。C#ではFake DatasetのCore結果と、PNGを除く共有Goldenを照合する。現行の `Json.cs` の挙動と矛盾する契約を無断で追加しない。
- 同一Core結果に対してMCPとCLIの応答は**volatileなtimestamp/requestId/媒体パスを除いて意味的に同一**であること（生JSON文字列一致は不要）。
- 最低限のFixture: 正常/欠落Status、無効緯度経度、不明LODと0件の区別、重複コード、期限切れPlan、出典・Token非混入、出力先逸脱、PNG Manifest整合。
- SchemaとFixtureはStatic CIで検証する。Existing Setup JSONの`schemaVersion:1`はこの新規Schemaで再解釈しない。

## 4. コマンド命名

外部公開名は実API確認後に決定する。現時点の**提案**:
- UnityMCP: `worldflow_readonly` という単一Custom Tool + `operation` selector（6個のToolを乱立させない）。利用環境が対応すればToolを分割してもよいが、Test/使用性で根拠を示す。
- 公式Unity CLI/Pipeline: `unity command worldflow inspect`、`unity command worldflow datasets`、`unity command worldflow coverage`、`unity command worldflow plan`、`unity command worldflow preview`。CLI側命名は実Versionの `[CliCommand]` 登録仕様を検証してから固定。
- いずれも内部Coreの `operation` とSchemaは共通。CLI固有のstdout表現やMCPツール応答ラッパーはAdapterが担当。

関連: [APIエビデンス](../compatibility/phase1-api-evidence.md)。
