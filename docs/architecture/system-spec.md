# PLATEAU-WorldFlow — システム設計仕様書

- **確定日**: 2026-11-11
- **最終更新**: 2026-11-11
- **公開形態**: GitHub Public Repository（公開済み）
- **Repository 名**: `DarumaPPAP/PLATEAU-WorldFlow`（**作成済み**）
- **UPM Package ID**: `com.darumappap.plateau-worldflow`
- **UPM Display Name**: `PLATEAU WorldFlow`
- **C# namespace**: `DarumaPPAP.PlateauWorldFlow`
- **ライセンス方針**: 自作コード MIT License／依存コード・都市データは個別ライセンスを遵守
- **状態**: 設計確定。GitHub Repository 作成済み。Unity Editor の機能実装と E2E 検証は未実施。

> 本仕様書のパス、コマンド、JSON、型、Assembly 名は、特記なき限り **PLATEAU-WorldFlow に新規実装する設計名**であり、既存 SDK の API 名を示すものではない。

## 1. 目的と原則

### 1.1 製品目的

ユーザーが「作品タイトルの聖地周辺を取り込みたい」等の自然言語で要求したとき、AI が実際の位置・出典・PLATEAU データ提供状況を調査し、複数の選択肢と推奨案を提示する。ユーザーが重要な Decision（地点、範囲／重複、品質、実行）を順番に承認し、Unity Editor に都市モデルを取り込み、PNG／必要に応じ MP4 で結果を検証できるようにする。

### 1.2 優先原則

1. **AI チャットが主 UI**。専用 Editor 確認 Window は必須にしない。
2. **Grill-me 型対話**：調査済み Fact とユーザーが決める Decision を分離。複数案・推奨案・理由・トレードオフを提示する。
3. **同一 C# Core を UnityMCP と公式 Unity CLI に接続**。片方への依存を Core に持ち込まない。
4. **計画と実行を分離**。Plan 作成／プレビューは読み取り専用を基本とし、Import・Scene 変更は明示承認後に行う。
5. **画像は確認手段、位置・面積・重複・LOD・出典は構造化データが正本**。
6. **データの出典・ライセンス・改変履歴を保存**。出典条件が未確認の場合は自動再配布しない。
7. **UnityAgent は必須依存にしない**。動画撮影等のコードを適切なライセンス表示付きで必要部分のみ抽出／改修する。
8. **初期対象は Unity Editor ワークフロー**。Runtime データストリーミングやプラットフォーム別最適化は別機能として判断する。
9. **導入直後に依存環境を診断し、不足があれば承認後に UPM で追加**。診断や情報表示に承認は不要だが、追加・更新・削除等の Project 変更は常に事前承認を必要とする。

### 1.3 初期対応範囲

- PLATEAU SDK を用いた Dataset 調査、範囲設計、LOD／品質 Plan、Import、確認。
- **初回セットアップ**：WorldFlow の導入後に PLATEAU SDK／関連 Package／Git LFS 等を検査し、必要な変更を説明して承認後に導入する。
- 複数地点の重複領域判定と統合／共有／分離の提案。
- CoplayDev UnityMCP と公式 Unity CLI の両経路。
- PNG は接続先が持つ既存撮影機能を優先。MP4 は後続フェーズで再利用モジュールを検討。
- PLATEAU Toolkits、広域動的タイル、動画の高度化は順次追加し、互換性を検証する。

## 2. 管理形態：Public GitHub Monorepo + 1 UPM Package

### 2.1 GitHub

- **Public Repository**：`DarumaPPAP/PLATEAU-WorldFlow`（作成済み）。
- **Default branch**：`main`。直接 Push を避け、すべて PR を通す。
- Branch の基本系：`feature/*`、`fix/*`、`chore/*`、`release/*`。
- PR は差分レビュー、テスト結果、互換性、影響範囲、権利確認を必須とする。
- **Squash Merge** を基本とし、**自動 Merge・無承認 Merge は行わない**。
- PR Gate は変更リスクに応じた段階制：Docs/Skill のみ、Core/Schema 変更、Editor/SDK 変更、配布・依存関係変更で区別。
- リリースは SemVer、Git Tag (`v0.1.0` 等)、CHANGELOG、GitHub Release。Release Tag 作成前に Package と Tag の Version 一致をチェック。
- 開発 Project はレビュー済み Commit SHA、利用者は安定 Tag を指定して再現可能にする。`main` 自動追従は原則避ける。
- Release 時に `LICENSE`・`THIRD_PARTY_NOTICES.md` と、第三者同梱資産がないことを確認。

### 2.2 Unity Package

- 当初は **1つの Unity UPM Package**：`Packages/com.darumappap.plateau-worldflow/`。
- `Editor` 内で Core／PLATEAU API 呼び出し／Preview／UnityMCP Adapter／Unity CLI Adapter を Assembly ごとに分ける。
- 一方の Adapter の導入は他方を強制しない。両方なしの場合も package が破綻しないことをテストする。
- Unity Editor 基準はまず **6000.3.10f1 以上で検証**。PLATEAU SDK v4.3.0 の検証環境に合わせた初期基準であり、別 Unity Version の対応可能性を否定しない。
- PLATEAU SDK v4.3.0、CoplayDev unity-mcp v10.3.0、Toolkits v2.3.0 を当初の検証対象候補として記録。実際のコンパイル・API 互換性を E2E で確定する。
- **SDK／Toolkits／MCP／CLI の Git 依存を UPM Package の `package.json` に直接記載しない**。Git URL は **利用 Project の `Packages/manifest.json`** から導入する。WorldFlow から追加する場合も `UnityEditor.PackageManager.Client` を利用し、Project 側の依存として解決する。登録済み Registry Package に限って `package.json` への通常依存を検討する。
- Package の `package.json` の技術名は **必ず `com.darumappap.plateau-worldflow`**。フォルダ名、ドキュメント、リリースの表記を一致させる。

### 2.3 UPM Git インストール形式（Tag 作成後に有効）

```text
https://github.com/DarumaPPAP/PLATEAU-WorldFlow.git?path=/Packages/com.darumappap.plateau-worldflow#v0.1.0
```

> 2026-11-11 時点で上記 Repository は作成済み、Tag は未作成。URL は**将来の導入形式例**であり、現時点で有効なダウンロードリンクではない。

### 2.4 初回導入の依存診断・確認・インストール（**実装必須**）

WorldFlow UPM Package を初めて導入したとき、**Unity Editor 上で依存状態を自動診断し、不足があればユーザーへ確認を出す**。ただし、**診断と確認の表示は自動でも、Package の追加は承認後だけ**とする。AI チャット中心の方針を維持し、専用セットアップ Window は必須にしない。対話可能な Editor では確認ダイアログ、MCP／CLI から呼んだ場合は AI チャットの提案・承認フローを利用する。

```text
WorldFlow Package の追加／更新／最初の利用
  ↓
Bootstrap Assembly がロード（PLATEAU SDK 未導入でも Compile 可能）
  ↓
Editor 安定後に依存状態を診断（Read-only）
  ↓
READY ──────────────→ 通常機能を有効化
  ├─ SDK がない／不足 → 必要 Package・導入元・Version・影響を提示
  ├─ Version 競合／環境不足 → BLOCKED、修正案を提示
  └─ ユーザーが保留 → 導入しない／後から再診断できる
  ↓ 明示承認（対象 Project・Package・Version・Source・Plan を固定）
UPM 経由で追加（非同期／同時リクエスト禁止）
  ↓
Domain Reload／UPM 再解決があっても進捗を復元
  ↓
再診断 → READY / PARTIAL / FAILED / BLOCKED の結果と対処法を提示
```

初回通知は **Project と WorldFlow Package Revision 単位で制御**する。`InitializeOnLoad` による Domain Reload で同じダイアログを連続表示しない。毎回の自動ポップアップは避け、初回通知・明示的な `再診断`・機能実行直前の Gate を使い分ける。保留した場合でも世界調査など SDK 非依存の機能は利用できる。

### 2.5 依存関係カタログと導入可否

| 対象 | 区分 | 初期判定・推奨アクション |
| --- | --- | --- |
| `com.synesthesias.plateau-unity-sdk` | **必須**（都市モデルの Import／加工） | 未導入なら公式 `v4.3.0` Git Tag を提案し、承認後に追加。既存互換版を無条件に置換しない |
| SDK の `package.json` に定義された Registry 依存 | **SDK の推移的依存** | UPM Resolver に任せる。追加される可能性がある依存と競合を説明するが、リストを無条件に二重追加しない |
| `com.unity.modules.unitywebrequest` | **Unity 組み込み** | HTTP 通信向け Built-in Module。別の Web API Package が必要と決めつけない。使用可否を診断し、欠落時は Project／Editor 設定の問題として案内 |
| 外部 HTTP／認証 API | **用途・接続先に依存** | Web 通信ライブラリの導入と API Key／サービス利用権は別。必要な利用先・認証方式・規約が確定してから判断。キーの自動発行・取得はしない |
| `com.synesthesias.plateau-unity-toolkit` | **任意** | Toolkit 固有の機能を選択したときのみ追加提案。v2.3.0 と SDK v4.3.0 の Manifest 依存整合性を実証するまでは自動導入を BLOCKED |
| `com.coplaydev.unity-mcp` | **任意** | CoplayDev 経由の操作を希望するときだけ提案。CLI の利用には必須にしない |
| 公式 Unity CLI／`com.unity.pipeline` | **任意** | 対応する Editor／外部 CLI の導入状況を別々に確認。未検証の固定 Git URL や Version を捏造して追加しない |
| Git／Git LFS | **Git 取得時の外部前提** | `git` / `git lfs` が実行可能か診断。PLATEAU 公式は Git URL 導入時の Git LFS を要件としている。未導入なら導入手順を提示し、勝手に OS へインストールしない |

PLATEAU SDK の導入ソースは、検証済みの Git Tag `https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity.git#v4.3.0` を初期候補とする。これは **SDK の Package URL** であり、WorldFlow Package URL ではない。Git／Git LFS がない場合や Git 取得が失敗した場合は、**公式 Release の `.tgz` をユーザーに選択してもらい、ローカル tarball を UPM に追加する手順**を案内する。未確認のダウンロード URL や任意の外部 Repository へフォールバックしない。

PLATEAU SDK v4.3.0 の `package.json` には Newtonsoft Json、Shader Graph、FBX、Splines、Addressables、Collections、Burst 等の依存が定義されている。**導入時点の正確な依存一覧は配布する Tag の `package.json` と UPM の解決結果を正本**とし、固定した手書きリストを機械的に全追加しない。Unity バージョンは v4.3.0 の公式推奨基準 **Unity 6000.3.10f1 以上**を初期 Gate とする。Toolkits v2.3.0 の manifest にある SDK 依存 `3.0.0` と公式対応案内の `4.3.0` との関係は互換性テストで解消するまで自動更新対象にしない。

### 2.6 実装 API と Bootstrap の独立性

- **診断**：`UnityEditor.PackageManager.Client.List(offlineMode: true, includeIndirectDependencies: true)` を用いて現時点で解決済みの Package 一覧を取得。Project の `Packages/manifest.json` と `packages-lock.json` も参照し、**直接依存／推移的依存／Git Tag・SHA／実解決 Version** を区別する。キャッシュ情報が古い・解決失敗等は `Unknown` とし、インストール済みと断定しない。
- **操作**：`Client.Add(string identifier)` または、複数追加を一括解決する `Client.AddAndRemove(packagesToAdd, packagesToRemove)` を使う。後者を使う場合でも **`packagesToRemove` は空**とし、無関係な Package を明示削除しない。いずれも非同期 Request を監視し、並行 UPM Request を発行しない。
- **API Version**：基準の Unity 6000.3 では `AddAndRemove(string[], string[])` を使う。後続 Unity Version にある `dryRun` オーバーロードを 6000.3 で使えると仮定しない。**完全な依存解決結果の事前プレビューは保証せず**、承認画面では「明示的に追加する項目」と「Resolver が変更し得る間接依存」を区別する。
- **通知と再開**：`[InitializeOnLoad]` または `[InitializeOnLoadMethod]` で診断の入口を登録し、`UnityEditor.PackageManager.Events.registeredPackages` から変更後の再診断を行う。Domain Reload でコールバックと一時状態が消えるため、Job ID・承認済み Plan・実行段階を Project-local の `Library/PLATEAUWorldFlow/Setup/` に保存して復元する。
- **Editor の状態**：Compilation、UPM 処理中、Play Mode、Batch Mode 等ではポップアップ／自動追加を抑制し、必要なら延期・明示的な診断エラーを返す。非対話 Batch Mode で承認ダイアログを表示しない。
- **必須の構造**：`Editor/Setup/Bootstrap` と `Editor/Core` は **PLATEAU SDK 型を直接参照せず**、SDK 未導入でも WorldFlow が Compile できる。`Editor/Plateau`、各外部 Adapter の asmdef は導入済み Package の有無に応じた Version Define／Define Constraints と参照により有効化する。Optional Dependency がない全組み合わせを Compile Test する。
- **手動入口**：`Tools > PLATEAU WorldFlow > Check Dependencies`（新規メニュー設計名）を用意する。自動通知を閉じた後も診断・インストール計画の再提示ができる。

### 2.7 承認・安全性・障害復旧

- **承認の対象**：Package ID、導入元 URL、Tag／SHA、バージョン、追加理由、対象 Unity Project、Plan ID／Revision、見込まれる再コンパイル／Domain Reload をユーザーへ明示する。AI が勝手に `approved: true` を送るだけでインストールできる仕様は禁止。
- **承認の入口**：Editor 対話時は `EditorUtility.DisplayDialogComplex` 等の標準ダイアログを利用。AI チャットからの場合は接続先の実際の Human Approval／ローカル確認と紐付ける。技術的に証明可能な承認経路がない場合は Editor ダイアログに切り替える。**専用の常設 Editor Window は作らない**。
- **操作制限**：導入先の URL／Package ID は検証済み Allowlist に限定。ユーザー入力や Web 検索結果の URL をそのまま Package Manager に渡さない。初回 Setup は *追加のみ*。既存 Package の置換・ダウングレード・削除、Registry の変更、OS への Git LFS インストールは勝手に行わない。
- **既存版の扱い**：対象 Package がすでにある場合は Source・Version・依存関係を比較。互換性不明／非互換なら `INCOMPATIBLE` とし、別の Migration Plan の提示までに留める。**無断更新しない**。
- **監査・復旧**：変更前後の `Packages/manifest.json` と `Packages/packages-lock.json` の差分、Request Status、エラーを Project-local の Evidence に記録。資格情報や機密 URL をログへ出さない。失敗時のリトライとユーザー主導の復旧手順を示し、既存 Package を巻き込む自動 Rollback は行わない。
- **成功条件**：`Client.Add` の成功だけをセットアップ完了とせず、`Client.List` による再解決、Package の識別／Version／Source、コンパイル状態、対応 Capability の利用可否を再検査して `READY` にする。
- **利用許諾**：Install Plan に、第三者 Package の出典とライセンスへのリンクを表示し、利用規約・Dataset の個別権利条件と混同しない。

#### ユーザーへ提示する確認内容（例）

```text
PLATEAU WorldFlow — 依存関係の確認

PLATEAU SDK for Unity             未導入   → v4.3.0 の追加を推奨
UnityWebRequest                   使用可能 → 追加不要（Unity 標準）
SDK からの推移的 UPM 依存             UPM が解決（実変更は事後検証）
Git / Git LFS                     診断結果に応じて READY / BLOCKED
CoplayDev UnityMCP               任意     → 希望する経路なら別途案内
公式 Unity CLI                    任意     → 希望する経路なら別途案内
PLATEAU Toolkits                  任意     → 初期導入対象外

追加元: Project-PLATEAU / PLATEAU-SDK-for-Unity, Tag v4.3.0
対象: 現在開いている Unity Project
影響: Package Manifest / Lock の変更、再コンパイル、Domain Reload

[承認して追加]  [今回は保留]  [詳細を確認]
```

### 2.8 Setup Contract（新規設計名）

- `DependencyInventory`：Project ID、Unity Version、検査時刻、Package ID、導入元、要求 Version、解決 Version、Source、直接／間接、互換性状態、Git／Git LFS 状態、取得元の根拠。
- `DependencyInstallPlan`：Plan ID・Revision・Hash、対象 Project、追加候補、理由、固定 Source／Tag、事前条件、既存 Package への影響、ユーザー承認状態、有効期限。
- `DependencyInstallJob`：Job ID、Install Plan Hash、UPM Request 状態、Package 別の結果、Domain Reload 再開情報、再診断結果、Evidence Path。
- Status は `READY` / `MISSING` / `INCOMPATIBLE` / `BLOCKED` / `UNKNOWN` とし、インストールの Result は `COMPLETED` / `PARTIAL` / `FAILED` / `CANCELLED`。**未導入・競合・通信障害を同一エラーにまとめない**。
- JSON Schema は `docs/contracts/dependency-inventory.schema.json`、`dependency-install-plan.schema.json`、`dependency-install-result.schema.json` に置き、UnityMCP と CLI で共通利用する。

## 3. 推奨フォルダ構成

```text
PLATEAU-WorldFlow/
├── README.md
├── AGENTS.md
├── LICENSE
├── THIRD_PARTY_NOTICES.md
├── CHANGELOG.md
├── .gitignore
├── .gitattributes
├── .github/
│   ├── workflows/
│   │   ├── validate.yml
│   │   ├── package-check.yml
│   │   └── release-check.yml
│   └── PULL_REQUEST_TEMPLATE.md
├── Packages/
│   └── com.darumappap.plateau-worldflow/
│       ├── package.json
│       ├── README.md
│       ├── CHANGELOG.md
│       ├── LICENSE
│       ├── THIRD_PARTY_NOTICES.md
│       ├── Editor/
│       │   ├── Setup/
│       │   │   ├── Bootstrap/
│       │   │   ├── Catalog/
│       │   │   ├── Diagnostics/
│       │   │   └── Installation/
│       │   ├── Core/
│       │   │   ├── Contracts/
│       │   │   ├── Models/
│       │   │   ├── Planning/
│       │   │   ├── Validation/
│       │   │   └── Jobs/
│       │   ├── Plateau/
│       │   │   ├── Dataset/
│       │   │   ├── Coverage/
│       │   │   ├── Import/
│       │   │   ├── Attributes/
│       │   │   └── Export/
│       │   ├── Preview/
│       │   │   ├── Areas/
│       │   │   ├── Camera/
│       │   │   └── Capture/
│       │   └── Integrations/
│       │       ├── UnityMcp/
│       │       └── UnityCli/
│       ├── Tests/
│       │   └── Editor/
│       │       ├── Core/
│       │       ├── Plateau/
│       │       └── Integrations/
│       ├── Samples~/
│       │   └── BasicWorkflow/
│       └── Documentation~/
├── .agents/
│   └── skills/
│       ├── plateau-world-planner/
│       ├── plateau-location-research/
│       ├── plateau-area-design/
│       ├── plateau-quality-design/
│       ├── plateau-city-import/
│       └── plateau-result-validation/
├── docs/
│   ├── architecture/
│   ├── contracts/
│   ├── workflows/
│   ├── decisions/
│   ├── compatibility/
│   ├── ai/
│   ├── legal/
│   │   ├── LICENSE_POLICY.md
│   │   ├── DATA_ATTRIBUTION.md
│   │   └── TRADEMARK_AND_BRANDING.md
│   └── installation/
├── eval/
│   ├── golden/
│   └── scenarios/
├── tests/
│   ├── fixtures/
│   └── unity-projects/
└── tools/
    ├── validate_contracts.py
    └── validate_package.py
```

- `Editor/Setup` は SDK 非依存の初回診断、Package Catalog、確認／承認、UPM 導入 Job／復旧を所有。PLATEAU SDK 未導入で Compile 可能な独立 asmdef とする。
- `Editor/Core` は製品固有の Plan・Job・状態遷移・入力検証を所有。SDK／MCP／CLI の型を露出しない。
- `Editor/Plateau` は PLATEAU SDK の操作をラップし、実行結果／失敗／部分成功を Core に報告。
- `Editor/Preview` は範囲枠や重複領域の可視化、カメラ制御、必要時の動画撮影を担当。
- `Editor/Integrations` は UnityMCP と CLI のスキーマ変換のみ。Core のビジネスロジックを重複実装しない。
- `.agents/skills` は Codex 向け正本。各 Skill の入力・出力・中止条件・参照資料を記述する。Work 利用時は `AGENTS.md` と `docs/ai/` に入口・引き継ぎ説明を用意する（Work が `.agents/skills` を自動ロードすると仮定しない）。
- 初期リリースでは `Runtime/` を作らない。ランタイム上の動的取得を要件にする場合のみ追加。
- `Editor` 下の各 Assembly Definition の境界は設計図として維持しつつ、**必須依存と任意依存の実コンパイル検証が通ること**を採用 Gate とする。SDK を導入していない状態でも `Setup` / `Core` がビルドされ、依存追加処理へ進めることが必須。
- Package には Unity の `.meta` を含め、`Samples~`・`Documentation~` は Unity パッケージ向けの規則に従う。

### 3.1 package.json（初期設計例）

```json
{
  "name": "com.darumappap.plateau-worldflow",
  "version": "0.1.0",
  "displayName": "PLATEAU WorldFlow",
  "description": "AI-assisted PLATEAU city-model planning, import and visual verification for the Unity Editor.",
  "unity": "6000.3",
  "license": "MIT",
  "author": {
    "name": "DarumaPPAP"
  },
  "keywords": ["plateau", "citygml", "gis", "ai", "editor"]
}
```

- `version: 0.1.0` は**将来の最初の試験リリース用例**。検証済み配布を意味しない。
- `dependencies` は、必要な通常 Registry 依存が確定した時点で追記。未検証の SDK Registry Version を決め打ちしない。
- `description` と `keywords` に `PLATEAU` を使う場合、製品非公式の免責を README に記載する。

## 4. 接続・運用アーキテクチャ

```text
AI Client / Codex / Work
  ├─ 初回 Setup：依存診断 → Install Plan → 承認 → UPM 追加
  ├─ 外部情報調査・出典確認
  ├─ Grill-me 対話・Human Approval
  └─ AI Skills (.agents/skills)
       │ JSON Plan / Tool Request
       ├───────────┬──────────────┐
       ▼           ▼              │
  CoplayDev MCP  Official CLI      │
       │           │              │
       ▼           ▼              │
   MCP Adapter  CLI Adapter        │
       └───────────┘              │
             ▼                    │
       Common C# Core ◄───────────┘
       (Plan / Validate / Job / Evidence)
             │
         PLATEAU Adapter
             │
       PLATEAU SDK for Unity
             │
       Unity Scene / Generated Assets
             │
      Preview & Capture（既存機能優先）
             │
       PNG / MP4 / 構造化レポート
             └────→ AI に結果を返す
```

### 4.1 コマンド（WorldFlow 側の新規公開設計）

| 共通意味 | UnityMCP 名の案 | CLI 名の案 | 実行種別 |
| --- | --- | --- | --- |
| SDK／依存環境診断 | `worldflow_inspect` | `worldflow.inspect` | Read |
| Package 依存状態一覧 | `worldflow_dependencies_inspect` | `worldflow.dependencies_inspect` | Read |
| 追加対象・影響の事前提案 | `worldflow_dependencies_plan` | `worldflow.dependencies_plan` | Read（Plan 作成のみ） |
| 承認済み Install Plan の実行 | `worldflow_dependencies_install` | `worldflow.dependencies_install` | **Mutate（Human Approval 必須）** |
| Install Job 状態取得 | `worldflow_dependencies_status` | `worldflow.dependencies_status` | Read |
| Dataset 利用可否 | `worldflow_check_coverage` | `worldflow.check_coverage` | Read |
| 候補範囲の作図 | `worldflow_preview_areas` | `worldflow.preview_areas` | Preview |
| 計画検証 | `worldflow_validate_plan` | `worldflow.validate_plan` | Read |
| 承認済み Plan の Import | `worldflow_import` | `worldflow.import` | Mutate（承認必須） |
| Job／進捗取得 | `worldflow_job_status` | `worldflow.job_status` | Read |
| 結果検証 | `worldflow_verify` | `worldflow.verify` | Read |
| 証跡一覧 | `worldflow_list_artifacts` | `worldflow.list_artifacts` | Read |

- 上記は **WorldFlow 用に新規作成する名称**。既存の MCP／CLI に存在するコマンドではない。
- API の引数・レスポンスの JSON Schema は `docs/contracts` を正本とする。両 Adapter は同一スキーマの Contract Test を通す。
- 対象 Unity Project、要求 Run ID、Plan ID、Plan Revision、確認済み範囲、承認範囲を照合してから実行する。
- `import` を同期のワンショットで長時間保持せず、非同期 Job と Status / Cancel の扱いを明示する。
- `dependencies_install` は計画の承認情報と実 Project の状態を実行直前に再照合する。SDK 未導入時も呼び出し可能な Setup 経路は SDK 型へのコンパイル依存を持たせない。
- 失敗／部分成功／中断後の再実行は成果物を検査し、重複 Import の防止・復旧を設計する。

## 5. Grill-me の標準ワークフロー

### STEP 0：依存環境の診断・承認付き導入（初回のみ／必要に応じ再実行）
- WorldFlow 導入直後に SDK・関連 UPM Package・Unity Version・Git LFS 等を診断する。
- 不足があれば追加候補、導入元、固定 Version、変更範囲、再コンパイルの可能性を提示し、**実際の追加前に承認を要求**する。
- 保留・権限不足・オフラインでも勝手に導入しない。SDK がない場合でも、SDK 非依存の聖地調査や設計資料の作成は進められる。
- **STEP 2〜4 の SDK 依存操作は `READY` Gate を満たすまで実行しない**。必要な場合はここへ戻って再診断する。


### STEP 1：場所の調査・候補提示
- 入力例：「作品Aの聖地周辺を取得したい」。
- AI が作品と場所を外部調査し、位置・関連出典・出典の信頼度をまとめる。
- PLATEAU の提供エリア／提供 LOD／データの有無は別途確認する。未確認事項を推測で補わない。
- 候補表を示し、**おすすめ候補を必ず提示**。ユーザーが採用地点を承認。

### STEP 2：範囲・分割方式
- A：聖地の近傍だけ／B：近景＋中景＋低詳細遠景（原則推奨）／C：広域。
- 重複の扱いは A：一体化／B：地点ごとに分離／C：元データ共有＋Scene 分離（原則推奨）。
- カメラ画角・撮影位置・都市モデルの有無を加味し、選択案ごとの範囲面積、重複面積、想定リスクを提示。
- **広域取得・Scene 書込みの前に必ず範囲承認**。
- 範囲プレビューは最初は地理座標／区域輪郭＋必要に応じた利用条件を確認済みの地図下地を利用する。大規模 CityGML Import をプレビュー前提にしない。

### STEP 3：品質の判断
- 軽量／バランス（原則推奨）／高品質を比較。LOD、Texture、Terrain、Collider、推定 Disk／Memory／Import 時間を記載。
- 元 Dataset にない LOD／Texture を「存在する」と扱わない。推定値は根拠・不確実性を明示。
- 必要なら小範囲の試験 Import と撮影を実施し、その差を示す。

### STEP 4：最終確認・実行・検証
- 調査地点、範囲、重複ルール、品質、出典、生成場所、必要容量を一覧化。
- 変更系操作は Human Approval Gate を通す。
- Import を実行、Job 状態を表示し、部分成功と失敗を分けて出す。
- PNG を基本として複数視点で Scene を検証。MP4 は必要時に追加。
- 出力物と一緒に出典・ライセンス・加工履歴・スクリーンショット証跡を保存する。

### 5.1 状態遷移

```text
Requested → LocationsResearched → LocationsApproved
→ CoverageDesigned → CoverageApproved
→ QualityConfigured → PlanApproved
→ Importing → Verifying → Completed
                          └→ Partial / Failed / Cancelled
```

- 上流の決定が変わった場合は、それに依存する Plan、試算、承認状態を失効させる。
- Skills の説明文だけではなく、Core 側でも状態遷移と承認を検査。

## 6. 画像・動画・Evidence

- **静止画**：UnityMCP／Unity CLI が提供する既存 Scene View・Game View の Capture を優先。能力検出し、使用不可の場合は「未対応」を返す。
- **動画**：UnityAgent の `PlayVideoEncoder` 等の必要部分を抽出・再設計することを後続フェーズで検討。コピーしたソースは原著作権と MIT 文面を保持。
- UnityAgent の動画コードは Editor Play Mode 前提、Windows/macOS H.264、`UnityEditor.PlayModeView` 非公開 Reflection 使用等の制約あり。非公開 API 互換性と代替経路を検査する。
- 実行時の MP4 を AI Client が直接受け取れるとは限らないため、**ファイル成果物 + メタデータ + 必要に応じキーフレーム画像** を返す。
- キャプチャは「ファイルの生成が確認できた」ことの証拠であり、PLATEAU Import の正確性・映像品質を自動合格とみなさない。
- Run ID、Project、Plan Revision、出力 Path、ハッシュ、撮影条件、失敗状態を Evidence として保持。

## 7. Public OSS：ライセンス・権利管理仕様

### 7.1 許諾の種類を分離

| 対象 | 根拠／条件 | 取り扱い |
| --- | --- | --- |
| WorldFlow の自作コード | 著作権者が選択する MIT License | Repository ルートと配布 Package に LICENSE を格納 |
| PLATEAU SDK for Unity | v4.3.0 リポジトリの MIT License | 可能ならコードを複製せず UPM で導入。コピー／改変時は元の著作権表示・ライセンスを保持 |
| PLATEAU SDK Toolkits for Unity | リポジトリの MIT License | SDK と同様。互換性は実検証 |
| UnityAgent Capture コード | MIT License（Copyright 2026 DarumaPPAP） | 必要部分を再利用し、ライセンス／原権利表示／改変の事実を明示 |
| CoplayDev unity-mcp のコード | MIT License（Copyright 2025 CoplayDev） | 原則 API 呼び出しのみ。コード移植時はライセンス保持 |
| PLATEAU の 3D都市モデル | PDL 1.0／CC BY 4.0 等の個別利用条件 | Dataset ごとに出典・権利者・利用条件・加工を記録。無条件の再配布禁止 |
| PLATEAU ロゴ・VI | 公式 VI マニュアル等 | 公式ブランドの流用や公式認定の誤認を避け、独自ロゴを制作 |
| GIS／Game Sample 資産 | 基本の MIT 以外に素材別ライセンスあり | サンプルアセット・フォント・音源・キャラクター等を初期 Repository に同梱しない |
| 地図タイル／航空写真／作品画像 | 提供元の別条件 | 権利未確認なら Repository／Package／リリースに埋め込まない |

### 7.2 PLATEAU 名称とブランド

- `PLATEAU-WorldFlow` は**独立・非公式の OSS**であり、国土交通省・Project PLATEAU の公式製品ではない。
- `README.md` と Package README に非公式である旨を記載する。
- 公式ロゴの無断複製・改変、国土交通省の保証・後援があると誤認させる表示をしない。
- 名前に `PLATEAU` を含めることが商標上問題ないかを本調査だけで法的保証はしない。公開前に必要なら権利者へ確認する。
- 独自のロゴ・アイコンを使用する。

推奨 README 免責文：

> PLATEAU-WorldFlow is an independent, unofficial open-source project that integrates with PLATEAU SDK for Unity. It is not developed, endorsed, or officially supported by Japan's Ministry of Land, Infrastructure, Transport and Tourism (MLIT) or Project PLATEAU. Third-party data and assets remain subject to their respective licenses and attribution requirements.

### 7.3 データ出典・加工履歴

- インポート時に `datasetId`、`datasetVersion`、`publisher`、`sourceUrl`、`licenseId`、`licenseUrl`、`retrievedAt`、`modifications` を可能な範囲で記録。
- 取得元が明示していないライセンスは推定しない。`unverified` 等の状態を使う。
- 自動生成する `ATTRIBUTION.md` に Dataset ごとの出典と加工内容を記載。
- **PDL 1.0 の出典・加工表示、個別 Dataset の CC BY 等を満たす**。個別条件と権利者、測量法等の制約を利用前に確認する。
- 出典が未確認の Data は外部公開・再配布への追加を保留にする。Import してよいかは個別条件による。
- 作品やアニメ画像は聖地調査用の **URL・出典メタデータ**を中心に扱い、著作物の複製を勝手に Repo に取り込まない。

### 7.4 権利表示ファイル

- `LICENSE`：自作コードの MIT License。
- `THIRD_PARTY_NOTICES.md`：**実際に同梱したコード・資産に対する**著作権者／元 URL／ライセンス全文または適切な参照／改変を記録する。未同梱物を同梱済みと誤認させない。
- `docs/legal/LICENSE_POLICY.md`：依存ごとの適用基準とチェックリスト。
- `docs/legal/DATA_ATTRIBUTION.md`：データに関する規則と `ATTRIBUTION.md` のフォーマット。
- `docs/legal/TRADEMARK_AND_BRANDING.md`：公式非関係・名称とロゴ使用基準。
- Package に同梱する `LICENSE` と `THIRD_PARTY_NOTICES.md` は Repository の内容と整合させる。

### 7.5 セキュリティ・公開前 Gate

- Token、API Key、ユーザー固有 Path、非公開 URL、認証情報を Repo／Issue／PR に含めない。
- Public PR から、権限付き Self-hosted Runner や Unity ライセンスのあるマシンを無条件実行させない。
- 依存 Package の Git Tag／SHA を固定し、互換性とライセンスを検査する。
- 第三者サンプル資産、データセット本体、ダウンロード Cache、PNG／MP4、生成 Unity Asset は通常の Git 履歴から除外。
- リリース前に OSS ライセンス検査・秘密情報検査・リリース内容物検査を実施。
- コードのライセンスと、データ／ロゴ／画像・動画の権利を混同しない。

## 8. 生成物と保存先

| 種類 | 既定の保存方針 |
| --- | --- |
| 自作ソース／Skills／Schema／小さなテスト Fixture | GitHub 管理 |
| 取得 CityGML・大型 GIS Data | 利用者指定の Dataset Cache、Git 除外 |
| Import 後の Unity Assets | 対象 Unity Project 内、明示承認 Scope の下に生成 |
| 計画・承認・Job・Progress | Project／Run ごとの状態保存領域。Revision を保持 |
| PNG／MP4／Evidence | Run ID ごとの Artifact ディレクトリ。Git 除外 |
| 出典情報 | Dataset 単位の機械可読メタデータ + 人間が読める `ATTRIBUTION.md` |
| Release | Git Tag + GitHub Release。将来必要なら `.tgz` を添付 |

- Cache のキーは Dataset ID／Version／取得対象範囲／条件で構成し、異なる版を誤共有しない。
- 自動 Delete、上書き、重複 Import は未承認で行わない。

## 9. Codex / Work 用の実装・検証計画

### Phase 0：Repository 基盤・初回 Setup

- モノレポ構築、README・AGENTS.md・本仕様書・License・Notices・UPM manifest・Release 方針。
- **最優先で SDK 非依存の Setup Bootstrap を実装**：初回導入検出、Read-only 依存診断、Install Plan 表示、明示承認後の `Client.Add`、Domain Reload 後の結果検査、手動再診断メニュー。
- Package 不在／古い・不一致／オフライン／Git LFS 不足／承認拒否の復旧導線を実装。新規の依存 Package を無承認で書き換えない。
- `com.darumappap.plateau-worldflow` の文字列が全ファイルで一致することを静的検査。
- Public PR Gate とルールの初期設定。**まだ Unity SDK の本処理を実装しない**。

### Phase 1：Core & Read-only PoC

- Phase 0 の Setup Job を UnityMCP / CLI 両経路から操作できるよう公開し、実ユーザー承認 Gate と SDK 未導入時のコンパイル成立を確認。
- Plan Schema、SDK 診断、地域データ照会、範囲プレビュー、Read-only Contract。
- UnityMCP と CLI の最小 Adapter を実装。両経路で同等 JSON を返せることを確認。
- AI に PNG を返して、ユーザーが範囲を選択できることを E2E 検証。

### Phase 2：Grill-me Skills

- `plateau-world-planner` を Coordinator とし、調査→範囲→品質→最終承認の状態遷移。
- Goldens に、曖昧な作品名、実在不明の聖地、重複地点、提供なし、未公開の著作物、出典矛盾等を含める。
- Fact と Decision を区別し、既定の推奨案を提示するが、根拠がない判断を強行しない。

### Phase 3：Import & Evidence

- 承認済み範囲だけを Import。Job 管理、部分失敗・再試行、サイズ検査、Asset 証跡。
- 複数視点 Capture、Scene 数／生成物／出典の確認。
- 第一に PNG 経路を完成させ、MP4 は次フェーズに分離。

### Phase 4：Video & Toolkits

- UnityAgent の録画コードを必要部分のみ再利用し、権利表示を保持。
- 動画のフレーム欠落・エンコード失敗、AI Client のメディア返却可否を検証。
- Toolkits の実互換性、広域／動的タイル、追加品質操作を段階的に採用。

### 9.1 最低テストマトリクス

| 対象 | 必須検査 |
| --- | --- |
| WorldFlow のみ導入（PLATEAU SDK なし） | Compile／初回診断／SDK 追加提案／保留／再診断。Setup 経路は機能し Import は Gate で停止 |
| PLATEAU SDK 導入承認 | 対象 URL・Tag・Project・Plan 照合、`Client.Add`、Reload 後の Version／Source／Compile 再検査 |
| ユーザーが拒否・保留 | `manifest.json` と Lock を一切変更しない。再確認はユーザー操作で可能 |
| 既存 SDK（互換／非互換／Embedded） | 無条件の再インストール・上書き・ダウングレードなし。Source／Version 別に診断 |
| 通信失敗／Git LFS 不足 | `FAILED`／`BLOCKED` を区別。元の Project を残し `.tgz` 手動導入案内 |
| Domain Reload 中断 | 初回確認のループ表示なし。Install Job の復元と再診断、重複 Add 防止 |
| UnityWebRequest | 組み込み利用可否を検査。別 Web API Package を無根拠に要求しない |
| 依存競合・Toolkits v2.3.0 | UPM 依存競合時は無断置換なし。互換性未実証の Toolkit は自動導入しない |
| UnityMCP のみ導入 | Compile / ツール発見 / Read / Preview |
| 公式 Unity CLI のみ導入 | Compile / コマンド発見 / Read / Preview |
| 両方導入 | 重複登録なし、同一 Core Result、相互干渉なし |
| どちらもなし | 意図しない Assembly 参照エラーなし。機能は診断可能 |
| PLATEAU SDK v4.3.0 | Import／LOD 検出／部分失敗／証跡 |
| Toolkits 未導入 | 基本機能の Compile／動作に影響しない |
| Toolkits v2.3.0 導入 | パッケージ解決と互換性は別途 E2E 確認 |
| データ不明／権利不明 | 未確認情報を断定せず、再配布を止める |
| 実行承認なし | Mutate 系は副作用を起こさない |
| 停止・Domain Reload | Job 状態を再構成でき、重複出力しない |
| Captures | 出力実体・エラー・ハッシュ・レビュー可能性を確認 |

### 9.2 PR / Release の受入条件

1. Target Scope と変更理由・リスクが明示されている。
2. コマンド Schema と Skills の記述が一致している。
3. 実行可能なテストと、未実行の Editor E2E が区別されている。
4. Third-party のコピー／改変がある場合、著作権表示・License 対応が完了している。
5. 生成データ／Media／秘密情報が Git に入っていない。
6. UnityMCP / CLI の片側対応が、他方の動作を壊さない。
6a. 導入直後の依存診断が動作し、ユーザー承認なしの Package 変更が0件である。SDK 未導入状態で Setup コードが Compile できる。
7. Release Version と Git Tag、Package ID の整合性が検証済み。
8. レビュー済み PR のみ、ユーザーの明示判断で Merge する。

## 10. 未検証事項と取り扱い

- **Repository は作成済み**。GitHub の既定ブランチは `main`。
- **Unity Editor の機能実装は未着手**。このファイルは設計仕様。
- 現時点では正式な Unity Editor E2E 未実施。Compatibility の確約には追加検証が必要。
- Toolkits v2.3.0 の Package manifest に記載された SDK 依存 Version と Release Note の対応説明に差異があるため、UPM 解決と実コンパイルを検証してから機能有効化する。
- UnityMCP／CLI の実 Version・オプション導入挙動は後続 PoC で確認する。
- **今回追加した自動依存診断と承認付き UPM 導入は仕様追加であり、まだ実装されていない。** Unity 6000.3 での Domain Reload 復元、Git LFS の検出、初回通知、Git／tgz 導入の E2E を必ず実施する。
- `Client.AddAndRemove` の事前 Dry Run を Unity 6000.3 で利用できるとは仮定しない。依存 Resolver の副作用は変更前後で記録・検査する。
- 本仕様は商標利用や第三者素材再配布の法的保証ではない。公開前に権利者への確認が必要となる場合がある。

## 11. 参考一次資料

- PLATEAU FAQ／ライセンス方針: https://www.mlit.go.jp/plateau/faq/
- PLATEAU Site Policy（出典、加工、PDL 1.0、個別法令）: https://www.mlit.go.jp/plateau/site-policy/
- PLATEAU 公式ハンドブック（VI マニュアル等）: https://www.mlit.go.jp/plateau/libraries/handbooks/
- PLATEAU SDK for Unity v4.3.0 Release: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.3.0
- PLATEAU SDK for Unity LICENSE: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/LICENSE.md
- PLATEAU Toolkits v2.3.0 Release: https://github.com/Project-PLATEAU/PLATEAU-SDK-Toolkits-for-Unity/releases/tag/v2.3.0
- PLATEAU Toolkits LICENSE: https://github.com/Project-PLATEAU/PLATEAU-SDK-Toolkits-for-Unity/blob/main/LICENSE
- GIS Sample LICENSE: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity-GISSample/blob/main/LICENSE.md
- GIS Sample ThirdPartyNotices: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity-GISSample/blob/main/ThirdPartyNotices.md
- Game Sample LICENSE: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity-GameSample/blob/main/LICENSE.md
- CoplayDev unity-mcp v10.3.0: https://github.com/CoplayDev/unity-mcp/tree/v10.3.0
- UnityAgent source／Capture（MIT）: https://github.com/DarumaPPAP/UnityAgent
- Unity Package Git dependency／subfolder: https://docs.unity3d.com/jp/current/Manual/upm-git.html
- Unity Package naming: https://docs.unity.com/en-us/engine/7000.0/manual/packages-list/cus-pkg-lp/cus-pkg-development/cus-pkg-structure/cus-naming
- Unity Package Manifest: https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/cus-pkg-development/upm-manifest-pkg
- Unity 6000.3 PackageManager Client.List: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/packagemanager/client/list
- Unity 6000.3 PackageManager Client.Add: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/packagemanager/client/add
- Unity 6000.3 PackageManager Client.AddAndRemove: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/packagemanager/client/addandremove
- Unity 6000.3 PackageManager Events: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/packagemanager/events
- Unity Built-in UnityWebRequest: https://docs.unity.com/ja-jp/engine/6000.0/manual/packages-list/packages-all/pack-build/com-unity-modules-unitywebrequest
- PLATEAU SDK 公式インストールガイド（Git LFS・Git URL／tgz）: https://project-plateau.github.io/PLATEAU-SDK-for-Unity/manual/Installation.html
- PLATEAU SDK v4.3.0 Package Manifest: https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/package.json

---

**実装への引き継ぎ基準**：本書を Repository `docs/architecture/system-spec.md` に配置し、`AGENTS.md` から参照する。最初の PR は Phase 0 に限定し、Unity SDK の本処理を先走って追加しない。
