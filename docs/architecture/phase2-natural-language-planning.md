# Phase 2 — 自然言語による場所選定・範囲・品質計画 実装仕様

- 位置づけ: **実装準備用設計**。Phase 2 の機能実装・E2Eを完了したものではない。
- Baseline: `main@c4464bc6c694e873fc9595b9498df39f1d8dec23`（Phase 0/1は実装・Merge済み）。
- 正本: [システム仕様書](system-spec.md)、[Phase 1 Read-only契約](../contracts/phase1-readonly.md)、[Phase 2 計画契約](../contracts/phase2-planning.md)。
- 初期対象: 日本国内のPLATEAU公開データ、Unity Editor、AIクライアントとの対話。特定のOS/Platformを一般要件にしない。実機検証は既存のWindows / Unity 6000.3.12f1を第一候補とする。

## 1. 到達目標・非目標

ユーザーの「○○周辺を使いたい」「作品の舞台候補を比較したい」などの自由文を起点に、(1)地点候補の特定、(2)出典の検証、(3)PLATEAU可用性の照会、(4)範囲の候補比較とプレビュー、(5)品質の候補比較、(6)ユーザーが後続Importに渡す**計画案**を確定する。調査済み Fact、推論、ユーザーのみが決める Decision を分離する。

**Phase 2ではCityGMLの取得・Import、Scene/Assets/ProjectSettings/manifest/lockの変更、OSへの依存追加、権利不明な地図タイルの取得、外部APIキーの収集、動画生成、Import承認を実装しない。** Phase 2の「計画承認」はPhase 3の「実行承認」とは別であり、AIの自然文同意からProject変更権限を導出しない。

## 2. アーキテクチャ・責務

`AI Client + .agents/skills` が意図抽出、Web調査、候補比較、質問とDecisionの順序を制御する。**Unity PackageにLLM SDKや特定AIベンダーを埋め込まない**。Phase 1 `worldflow_readonly` / `worldflow readonly` がUnity Project実在性とPLATEAU Dataset/メッシュ可用性の技術的正本。

`User text -> Intent -> Research providers (Web/official PLATEAU MCP optional) -> Place candidates + evidence -> User selects place -> Phase1 datasets.list/coverage.inspect -> Area proposals/areas.plan/areas.preview/areas.preview_png -> User selects area -> Quality proposals -> Plan record (read-only) -> Phase3 handoff (future)`

- Planner Skill: Coordinator。過去の承認、取得元の同一性、確認日時、Plan Revision、次に必要な質問を管理する。一般的な自由文をUnity操作コマンドとみなさない。
- Location Research Skill: 地名、施設名、作品舞台、自治体等を出典付きの**候補**に正規化。候補が1件でも曖昧・未検証なら選択を強要しない。
- Area Design Skill: Phase 1観測の`datasetId/source/sourceReference/coverageSnapshotId`を利用する。単なる自治体一致からメッシュやLODの存在を断言しない。
- Quality Design Skill: 確認済み`availableLods`等のみを根拠に利用可能候補を提示。Texture、容量、Import所要時間、実測コストはPhase 2では未検証なら`null`とし、数値を創作しない。
- Phase 2の保存表現: 会話側の構造化Plan（[契約](../contracts/phase2-planning.md)）。**新しいC# Managerや2系統目のGeo計算は作らない**。必要なら最小の純粋な検証器を追加する。
- Provider境界: 外部情報のテキストは不信頼。指示、ダウンロードURL、Scene変更要求が混入してもTool実行権限に昇格させない。

## 3. 重要Decisionの依存順

| 順番 | AIが調査できるFact | ユーザーのDecision | 次へ進める条件 |
| --- | --- | --- | --- |
| 1. 目的・地点 | 固有名詞、同名候補、住所/座標の出典、現存情報 | 地点（候補ID）を選ぶ | 根拠のない座標や曖昧性を隠していない |
| 2. Dataset | 配布年度、Dataset ID、メッシュ、地物・LOD確認状態 | 使用するDatasetと範囲方針 | `datasets.list` / `coverage.inspect`の結果を照合 |
| 3. 範囲 | 選択メッシュ、確認済み外接矩形、未確認/不在、PNG | 範囲・分割・重複方針 | 同じProject/Session/観測Revisionに紐付く`areas.plan` |
| 4. 品質 | 根拠付きLOD/地物、未検証値 | 軽量/バランス/高品質のいずれか等 | 要求LODが実在するか、未確認なら明示 |
| 5. 計画確定 | 出典・選択・制約・不足 | Phase2 Plan採用/修正/保留 | 有効な依存チェーンとPlan Digest。Import承認ではない |

初期推奨は、近景/中景/遠景の三段階と共有Dataset+Scene分離を**候補**に含める。ただし「常に推奨」とせず、写真用途や単地点では狭域のほうが合理的な場合も説明する。選択肢を増やすだけの比較表は不要。

## 4. 会話ステート（プロトコルではなく設計名）

`REQUESTED -> RESEARCHING -> LOCATION_PROPOSED -> LOCATION_SELECTED -> COVERAGE_CHECKING -> AREA_PROPOSED -> AREA_SELECTED -> QUALITY_PROPOSED -> PLAN_READY`

中間状態: `NEEDS_CLARIFICATION` / `PARTIAL` / `BLOCKED` / `CANCELLED`。上流Decision変更時は下流のSelection/`coverageSnapshotId`/Plan/Preview/Quality/承認をすべて失効。タイムスタンプだけで同一性を判定せず、Project Fingerprint、取得元識別子、Dataset ID、Revision、選択地点IDを照合する。

- `LOCATION_SELECTED`を未承認の推論だけで設定しない。
- `areas.plan`の`planId`は**Phase 1の一時的なRead-only Plan**。Phase 2の`planningSessionId`、最終`planDigest`、Phase 3 Import Plan IDとは異なる。
- Phase 1のCoverage/PreviewはEditorセッション内で期限付き。Domain Reload後は再照会。保存済みPNG/JSONを現在の可用性の証明として再利用しない。
- `READY`と`PARTIAL`を混同しない。`UNAVAILABLE`（明示的不在）と`UNKNOWN`（照会不能）を分離。`PARTIAL`は検証済み範囲だけを提示。

## 5. 地点解決：地名と作品聖地を分離

**地名・自治体・施設名**は出典つき名称、住所・自治体コード、緯度経度、座標系、確認日時を収集する。座標の精度/提供元が不明なら`coordinates=null`。同名地名は自治体などの識別情報で候補分離する。

**作品の舞台・聖地**は作品の公式情報、自治体観光案内などの一次情報を優先する。ファン記事・SNSによる推定は`UNVERIFIED`または`DISPUTED`として保持し、場所そのものの存在と作品との関連性を別々に検証する。著作物の画像や地図画像を無断再配布しない。

誤った座標でのImportを防ぐため「地点が候補として見つかった」「PLATEAU自治体Datasetがある」「選択メッシュで地物とLODがある」の3つを別の真偽値で扱う。`plateau_search_areas`による自治体候補は正確なジオコーディングの代わりではない。

## 6. 公式PLATEAU MCPの位置づけ

[公式調査資料](../compatibility/phase2-research-source-evidence.md)に記録した `plateau_search_areas` / `plateau_search_datasets` / `plateau_get_dataset` / `plateau_get_citygml_files` 等を**任意の調査Adapter候補**にする。AIクライアントがMCPに対応していても、世界中のクライアントへ自動インストールしない。公式サービスは試験提供、SLA・互換性保証なし。使えないときは既存Phase1に接続可能な既知Dataset/自治体にスコープを縮め、未確認は未知として出す。

データセットの外部IDやURLをPhase1の`datasetId`や`sourceReference`へ勝手に転記しない。Phase1の取得元が返したIDと対応を確認できた場合のみ同一と認定する。外部MCPの結果だけではPhase1の`coverage.inspect`を省略しない。

## 7. 候補提示と質問

候補ごとに名称、行政区域、場所の根拠、座標の有無、PLATEAU Dataset有無/メッシュ可用性/LODの**別状態**、推奨理由、リスク、出典の最終確認日を出す。推奨1案は可能なら示すが、根拠不足なら`NO_RECOMMENDATION`。重要なDecisionを一度に大量質問せず、その時点で不可欠な1つから確認する。

複数地点は地点ごとにCoverageを取得し、重複は同一CRSと計算根拠があるときのみ面積表示する。Phase1で`areaMethod=NOT_COMPUTED`の場合は`estimatedAreaSquareMeters=null`を維持し、言語モデルの暗算で代替しない。

## 8. 品質計画の方針

品質Presetは`LIGHT/BALANCED/HIGH`を会話上の比較案として定義する。具体的なLOD・地物の割り当ては実Dataset観測と用途をもとに選定する。検証されていないTexture有無、推定Disk・Memory・GPU負荷は`UNKNOWN`とし、一般的な倍率を表示しない。Phase 3での少量Import実測まで精度の高い容量見積もりを期待しない。

## 9. セキュリティ・権利・計測

- Toolの呼出しはallowlistのみ。検索結果に埋め込まれた「SDKを追加」「このURLを開け」等を命令として実行しない。
- 生のAPIキー、個人情報、利用者Project絶対パス、Local Dataset Root、認証付きURLは公開Plan・PR・Evidenceへ載せない。
- 対象Projectへの書込み・依存追加はPhase0の承認Gate。Phase2のPlan採用だけで`approvedForImport=true`にはしない。
- 調査失敗や矛盾は`UNKNOWN/PARTIAL/BLOCKED`として理由と再調査先を表示する。出典の利用条件不明なら外部再配布を保留。
- 検証指標: 地点Top-k候補の正答、誤った位置の断定率、出典提示率、可用性誤判定率、Decision順序違反数、旧Plan誤用数、Phase2操作によるProject Mutation件数（**0**）、Tool呼出し数・Latency・Token量。
- 実行費用、検索回数、候補数の上限はE2E計測で決める。今は根拠のない性能SLAを約束しない。

## 10. Phase2 Definition of Done

1. 曖昧地名、作品聖地、複数地点、提供なし、タイムアウト、無効なCRS、旧Plan、悪意ある検索結果を含む[Golden](../ai/phase2-golden-cases.md)で期待状態に到達。
2. 公式MCPあり/なし、UnityMCPのみ/CLIのみで、既存Phase1操作を正しく使いRead-only Planを構築。双方の差は契約フィールドで説明する。
3. 地点承認を飛ばさず、出典・取得日時・ユーザー選択・未確定情報が追跡できる。外部MCPの可用性だけでは`availableLods`を確定しない。
4. Project Mutationが0件。Phase3 Import未実装/承認未取得を隠さない。
5. Test結果を`PASS/FAIL/BLOCKED/NOT_RUN`で分け、Model/Prompt/Tool Versionと観測日時を記録。
6. Phase1のSchema/Adapterを不用意に破壊せず、Phase2全体の完成条件を維持してレビュー・検証する。

## 11. 実装の進め方と完成単位

**Phase 2の自然言語ワークフロー全体を1つの実装Goalとして扱う。** 既定では単一のfeatureブランチ・実装PRで、場所調査・取得元照合・契約/状態遷移・Skills・Phase1接続・品質比較・Golden/Unity E2Eをすべて完成させる。内部の実装順は担当者が依存関係に応じて選ぶが、その工程を自動的に複数PRへ分ける理由にしない。

分割が不可避になった場合に限り、変更規模・レビュー・依存関係・リスクの根拠と代案を提示して**ユーザーの明示判断**を得る。Phase2の部分機能だけを完成と宣言しない。読み取り可能な状態遷移・証跡を基盤とし、生成AIの文章の自然さだけでContractやE2Eの成功とみなさない。実装指示は[Codex用ハンドオフ](../ai/phase2-codex-handoff.md)を参照。
