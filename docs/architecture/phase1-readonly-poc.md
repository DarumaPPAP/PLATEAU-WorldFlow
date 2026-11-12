# Phase 1 — Core & Read-only PoC 実装仕様

- 対象: `DarumaPPAP/PLATEAU-WorldFlow`
- Baseline: `2743043a052322134dc09c6fe0d4b54054b94d54`（Phase 0 + 文書整理済み `main`）
- 初期実証環境: Unity `6000.3.12f1`、PLATEAU SDK for Unity `4.3.0`（固定Commit `a9830e9a86e8c5b28486ab0e318714ca4a20364c`）、CoplayDev unity-mcp `10.3.0`。CLI/Pipelineは導入環境で別途Versionを記録する。
- 本書は **Phase 1 実装仕様**。新規のAPI名、C#型、JSON構造は実装前の設計であり、外部SDKの実在APIを意味しない。
- 製品方針の正本: [system-spec.md](system-spec.md)。通信・配布・権利は正本および[法務方針](../legal/LICENSE_POLICY.md)を優先。
- 共有出力: [Phase 1 Read-only Contract](../contracts/phase1-readonly.md)。実装担当への実行指示: [Codex handoff](../ai/phase1-codex-handoff.md)。

## 1. 結論と到達点

ユーザーが選んだ**都市データセットとメッシュコードを、Unity EditorのScene・Assets・Package構成を変更せずに調査**し、対象範囲を数値・構造化JSON・**簡易PNG範囲図**で照合できるようにする。UnityMCPと公式Unity CLI/Pipelineの両方から同じC# Coreと同じ意味のJSONを得る。

Phase 1のPNGは**都市モデルのレンダリング画像ではない**。データに裏付けられたメッシュ/範囲の2D概略図であり、地図タイル・作品画像・CityGML本体を同梱しない。PNGに座標、縮尺解釈上の注意、出典・対象Datasetを添える。外部地図との地理的整合が未検証ならそれを明記する。

## 2. スコープと禁止事項

### 実装する

1. Phase 0の依存Inventory/Setup Jobを変更せず読み取る共通入口。SDK未導入時も使用可能。
2. SDKの**Datasetメタデータ照会**（一覧、ID、タイトル、feature types、取得時刻、参照元）と**GridCode/Extent/LOD有無**の問い合わせ。実際に確認できた値だけ返す。
3. 検証済みGridCodeを選択し、範囲の輪郭、重複、概略面積、データがあるメッシュを比較できるRead-only Plan。
4. Editor内の一時メモリでPNG範囲図を構成。許可されたProject外の専用一時/Evidenceディレクトリだけに明示要求時保存。Scene・Assets・ProjectSettings・manifest/lockは変更禁止。
5. UnityMCPと公式Unity CLI/Pipelineそれぞれの**任意Adapter**。同じCoreを呼び、同じ意味論とSchemaを返す。
6. 生成したPNGが呼び出し側AIクライアントで視認可能かを独立検証。画像配送が不可能な経路は `ARTIFACT_ONLY` と記録し、画像表示可能と偽らない。

### 実装しない

- CityGML取得・Import・解凍・Asset生成、SceneViewを変更する範囲選択UI、地図タイルの自動取得、LOD品質の推測、3D都市レンダリング、動画、Toolkits、外部サービスKeyの発行。
- AIによる作品の聖地の真偽推定、地名検索、事実確認、自然言語対話の状態機械（Phase 2）。
- Dataset/地理データの自動公開やキャッシュの無制限保存、非承認のUPM追加・ユーザーProjectへの書き込み。
- 公式Unity CLIとCoplayDevの `unity-mcp` CLIを同一物として扱うこと。

### Read-only境界（重要）

Read-onlyは**Unity Project状態の不変**を指す。明示した範囲で外部APIへのGET系照会、メモリ計算、Project外への匿名化されたEvidence PNG/JSONの保存は許可する。ただし外部SDK内部キャッシュなど副作用が不明なら `UNKNOWN` として記録する。勝手なDownload/Import、Scene変更、Assets更新、Package追加を許可しない。既存のPackages/manifest.json、packages-lock.json、Assets、ProjectSettings、Sceneを前後比較する。

## 3. 依存方向・Assembly案

```text
AI Client
  ├── CoplayDev unity-mcp (optional, v10.3.0検証)
  └── Official Unity CLI + com.unity.pipeline (optional,実Version検証)
         │                    │
         └───薄いAdapter ─────┘
                  │
    WorldFlow Read-only Core (SDK / MCP / CLI 参照なし)
       ├── Contract / 入力検証 / Geo計算 / Plan
       ├── Setup Read-only facade（既存 Phase 0）
       ├── Dataset query port ── PLATEAU SDK Adapter（任意）
       └── Preview model ── 一時PNG renderer（Unity Engineのみ）
```

推奨Assembly名（**新規設計名**）:
- `DarumaPPAP.PlateauWorldFlow.ReadOnly.Editor`：Core Editorの上に重ねるRead-only Coordinatorとモデル。既存Coreを不用意に肥大化させない。
- `DarumaPPAP.PlateauWorldFlow.Plateau.Editor`：`PLATEAU.*`型を参照。SDK導入時だけCompile。
- `DarumaPPAP.PlateauWorldFlow.Preview.Editor`：一時2D描画、PNG・Evidence出力。SDK/通信を知らない。
- `DarumaPPAP.PlateauWorldFlow.UnityMcp.Editor`：MCPの実APIに限定したAdapter。
- `DarumaPPAP.PlateauWorldFlow.UnityCli.Editor`：Pipelineの実APIに限定したAdapter。

基本方向: Adapter → ReadOnly → Core。Plateau → SDK。Preview → ReadOnly。ReadOnlyからSDK/MCP/CLIの型に依存しないよう、必要最小限の具体Portのみ設ける。既存Core/Setupの公開挙動を変えない。

`asmdef` は **Editor-only**、Version Defines/Define Constraintsで任意Packageが欠落した場合は当該Assembly自体をコンパイル対象から外す。SDKあり・なし、MCPあり・なし、CLIあり・なしの組み合わせを実コンパイルで検証する。URLやGit依存をWorldFlowの `package.json` に追加しない。

## 4. Coreの入出力と権限

共通操作の**設計識別子**（既存外部コマンドではない）:

| 操作 | 概要 | SDK | ファイル書込 |
| --- | --- | --- | --- |
| `dependencies.inspect` | Phase 0診断結果の安全化した読取 | 不要 | なし（既存診断を起動するなら内部記録は例外） |
| `datasets.list` | サーバー/ローカルDatasetのメタデータ照会 | 原則必要 | なし |
| `coverage.inspect` | DatasetのGridCode、Extent、既知のLODを取得 | 必要 | なし |
| `areas.plan` | GridCode列から範囲、重複、出典・不明点を確定 | 必要データは入力済 | なし |
| `areas.preview` | Planと網羅データから概略図モデルを構築 | 不要 | なし |
| `areas.preview_png` | 概略PNGを生成しProject外に保存 | 不要 | 明示したEvidenceのみ |

Phase 0 `dependencies.install` をPhase 1のMCP/CLIから無承認で呼べるようにはしない。依存Setupの変更系操作は既存ローカルEditor承認を通す。初期Phase 1ではMCP/CLIには読み取りとプレビューだけを公開してよい。契約/検証ゲートは[契約書](../contracts/phase1-readonly.md)を参照する。

入力:
- Project Fingerprint / 実Project一致、Dataset ID、取得Source `REMOTE|LOCAL`、既知の出典、対象GridCode、最大件数/範囲、必要なら座標系（EPSG等の検証済みID）。
- サーバーURL/Tokenは許可された設定経路でのみ使用。認証Token・絶対パス・生のリモートURLクエリは公開JSONに入れない。
- 端点座標は原則 `latitude/longitude`（WGS84であることが確認できた場合のみ）、適用した座標系を明示。経度反転、南北反転、経度180度跨ぎを未定義にしない。
- 監査用 `requestId` と `sourceCheckedAt`。正確な意味は契約書に固定。

## 5. PLATEAU SDK 4.3.0 調査アルゴリズム

固定SHA版の公式C#ソースで次を確認済み。ただし実Editorでのリンク・Native DLL動作・例外形式は必ずCodexが検証する。

1. リモート一覧: `PLATEAU.Network.Client.Create(serverUrl, token)` → `GetDatasetMetadataGroup()` → Groupの `Datasets` → Metadata `ID/Title/FeatureTypes`。トークンは結果に出さない。
2. ローカル検査: `DatasetSource.Create(new DatasetSourceConfigLocal(path))` → `Accessor` → `GridCodes`。
3. リモート個別照会: `DatasetSourceConfigRemote(datasetId, serverUrl, token)`。既存SDKが必要に応じネットワークへ問い合わせる可能性があるため、契約上 `REMOTE_QUERY` と明示。
4. `GridCode.StringCode`、`GridCode.Extent`、`GridCode.IsValid` から利用可能範囲を取得。`GetAllGmlFiles` 等はデータ取得/負荷/副作用を確認してから限定利用し、LODを未取得なら `null` にする。
5. Native所有物（`Client`、`DatasetSource`、`DatasetAccessor`、`NativeVector*`、`GridCode`）は所有権ごとに `Dispose` / `using` を適切に行う。メインスレッド制約と非同期キャンセルは実SDKで確認。
6. SDK標準の `AreaSelectorBehaviour` はSceneView、GameObject、GSI Map Loaderを変更するためPhase 1で呼び出さない。地図タイルの利用許諾検討を回避するため、範囲輪郭だけの独自2Dプレビューにする。

Coverageは「Datasetが存在する」「指定GridCodeが存在する」「そのGridCodeでLOD nが存在する」を別の事実として出力。Dataset一覧だけでメッシュ可用やLODを断定しない。接続不能/SDK未導入/ゼロ件/不正GridCode/Native API失敗は別の `code` に分類し、未取得値を補完しない。

## 6. 範囲とPNGプレビュー

- CoreではメッシュコードのExtentを緯度・経度範囲として保持し、必要なら検証済み座標参照系へ投影して距離・面積を計算。単純な緯度経度の差分をメートルとして扱わない。
- 選択したメッシュ群と利用可能メッシュ群を別の集合にし、範囲外は明確に表示。重複はDataset/Area単位の対象を特定し、計算方式と誤差を記録。
- PNGにはDataset ID、照会元の区分、選択/利用可能/不足の凡例、方位、範囲、撮影ではなく「概略図」の表記を付与。正確な背景地図・建物形状と誤認させない。
- 画像描画はオフスクリーンで完結。Camera/SceneView/GameObject/AssetDatabaseの状態を変更しない。メモリの `Texture2D` 等は `finally` で解放。
- 実装初期値: 最大1024×1024 PNG、最大500グリッド/要求、最大20 Dataset/一覧、リクエストタイムアウト/キャンセルを明示し、巨大データや無限ポーリングを避ける。制限は実測で調整し、拒否/部分返却の理由を残す。
- 出力先は検証用Project内 `Library/PLATEAUWorldFlow/Preview/` をデフォルトとし、通常Assets/Sceneを汚染しない。**Library内Evidenceのみ許可する例外**として明記。別Project/任意絶対パスへの書込み禁止。
- PNG保存時は同時にManifest（Plan ID、Dataset ID、生成時刻、SHA-256、画像サイズ、座標系、範囲、出典、`deliveryStatus`）をJSONで保存。認証情報や元画像等を持ち込まない。
- Adapterで画像バイナリを返せない場合は `ARTIFACT_ONLY`（画像パス+Hash）とし、CLI/MCPクライアント上での視認性E2Eを別テストにする。画像返却方法は外部APIの対応を実機で確認して決める。

## 7. Phase 1 PR分割（推奨）

| PR | 責務 | 受入条件 |
| --- | --- | --- |
| PR-A | Core + v1 Read-only Schema/Fixture + Fake Dataset | SDK/MCP/CLIなしCompile。単一の共有Goldenで形・境界を検証 |
| PR-B | PLATEAU SDK v4.3.0 Adapter | 実Datasetのメタデータ・GridCode取得を確認、Native解放、SDKなしCompile維持 |
| PR-C | 2D範囲プレビュー/PNG + Evidence | Scene/Assets/manifest/lock不変。PNG視覚確認、出典・Hash一致 |
| PR-D | CoplayDev MCP + Unity CLI/Pipeline optional Adapter | 両経路の発見・結果パリティ・安全性・PNG配送の可否をE2Eで記録 |

PR-DでCLI/Pipelineが提供されない場合は `BLOCKED` を残して未検証を成功扱いしない。必要ならPR-DをMCPとCLIに分割してよい。各PRのBaselineはその時点のmain SHA、逐次PR・手動Squash Merge。**CodexはPRの作成まで、Mergeは利用者のみ**。

## 8. テスト方針（冗長化禁止）

自動テストは重要な振る舞いのみ。既存Phase 0テストをそのまま実行し、新規抽象化やテストのための大規模Mockを追加しない。

- **C-01** SDK/MCP/CLIなしでCore/Setup/PreviewのCompileと診断が成立。
- **C-02** Fake Datasetを使い、GridCodeの順序・重複排除・範囲外・座標系不明を1つのParameterized Test群で検証。
- **C-03** 計画/PNG ManifestのSchema/Golden互換と出典・SHA、秘密情報非混入。
- **C-04** 保存前後のScene/Assets/ProjectSettings/manifest/lock不変（少なくともHashesとScene dirty状態）。
- **I-01** SDKの実照会（メタデータ/メッシュ/無効範囲の拒否）。ネットワークなしは `BLOCKED`。
- **I-02** UnityMCP/CLIの操作発見と同一入力→意味的に同一JSON。画像の表示可能性を別途証明。
- **I-03** SDK/MCP/CLIの有無の組み合わせでAssemblyが破綻しない。
- **I-04** UI/PNGの目視確認（正しい数値・凡例、誤認を招く地図画像ではない）。

Test Caseの重複を増やさない。原則: 新規テスト1件にはリスク/失敗条件を明示し、実行していないE2EをPASSにしない。特にCIのGreenはUnity検証ではない。

## 9. Definition of Done

Phase 1完了は次のすべてを満たすこと:
1. ユーザーProjectを変更しないDataset/範囲のRead-only操作（非認証・ローカルの少なくとも各1ケース）を実証。
2. SDK未導入でもSetupとCoreとPreviewがCompile、任意Adapterがなくてもエラーなし。
3. SDK由来の事実と未確認を区別でき、出典・照会時刻を記録できる。
4. 範囲情報と実際のPNG概略図の一致、PNGファイル/Hash/寸法/出典記録が検証済み。
5. UnityMCPと公式Unity CLI/Pipelineの両経路を**実機**で試験し、同じJSONを得る。片方未導入ならPhase 1全体は `BLOCKED`。
6. PNGがAIクライアントに表示できるなら実証する。片方のクライアントが画像を受け取れないなら `ARTIFACT_ONLY` と明示し、改善案を残す。
7. 既存Setupの承認Gate・データ保護・30件のEditModeを退行させない。
8. PR内にVersion、SHA、条件、コマンド/操作、ログ要約、未実行、リスクを明記。自動Mergeしない。

## 10. 公式資料

- PLATEAU SDK v4.3.0 C# Source: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/tree/v4.3.0
- CoplayDev MCP v10.3.0: https://github.com/CoplayDev/unity-mcp/tree/v10.3.0
- CoplayDev Custom Tools: https://coplaydev.github.io/unity-mcp/guides/custom-tools
- Unity CLIとPipelineの役割: https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-cli-pipeline-package
- CLI Custom commands: https://docs.unity.com/en-us/unity-cli/command-name-conventions/define-command
- Unity asmdef Version Defines: https://docs.unity3d.com/ja/6000.0/Manual/assembly-definition-includes.html
- APIの確認結果と未確認事項: [Phase 1 API evidence](../compatibility/phase1-api-evidence.md)
