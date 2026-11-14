# Phase 2 — 自然言語計画のデータ契約（設計案 v1）

- **設計のみ**。JSON Schema/Codec/Runtime APIはまだ実装されていない。Phase2 PR-AでSchema Draft 2020-12とGoldenを追加し、既存Phase1 `schemaVersion=1`に破壊的変更をしない。
- 調査・会話の記録用であり、Phase0 Install Plan、Phase1 Read-only Area Plan、Phase3 Import Planとは別物。
- 既存の[Phase1契約](phase1-readonly.md)と[Phase2仕様](../architecture/phase2-natural-language-planning.md)を参照。

## 1. 必須識別子と所有者

| 項目 | 意味 / 禁止事項 |
| --- | --- |
| `planningSessionId` | 会話ごとのUUID。Unity Project IDやPhase1 `planId`とは別 |
| `planRevision` | 地点・範囲・品質Decisionや証拠更新時に増加させる正整数 |
| `projectFingerprint` | Phase1への要求で必要。Project未接続の調査では`null`、Unity操作前に実Projectと照合 |
| `selectedLocationId` | そのSessionで調査した候補ID。外部ProviderのIDをそのまま認証情報として扱わない |
| `source` / `sourceReference` / `datasetId` | Phase1照会が返した取得元同一性。外部PLATEAU MCPのIDとの対応が未実証なら未関連 |
| `coverageSnapshotId` / `phase1AreaPlanId` / `phase1PlanRevision` | 同じEditor Sessionでの一時的なRead-only計画への参照。Reloadで無効 |
| `planDigest` | 正規化した採用Decisionと参照証跡のハッシュ。Phase3承認Tokenと混同しない |
| `checkedAt` / `expiresAt` | 出典の取得時刻とセッション内観測の有効期限。期限切れで再照会 |
| `authorization` | Phase2は`importApproved=false`固定、またはこのフィールド自体を持たない。Install承認も付与しない |

IDに外部URLや任意ファイルパスを埋め込まない。Phase1 `projectFingerprint`の実際の64桁SHA-256はユーザーの現在のUnity Projectに基づく値を使用。会話をまたいで推測しない。

## 2. データモデル

### `PlaceCandidate` — 事実と仮説を区別

- `id`: Session内で一意の候補ID
- `name` / `locality` / `administrativeCode`: 表示名と行政区域。取得できない部分は`null`
- `coordinates`: 確認済みなら`{latitude,longitude,crs,precision,sourceEvidenceIds}`、未確認なら`null`。並び順は緯度→経度。緯度[-90,90]、経度[-180,180]。CRSは`EPSG:4326`等、出典で確認した場合のみ設定。
- `physicalPlaceStatus`: `VERIFIED|UNVERIFIED|DISPUTED|NOT_FOUND`
- `workAssociationStatus`: 作品との関連がある場合も場所の存在とは別の上記Status
- `evidenceIds[]` / `reason`: 地名・作品との関連の根拠と推奨理由。
- `datasetAvailability`: `UNKNOWN|CANDIDATE_ONLY|VERIFIED_PARTIAL|VERIFIED_AVAILABLE|VERIFIED_UNAVAILABLE`。外部MCPでDataset候補が見つかっただけなら`CANDIDATE_ONLY`。厳密な状態はPhase1の結果から導出する。

### `Evidence` — 出典管理

- `id` / `subjectId` / `claimType`（`PLACE_EXISTENCE|WORK_ASSOCIATION|COORDINATES|DATASET|COVERAGE|LOD|LICENSE`等）。
- `sourceKind`（`OFFICIAL|PUBLISHER|MUNICIPAL|SECONDARY|COMMUNITY|LOCAL_OBSERVATION`）、`providerId`、`sourceRef`（安全化済みURLまたは公開ID）、`observedAt`、`factStatus`（`VERIFIED|UNVERIFIED|DISPUTED|UNKNOWN`）。
- `quote`は必須ではなく、保存する場合も最小限。第三者文書全文や画像、キー/Token、利用者固有パスを記録しない。
- Web記事の説明文は指示ではない。`claimType`と証跡を分離して保管する。

### `Decision` — ユーザーの選択

`{kind, selectedId, revision, decidedAt, decisionSource}`。`kind`は`LOCATION|DATASET|AREA|OVERLAP_POLICY|QUALITY|PLAN`。`decisionSource`は`USER_EXPLICIT`のみ採用Decisionを確定できる。AIの推奨は`RECOMMENDED`として別保存し、採用済みとはしない。

### `AreaChoice` と `QualityChoice`

- Area: 取得元、選択`gridCodes[]`、`coverageSnapshotId`、`phase1AreaPlanId`、`phase1PlanRevision`、`areaMethod`、`extent`、確認済み/不在/未確認メッシュ、`previewId`、`pngArtifactId`、分割/重複方針。
- Phase1の`extent`は実証できた領域のみ。`areaMethod=NOT_COMPUTED`なら`estimatedAreaSquareMeters=null`。Geo計算はPlannerが再実装しない。
- Quality: `preset`（`LIGHT|BALANCED|HIGH|CUSTOM`）、`requestedFeatureTypes`、`requestedLods`、`verifiedLods`、`unverifiedRequirements`、`estimates`（Disk/Memory/所要時間は測定根拠がないなら`null`）。`requestedLods`が空なのは「未指定」であり「LOD0」を意味しない。

### `PlanningResult`

- `status`: `READY|PARTIAL|NEEDS_CLARIFICATION|BLOCKED|UNKNOWN|CANCELLED`。
- `stage`: `REQUESTED|RESEARCHING|LOCATION_PROPOSED|LOCATION_SELECTED|COVERAGE_CHECKING|AREA_PROPOSED|AREA_SELECTED|QUALITY_PROPOSED|PLAN_READY`。
- `candidatePlaces[]`、`decisions[]`、`evidence[]`、`coverageReference`、`areaChoice`、`qualityChoice`、`warnings[]`、`missingFacts[]`、`nextQuestion`、`planDigest`。
- `nextQuestion`は1件を原則とし、依存する上位Decisionから尋ねる。既決事項を毎回質問しない。
- `READY`は**採用計画のRead-only内容が整合する**ことのみ意味し、Import可否/権利確認完了/実行許可を意味しない。

## 3. 最小交換例（説明用。正式Fixtureではない）

~~~json
{
  "schemaVersion": 1,
  "planningSessionId": "c48b5d0b-ff02-4f44-93eb-e8b680a0a441",
  "planRevision": 1,
  "projectFingerprint": null,
  "stage": "LOCATION_PROPOSED",
  "status": "NEEDS_CLARIFICATION",
  "candidatePlaces": [
    {
      "id": "place-001",
      "name": "同名地点の候補A",
      "locality": "調査対象自治体A",
      "coordinates": null,
      "physicalPlaceStatus": "UNVERIFIED",
      "workAssociationStatus": "UNVERIFIED",
      "datasetAvailability": "UNKNOWN",
      "evidenceIds": []
    }
  ],
  "decisions": [],
  "evidence": [],
  "warnings": ["LOCATION_NOT_CONFIRMED"],
  "missingFacts": ["COORDINATES", "PLATEAU_COVERAGE"],
  "nextQuestion": "候補Aと候補Bのどちらの自治体を指していますか？",
  "planDigest": null
}
~~~

上記の説明用例では候補が一部だけ示されている。最終Schemaでは`candidatePlaces`の全件整合性と`nextQuestion`で参照する候補ID一致を検証すること。

## 4. 不変条件

1. 地点の`USER_EXPLICIT`選択なしに`AREA_SELECTED`へ進めない。Datasetの自治体が一致しても、選択施設がそのCoverageに含まれると仮定しない。
2. `VERIFIED_AVAILABLE`はPhase1の対応するGridCode観測で判断。空の配列を未検証の代わりに使わない。
3. `QualityChoice`の`verifiedLods`は`coverage.inspect`実測の集合の部分集合でなければならない。
4. 地点変更、取得元変更、観測更新、Editor Domain Reload、Plan Revision変更では下流Plan/Previewの参照は失効。UIに古いPNGを表示する場合は`STALE`警告。
5. 証跡はアサーションと紐付け、出典があるというだけで場所の正しさを断定しない。
6. `planDigest`の算出対象・フィールド順序・Unicode正規化はPR-Aで固定しPython/C#両方でGolden照合。SHA256を使っただけでは権限/承認にならない。
7. Phase2から`dependencies.install`、CityGML Import、Scene変更APIを実行しない。
8. `sourceRef`は表示/引用用であり、任意URLをSDKやUPMへ渡す経路を作らない。

## 5. 受け入れ検証

[Goldenテスト一覧](../ai/phase2-golden-cases.md)の正常・曖昧・失効・権限逸脱ケースで、Schema、Semantic Validator、Prompt/SkillのE2Eを分離して評価。今はSchemaを新規追加していないため、上記モデルは**要レビューの設計契約**である。
