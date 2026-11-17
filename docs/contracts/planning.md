# AI計画 API

自然言語の会話から地点・Dataset・範囲・品質を**利用者の選択順に**確定し、読み取り専用の`PlanningResult`を生成します。実行方法は[計画ガイド](../guides/planning.md)、型の正本は[Planning Result Schema](planning-result.schema.json)です。

## 何を管理するか

| 項目 | 役割 |
| --- | --- |
| `planningSessionId` / `planRevision` | 会話ごとのUUIDと、選択・観測更新で増分するRevision |
| `projectFingerprint` | Unity接続前は`null`、接続後は実Projectと照合 |
| `candidatePlaces[]` | 地点候補・住所・座標・調査状況 |
| `evidence[]` | 出典・確認日時・Factと未確認事項 |
| `decisions[]` | 利用者の地点/Dataset/範囲/共有方針/品質/Plan選択 |
| `coverageReference` / `areaChoice` | Unityで確認したメッシュ、範囲、Previewへの参照 |
| `qualityChoice` | 地物・LODの希望と確認済み条件 |
| `warnings[]` / `missingFacts[]` / `nextQuestion` | 不明点と次に必要なユーザー判断 |
| `planDigest` | 採用時の内容照合に使うHash。実行承認Tokenではない |

## 意味を混同しないための状態

- `status`：`READY` / `PARTIAL` / `NEEDS_CLARIFICATION` / `BLOCKED` / `UNKNOWN` / `CANCELLED`
- `stage`：`REQUESTED` → `RESEARCHING` → `LOCATION_PROPOSED` → `LOCATION_SELECTED` → `COVERAGE_CHECKING` → `AREA_PROPOSED` → `AREA_SELECTED` → `QUALITY_PROPOSED` → `PLAN_READY`
- 利用者が明示的に選んだときだけ`decisionSource=USER_EXPLICIT`を記録します。AIの推薦や外部テキストは利用者の決定になりません。
- 地点の`physicalPlaceStatus`と作品との`workAssociationStatus`は別に記録し、`VERIFIED` / `UNVERIFIED` / `DISPUTED`等を混同しません。
- Dataset候補`CANDIDATE_ONLY`は可用性の保証ではありません。`VERIFIED_AVAILABLE`は同じProjectでのCoverage実測に基づきます。
- `requestedLods=[]`は「未指定」であり、LOD0の意味ではありません。取得サイズ・所要時間・メモリは測定根拠がなければ`null`です。

## 採用できる条件

1. 地点・Dataset・範囲・品質を上位のDecisionから選び、選択履歴を最新Revisionへ結び付けます。
2. `POINT`では出典付き座標の誤差範囲が、同一CRSの確認済み選択メッシュに包含される必要があります。`MUNICIPALITY`・`GRID`では選択したメッシュの可用性を検証します。
3. `areaChoice.spatialFit`は`VERIFIED` / `UNVERIFIED` / `OUTSIDE` / `NOT_APPLICABLE`を区別します。精度不明、座標系不一致、未確認セルの混入がある場合は`VERIFIED`を返しません。
4. 希望した地物とLODの組合せが確認できない場合、`FEATURE_LOD_ASSOCIATION`を`unverifiedRequirements`に残し、勝手に品質を確定しません。
5. 採用直前に同じEditor・Dataset・ProjectでPreviewを再照会し、`READY`と`adoptionCheck`の一致を確認します。不完全・期限切れ・Reload後は再計画します。
6. `stage=PLAN_READY`かつ`status=READY`は**読み取り専用計画が採用済み**という意味です。SDK取得、`dependencies.install`、Scene変更やCityGML Importの承認を含みません。

## Hashと失効

`planDigest`はルートから`planDigest`を除いたJSONのキーをASCII順、配列を記録順、不要な空白なしにシリアライズし、Unicode NFCへ正規化したUTF-8のSHA-256です。数値リテラルの表記を交換後に書き換えません。**Hashに署名・権限・観測の有効性は含まれません。**

地点/Datasetの変更、Coverageの更新、Editor Domain Reload、Plan Revision変更で下流PlanとPNGを`STALE`にします。新しい提案と選択が必要です。

## サンプル・検証

- [採用済み計画のFixture](../../tests/fixtures/planning/adopted-plan.json)
- [未検証条件のFixture](../../tests/fixtures/planning/unverified-adoption.json)
- [計画のユニットテスト](../../tests/test_planning.py)

Data Modelの正確な必須フィールド・値域は[JSON Schema](planning-result.schema.json)、イベントの順序とProjectの実測照合は`tools/planning.py`とテストを正本とします。
