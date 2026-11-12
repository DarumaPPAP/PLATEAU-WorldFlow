# Codex Local — Phase 1実装ハンドオフ

> 本書は**PRを分けて実装するための作業指示**。この文書を読むだけで「API検証済み」「Phase 1完了」と宣言しない。

## 0. 正本と開始条件

- Repo: `DarumaPPAP/PLATEAU-WorldFlow`
- Phase 1設計Baseline: `2743043a052322134dc09c6fe0d4b54054b94d54`。
- 本仕様PRが利用者によって手動Mergeされた後、**必ず改めて `origin/main` のSHAを実装Baselineとして取得**する。設計Baselineを実装PRにそのまま流用しない。
- [AGENTS.md](../../AGENTS.md) → [システム仕様](../architecture/system-spec.md) → [Phase 1実装仕様](../architecture/phase1-readonly-poc.md) → [契約](../contracts/phase1-readonly.md) → [API根拠](../compatibility/phase1-api-evidence.md) → [検証方針](verification-policy.md) の順で読む。
- Unity 6000.3.12f1 / Windowsを初回の検証環境とする。別Version/OSでは実Versionを記録して対応可否を別判断する。
- PLATEAU SDK v4.3.0はSetupで固定したTag/Commitを使い、**既存SDKの自動上書き禁止**。
- 初回のCodex作業は**PR-Aのみ**。ユーザーのMerge判断なしに続くPR-B/C/Dをmainにまとめて入れない。

## 1. Codexへ渡す最初のGoal

```text
/goal DarumaPPAP/PLATEAU-WorldFlow の Phase 1「Core & Read-only PoC」を、レビュー可能な小さなPRに分けて実装する。

最初の作業は PR-A（SDK・UnityMCP・CLI非依存のRead-only CoreとJSON契約）だけ。
今のorigin/main SHAを新たな実装Baselineとして記録する。
正本: AGENTS.md / docs/architecture/system-spec.md / docs/architecture/phase1-readonly-poc.md /
docs/contracts/phase1-readonly.md / docs/compatibility/phase1-api-evidence.md /
docs/ai/verification-policy.md / docs/ai/phase1-codex-handoff.md。

要求:
- Phase 0の承認Gate、Setup、既存30 EditMode Testを壊さない。
- Dataset/AreaのRead-onlyリクエスト・結果モデル、入力検証、必要最小限のGeo/重複計算、Fake Sourceを実装。
- Phase 1専用のJSON Schema/匿名Fixture/共有Goldenを追加し、PythonとC#で同等の意味を検証する。
- 実SDK型、MCP型、CLI型はPR-Aで参照しない。Scene/Assets/manifest/lock/ProjectSettingsを書き換えない。
- READMEと必要なSkillは「未実装の機能」を実装済みと誤認させない範囲だけ更新。
- 単体/Editorテストは重要な境界だけ。テスト専用Managerや過剰なMock/Interfaceを導入しない。
- C#コードコメントは日本語、API識別子・Schemaキーは変更しない。
- UnityMCPのrun_tests/get_test_jobでEditModeを実行し、SDK/MCP/CLIなしBatchでCompile/テストする。
- 実行コマンド、結果、ログ要約、前後ファイル差分、未実行/不可をPR本文に記載する。
- PRを作成してレビューを依頼する。Pushは新規featureブランチのみ。
- main直Push、契約の無断破壊、UPM追加、SDK Import、Release/Tag、Mergeを行わない。

Done: Read-only Coreと契約がFake Datasetで正しく動き、SDK非依存Compile、
既存テスト＋必要最小限の新規テスト、静的CIが成功し、PR-Aに証跡がある。
```

PR-Aのレビュー・手動Mergeが完了したら、PR-B/C/Dについて下記の条件で順次Goalを与える。前PRが未Mergeなら次の実装ブランチをmainに依存させない。

## 2. 実装PR計画

### PR-A — Read-only Core + Contract

推奨ブランチ: `feature/phase1-readonly-core`

作業:
- 既存Coreへの最小参照、Read-only CoordinatorとDTO。
- Schema Draft 2020-12、匿名Fixture、Golden（Python/C#双方で検証）。
- GridCode選択、座標型、Extent、選択Plan、Overlapの抽象計算。実SDKの非公開APIを想像しない。
- プロジェクト不変（Scene dirty / Assets / Package）を検査。
- 既存Testに新規を**必要な数だけ**追加、重複は禁止。

成功条件: SDK/MCP/CLIなしCompile、契約テスト、Phase 0 EditMode退行なし、Static CI Green。

### PR-B — PLATEAU SDK v4.3.0 Dataset/Coverage

推奨ブランチ: `feature/phase1-plateau-query`

作業:
- 実PackageとNative DLLの解決を検証し、`PLATEAU.Network.Client`、`DatasetSource`、`DatasetAccessor`等のAPIを固定Tagで利用。
- 認証情報非公開、Local/RemoteのRead-only照会、GridCode/Extent、確認できたFeature/LOD。
- sdk無しasmdef CompileとNative resourceのDispose、Retry/Timeout/Cancel。
- 無制限のGML Downloadは禁止。巨大な応答は上限と `PARTIAL` を明示。

成功条件: LocalとRemoteの実照会に根拠。ネットワーク不可ならRemoteを `BLOCKED` と記録し、未実行を成功扱いしない。

### PR-C — 概略範囲PNG + Evidence

推奨ブランチ: `feature/phase1-area-preview`

作業:
- 選択/可用/不明のグリッドを独立に描く2D概略図。Scene/GameObject/AssetDatabaseの変更は禁止。
- `Library/PLATEAUWorldFlow/Preview/` にPNGと安全化したManifestを明示要求時だけ保存。
- 画像サイズ・SHA・元Planと実描画内容の一致を検証。
- 日本語説明、凡例、数値の誤認防止。正確な地図背景と区別。
- 必要ならUnity標準のテクスチャ描画で実装。図形ライブラリを安易に追加しない。

成功条件: PNGを人間が目視確認し、Scene/Assets等の前後差分ゼロ、ManifestがSchema適合。

### PR-D — UnityMCP / Unity CLI Adapter + E2E

推奨ブランチ: `feature/phase1-readonly-adapters`

作業:
- CoplayDev v10.3.0 `McpForUnityTool` の実Discoveryと呼び出し、または検証済み既存Toolを使った薄いAdapterを実装。MCP経由で画像がInline表示できるかE2Eで判定。
- Unity公式CLI + `com.unity.pipeline` の**実インストールVersion**を読み、`[CliCommand]`の属性/CommandResult/登録規則を検証したうえでAdapter実装。Version違いを勝手に解釈しない。
- 両経路とも同じRead-only CoordinatorとSchemaに委譲。揮発フィールド以外の意味的な出力一致を検証。
- Adapter不在時にCore/Setup/PreviewのCompile成立。実Packageがないときは `BLOCKED` として報告。
- 誤ってUnityMCP同梱CLI（`unity-mcp`）を公式Unity CLI（`unity`）のE2E代替として合格にしない。

成功条件: UnityMCPと公式CLIの両経路で同一Dataset/Areaを実行、同じ結果JSONとPNG Evidenceを返し、画像のクライアント表示可否を明記。

## 3. ローカル開発・検証作業

実装開始前:
```bash
git fetch origin
git switch main
git pull --ff-only origin main
git rev-parse HEAD
git status --short
```

作業者が新規ブランチを作る前に未コミット作業がないか確認。必要なら利用者へ報告し、既存変更を上書きしない。

静的検証（Repository root）:
```bash
python tools/validate_contracts.py
python tools/validate_repository.py
python tools/validate_package.py
python -m unittest discover -s tests -v
```

Unity Editor:
- 事前にProject条件/UnityVersionを記録。既存Sample Projectの代わりに今あるUnityMCP接続Projectを使ってよいが、**SDKなし検証だけは独立した依存なし環境が必要**。検証Projectの作成は任意、プロジェクト設定変更は承認取得後。
- `run_tests(mode=EditMode, assembly_names=[...])` → `get_test_job` でEditMode確認。テスト実行は既存アセンブリ名を実環境で確認して指定。
- Batch: `-batchmode -nographics -runTests -testPlatform EditMode` +実Assembly名+結果XML+ログ。
- 外部APIの実装で未確認の引数/アセンブリがあれば、VersionソースとEditor内反射で検証してから使う。
- Coverage/Previewではハッシュ・Scene dirty・変更ファイル一覧・画像目視を添付。認証情報、非公開絶対パス、生の都市データはPRへ出さない。
- Editor版のコンパイル/Plugin組合せと外部ネットワークのE2Eを静的CIと混同しない。

## 4. 作業停止と利用者に必要な操作

- UnityMCP / CLI / PLATEAU SDKがないからといって**自動インストールしない**。必要なPackage/Version/発行元/変更範囲を提示し、利用者のEditor承認後に導入。
- Unity CLI `unity pipeline install` はProjectを変更する可能性があるため、実行前に必ず了承を得る（検証環境のみ）。
- Serverの認証が必要なら、Tokenは安全なローカル入力とし、コード・PR・Artifactへ入れない。
- 有償/API利用制限、非対応CRS、利用条件不明の地図タイル、取得実装がImportを起動してしまう場合はその経路を止める。
- 契約の上流Decisionを変える必要がある場合は、PRでトレードオフと提案を明示する。独断でCore仕様を書き換えない。
- Phase 1全体の完了はMCPと**公式**CLI両経路の成功が必要。未実行ならPhase 1は完了扱いにしない。

## 5. PRの提出内容（簡潔）

| 必須 | 具体内容 |
| --- | --- |
| Baseline | 分岐したmain SHA、実装HEAD、Unity/SDK/MCP/CLI Version |
| 変更 | ファイル一覧と依存関係の変化、Contractの互換性 |
| テスト | Static、EditMode、実API、PNG、E2Eを別々にPASS/FAIL/BLOCKED/NOT_RUN |
| 証跡 | テスト件数、終了コード、公開可能なHash、前後差分、画像の目視結果 |
| リスク | 未実行環境、画像配送、外部依存/権利/Native例外 |
| 最終操作 | Mergeせず、利用者のレビューを待つ |

## 6. 次Phaseへの接続

Phase 1のPoC完了後に、Phase 2の自然言語Fact/DecisionフローをCoreのRead-only Resultに接続する。Phase 3のScene ImportはこのPhaseでは開始しない。Phase 1の実行成功を根拠としてSkillsの「未実装」宣言を限定的に更新し、Import禁止境界は維持する。
