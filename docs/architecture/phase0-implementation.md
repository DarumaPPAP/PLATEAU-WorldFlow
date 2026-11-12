# PLATEAU-WorldFlow — Phase 0 実装仕様書

**対象**: `DarumaPPAP/PLATEAU-WorldFlow`（Public）
**作成日**: 2026-11-11
**目的**: 既存の『PLATEAU-WorldFlow — システム設計仕様書』の Phase 0（Repository基盤・初回Setup）を、安全に開発・検証・PR化するための実行仕様。
**状態**: 実装指示。コード実装およびUnity E2E完了を意味しない。

> 正本は `docs/architecture/system-spec.md` に配置する元仕様書。本文はその**Phase 0 実装の具体化**であり、元仕様の製品方針を変更しない。本文のクラス名、スキーマフィールド名、テストIDは特記なき限り**新規設計候補**であり、実在するUnity APIと混同しない。

## 0. 現状・不変条件

### 0.1 Phase 0開始時点のGitHub状態

- Repository: `DarumaPPAP/PLATEAU-WorldFlow`。Public、`main`がdefault。
- `protect-main` rulesetはActive、Default Branch対象。PR必須、Required approvals=0、会話解決必須、Force Push/削除禁止、Linear History必須、Squashのみ、Bypassなし。
- RepositoryレベルのMerge Commit / Rebase Merge / Auto Mergeは無効、Squash Mergeのみ有効。
- 現在の`main`は初期`README.md`と`LICENSE`（MIT）のみ。`LICENSE`の既存権利者表記は `Copyright (c) 2026 Daruma`。**理由なく上書きしない**。
- Required status checksは未設定（初回CI成功後に追加）。Actions詳細設定、Secret scanning等の管理画面設定は連携APIでは未確認。
- 正本ではRepository作成状況のみ確認済みの事実に合わせて更新した。その他の未検証事項は根拠なく『検証済み』に変更しない。

### 0.2 固定識別子

| 項目 | 値 |
|---|---|
| Repository | `DarumaPPAP/PLATEAU-WorldFlow` |
| UPM package ID | `com.darumappap.plateau-worldflow` |
| UPM display name | `PLATEAU WorldFlow` |
| C# namespace | `DarumaPPAP.PlateauWorldFlow` |
| Package root | `Packages/com.darumappap.plateau-worldflow/` |
| 初期Editor検証基準 | Unity `6000.3.10f1` 以上（互換性の保証は実測結果次第） |
| 初期SDK候補 | PLATEAU SDK for Unity `v4.3.0`、ただし検証済みの正式対応範囲は実E2Eで判断 |
| License | 自作コードMIT、第三者同梱資産・データは別条件 |

### 0.3 守るべき禁止事項

- `main`直接Push、自動Merge、無承認Merge。
- AIが送る `approved: true` だけでUPM追加を許可すること。
- SDK/Toolkit/MCP/CLIのGit依存をWorldFlowの`package.json`に直接書くこと。
- `Client.Add`前の『完全な推移的依存変更のプレビュー』を保証すること。
- 未確認のSDK APIやCLIコマンド、架空のMCP APIを実装済みとして記述すること。
- 外部PRの未検証コードを秘密情報・権限付きrunner・ローカルUnityライセンス環境で自動実行すること。
- PLATEAUデータ、CityGML、生成Asset、PNG/MP4、トークン、秘密URLをPublic Git履歴へ入れること。
- 本Phaseで都市モデルImport、動画、Toolkits機能、Runtime Streamingを実装すること。

## 1. 開発境界とPR戦略

### PR-01：Work — Repository Foundation / Context / Contract

- **Branch案**: `feature/phase0-repository-foundation`
- **責務**: 仕様を正本化し、検証可能なContext・契約・Skill定義・静的CIを作る。Unity C#実装やE2E成功を主張しない。
- **出力**: PR、差分要約、契約の仕様、静的CI結果、未検証事項、Codexへのハンドオフ。
- **Merge**: Workは行わない。ユーザー確認後、手動Squash Merge。

### PR-02：Codex Local — UPM / SDK非依存Setup / Unity Test

- **Branch案**: `feature/phase0-setup-bootstrap`
- **前提**: PR-01がレビュー後にMerge済み。`main`のコミットSHAを記録し、その内容をBaselineとする。
- **責務**: PR-01のUPM基礎構成にC#実装を追加、依存診断、承認、非同期UPM、Domain Reload復旧、EditMode Tests、Unity E2E。
- **出力**: PR、Test Matrix、Unity version、テスト結果、EditorログとEvidence、未達項目。
- **Merge**: Codexは行わない。ユーザー確認後、手動Squash Merge。

> Work/ Codexが同一ファイルを同時編集しない。PR-02で契約修正が必要なら差分と互換性理由を明示してPRへ含めるか、別の設計PRに分離する。上流Decisionを密かに変更しない。

## 2. PR-01 — ファイル単位の実装要件

| パス | 要件 | 完了判定 |
|---|---|---|
| `README.md` | 目的、非公式OSSの免責、動作未検証である旨、Phase、導入注意、設計リンク | 内容とリンクが正確 |
| `AGENTS.md` | 100〜200行を目安にした短い開発指示、正本参照、禁止操作、検証方針、変更時の判定 | 読み手に実装責務と境界が伝わる |
| `docs/architecture/system-spec.md` | 添付元仕様書を正本として格納。Repo作成済み等、確実に変わった「現状欄」のみ更新 | 要件の脱落・勝手な書換えなし |
| `docs/architecture/phase0-implementation.md` | 本文を整合させて配置。PR責務、実装Acceptance、保留事項 | 元仕様と矛盾がない |
| `docs/ai/development-handoff.md` | Work→Codex引継ぎ様式、Baseline SHA、未実装範囲、試験コマンドの記入欄 | Codexへ渡せる |
| `docs/ai/verification-policy.md` | Static CI / Unity Local / 実操作E2Eを区別、証跡・失敗時の扱い | 未実行試験を成功と誤認しない |
| `docs/contracts/dependency-inventory.schema.json` | 診断結果の構造化契約 | 正常/異常Fixtureで検証 |
| `docs/contracts/dependency-install-plan.schema.json` | 固定導入計画・Project fingerprint・Hash | 正常/異常Fixtureで検証 |
| `docs/contracts/dependency-install-result.schema.json` | 非同期Job結果とEvidence参照 | 正常/異常Fixtureで検証 |
| `.agents/skills/plateau-*/SKILL.md` | 元仕様の6 Skillを定義。各SkillにTrigger、入力、出力、中止条件、参照契約、未実装機能の境界 | 架空の実行能力を宣言しない |
| `Packages/com.darumappap.plateau-worldflow/package.json` | Package名・表示名・Unity基準・License・Version整合。**外部Git依存は入れない** | JSONパースとpackage ID検査 |
| `Packages/.../README.md` | Packageの用途、未実装範囲、将来の導入手段、非公式免責 | 公開説明として整合 |
| `Packages/.../LICENSE`, `THIRD_PARTY_NOTICES.md` | ルートのライセンス内容と整合。コピーしていない第三者資産を同梱済みと表現しない | 著作権表示を保持 |
| `CHANGELOG.md`, `THIRD_PARTY_NOTICES.md` | 初期状態の文書。実装済みでない内容をリリース済みにしない | 表示整合 |
| `.gitignore`, `.gitattributes` | Unity一時領域・生成成果物・機密・大規模Datasetを除外。Package側の`.meta`やテストFixtureを誤除外しない | 必要ファイルが追跡可能 |
| `.github/PULL_REQUEST_TEMPLATE.md` | Scope/根拠/破壊的変更/権利/静的CI/Unity E2E/未実行/承認 | 全PRで利用可能 |
| `.github/workflows/validate.yml` | Python静的検証、Schema/Fixture、文書等 | PRで成功 |
| `.github/workflows/package-check.yml` | Package manifest / License/禁止資産/ID検査 | PRで成功 |
| `.github/workflows/release-check.yml` | Tagと`package.json` version整合を検査（Release自動発行しない） | dry-runまたはテスト可能、Tag未発行ならその旨記録 |
| `tools/validate_contracts.py`, `tools/validate_package.py` | テスト可能な静的バリデータ、明示的Exit Code、不正入力で失敗 | ローカルとCIで一致 |
| `tests/fixtures/` | 成功・失敗の小型匿名JSON fixture | Contractの境界をテスト |
| `docs/legal/` | LICENSE_POLICY, DATA_ATTRIBUTION, TRADEMARK_AND_BRANDING | 出典・コード権利・Dataset条件を分離 |

### 2.1 AGENTS.md 必須内容

- 最上位目的と非目的、`docs/architecture/system-spec.md`を正本とすること。
- 設計/Contract/実装/テスト/リリースの責務と、Phase境界。
- 名前、Package ID、namespaceを固定。コードは不要なSingleton/Manager/汎用化を避け、責務を分ける。
- メソッド名に `_` を入れないなど現行規約を採用する場合は、その出典を明示し、**別Repositoryの全規約を無差別にコピーしない**。
- `main`直Pushと自動Merge禁止。承認要求する操作の例を列挙。
- Scopeの変更とDesign DecisionはPRで提案し、利用者承認なしに仕様を改定しない。
- 推測API禁止、Version依存は公式Docs／実環境で確認。
- 何をどのコマンドで検証したかを保存。未実行・環境制約はBlocker扱い。
- 大量の説明を`AGENTS.md`へ複製せず、参照リンクで遅延読込する。

### 2.2 JSON Schema契約 — 最小必須の意味論

次のフィールドは**新規設計候補**。Workは命名差分を採用前に明示し、Codexと共有する。各JSON Schemaは`$schema`、`$id`、`title`、`type`、`required`を明記し、未知フィールドの許容ルールを記す。`schemaVersion`で契約の版を分離する。

**Inventory**:
- `schemaVersion`、`projectFingerprint`、`unityVersion`、`inspectedAt`、`packages[]`、`tools[]`、`diagnostics[]`。
- Package記録は`packageId`、`requiredVersion`、`resolvedVersion`、`source`、`directDependency`、`status`、`evidence`を持つ。値不明は`null`／UNKNOWN、推測のVersionで埋めない。
- `status` は `READY | MISSING | INCOMPATIBLE | BLOCKED | UNKNOWN`。`Client.List`の`offlineMode=true`はキャッシュが古い可能性を明示。

**Install Plan**:
- `schemaVersion`、`planId`、`revision`、`planHash`、`projectFingerprint`、`createdAt`、`expiresAt`、`baselineManifestHash`、`baselineLockHash`、`changes[]`、`warnings[]`。
- 各Changeは`packageId`、`sourceUri`、`versionOrCommit`、`reason`、`licenseUrl`を持ち、Allowlistに含まれる導入元だけ受け入れる。
- `planHash`は承認対象の不変情報だけから決定論的に計算。フィールド順・空白に依存しない正規化ルールを文書化し、共有Golden FixtureをPR-01に置き、PR-02でC#から同じファイルを使って検証する。
- **承認が成立したか否かをRequest BodyのBooleanだけで判定しない**。承認証跡はEditor側の信頼境界に保持。

**Install Result**:
- `schemaVersion`、`jobId`、`planId`、`planHash`、`projectFingerprint`、`state`、`packages[]`、`evidencePaths[]`、`diagnostics[]`。
- `state` は `COMPLETED | PARTIAL | FAILED | CANCELLED`。`READY`などの診断状態と混ぜない。Job進行中の内部状態は別途管理する。
- Errorのコード・発生場所・再試行可否を区別。Tokenや私的パスは外部出力に入れない。

**契約テスト**: 成功/欠落フィールド/不明なStatus/無効URL/異なるProject/期限切れ/Hash不一致/秘匿情報混入のFixtureを用意。Schemaで表現できない意味検証（Hash再計算、Allowlist、承認、実Projectとの照合）はC# Validation層の責務として明記する。

### 2.3 Skillの状態管理

元仕様で定義された次の6 Skillの名称を維持する。全て`SKILL.md`のFrontmatterに`name`/`description`を持たせる。既存機能がないPhase 0では、実行可能な機能を偽装せず、`Not implemented`・Planのみ可などを明示する。

| Skill | 役割 | Phase 0での扱い |
|---|---|---|
| `plateau-world-planner` | 調査→範囲→品質→実行のCoordinator | Workflow/Decision定義のみ |
| `plateau-location-research` | Factと出典の調査 | 出典の記録契約・未確認扱いを定義 |
| `plateau-area-design` | 候補範囲・重複計画 | Schema参照と承認境界定義のみ |
| `plateau-quality-design` | LOD/品質の比較 | 推定値とFactの区別を定義 |
| `plateau-city-import` | 承認済みImport実行 | **Phase 0では実行禁止** |
| `plateau-result-validation` | Evidence/PNG/出典の検証 | 未実行E2Eを合格にしない規則のみ |

## 3. PR-02 — Unity実装仕様（Codex Local）

### 3.1 Assembly構成

- `DarumaPPAP.PlateauWorldFlow.Core.Editor`（設計名）：SDK/MCP/CLIを参照しない状態・Plan・Validationの中核。
- `DarumaPPAP.PlateauWorldFlow.Setup.Editor`（設計名）：Bootstrap、依存カタログ、診断、Install Plan、Approval Gate、UPM Job、復元。SDK型を一切参照しない。
- `DarumaPPAP.PlateauWorldFlow.Tests.Editor`（設計名）：EditModeでCore/Setupをテスト。Test Frameworkの実Assembly参照はUnity 6000.3で確認する。
- `Editor/Plateau`、`Editor/Integrations/UnityMcp`、`Editor/Integrations/UnityCli`はこのPhaseで**実機能を作らない**。必要ならREADMEや設計図で予定として表現し、未検証のasmdefを先行作成しない。
- `.asmdef`・C#・`.meta`をUnity Editorまたは検証可能な手順で整合させる。`versionDefines`/`defineConstraints`は任意依存実装の際に公式仕様と実Compileで検証する。

### 3.2 Phase 0コンポーネント

1. **Bootstrap**: Editorロード後の安全なタイミングに初回Read-only診断を予約。Domain ReloadやBatch Modeでダイアログループを作らない。初回通知はProject+Package Revisionをキーに制御。
2. **DependencyInventory**: `Client.List(true, true)`に加えProjectのmanifest/lockを照合。Source/Version/直接間接/不明を区別。UPM Requestは非同期完了を監視する。
3. **DependencyCatalog**: WorldFlow管理の検証済みPackage ID・Source URL・Version・利用理由・ライセンスURL。PLATEAU SDK `v4.3.0`を初期候補。Toolkits/MCP/CLIは任意で初回自動追加対象外。
4. **Install Plan**: 読み取り専用で作成。Project fingerprint、manifest/lock baseline、対象Source・Tag、plan hash、有効期限、変更警告を固定。既存SDKを勝手に更新・ダウングレードしない。
5. **Approval Gate**: 通常の対話Editorでは標準Dialogで実ユーザー入力を取得。AI送信のBooleanのみは無効。Batch Mode/非対話環境ではMutate不可、承認待ちまたはBLOCKEDを返す。
6. **Installer**: 承認された同一Planか再照合し、`Client.Add`または妥当性が検証された`Client.AddAndRemove`（remove配列は空）を非同期実行。並列UPM Requestを出さない。ユーザー指定の任意URLを通さない。
7. **Recovery**: `Library/PLATEAUWorldFlow/Setup/`にJob/Plan/段階をProject-localで保存。Reload時にUPM結果とManifestを再診断し、実行中か不明な状態でAddを再発行しない。手動再診断・失敗理由・部分成功を出す。
8. **Menu**: `Tools > PLATEAU WorldFlow > Check Dependencies` からいつでも明示再診断。常設Windowは作らない。
9. **Evidence**: 前後manifest/lockの安全化した差分、Plan Hash、Job ID、エラー、再診断結果。認証文字列・ユーザー固有Path・プライベートURLをPRに送らない。

### 3.3 処理状態と二重実行対策

```text
Bootstrap → Inspect(Read) → {READY | MISSING | INCOMPATIBLE | BLOCKED | UNKNOWN}
MISSING → Plan(Read) → WaitingForLocalApproval → ApprovalGranted
ApprovalGranted → RevalidatePlan+Project → PersistJob → UPM InFlight
UPM InFlight → Reload/Retry reconciliation → Reinspect → {COMPLETED | PARTIAL | FAILED | CANCELLED}
```

- `Client.Add`のリクエスト完了だけで`READY`扱いしない。解決済みVersion/Source、Compile、Capabilityの再評価まで必要。
- `Client.List`失敗、通信不能、Lock競合はUNKNOWN/BLOCKED/FAILEDを理由別に分ける。
- 保留・拒否時のmanifest/lock差分は**0件**。OSのGit LFS導入、Registry設定変更、自動Rollbackは禁止。
- Project移動・manifest変更・計画期限超過・追加対象変化などは承認済みPlanを失効させる。ユーザー承認を使い回さない。
- Reload中にUPM処理が完了したか不明な場合、まず現ProjectとJob記録を**照合する**。証拠なしの再Addは禁止。

## 4. GitHub Actions / 静的CI

- `pull_request`（必要に応じ`push: main`）で起動。`permissions: contents: read` を宣言。
- Public外部PRのコード実行に秘密情報、`pull_request_target`、特権Self-hosted Runner、Unityライセンスを使わない。
- 使用Actionは現行Repositoryの制限に合わせて**完全長40文字Git Commit SHA**でpinする。適当なSHAを書かない。許可リストも確認。GitHub-hosted runnerを原則とする。
- Python依存は必要最小限かつバージョン固定。`pip install`の任意外部スクリプト実行は禁止。
- `validate.yml`: schema構文、良/悪Fixture、Skills frontmatter・リンク、基本規約。`package-check.yml`: package IDとルート一致、license/notices、禁止パス/ファイル、外部Git依存なし。`release-check.yml`: リリースTagとPackage Version一致（**リリースしない**）。
- 各jobの名称は固定し、最初のCI成功後にGitHub Rulesetの`Require status checks to pass`へ登録する。変更したらRulesetのCheck名も点検。
- Unity E2Eは当初Windowsローカルで手動実行。CIの緑とUnity実機成功は別。PRにはそれぞれ実行/未実行/失敗の欄を設ける。

## 5. テスト仕様と証跡

### 5.1 Static（Work PR-01）

| ID | 内容 | 成功条件 |
|---|---|---|
| S-01 | 元仕様への参照・識別子一致 | 全箇所Package ID一致、READMEリンクが有効 |
| S-02 | Schema陽性Fixture | 3契約すべてPass |
| S-03 | Schema陰性Fixture | 欠落・不正Enum・無効型がReject |
| S-04 | Script exit code | 不正Packageと禁止ファイルでnon-zero |
| S-05 | Skills Validation | 6つの`SKILL.md`とFrontmatter・Phase境界 |
| S-06 | OSS/セキュリティ検査 | Secret/大型データなし、Notices整合、CI read-only |
| S-07 | CI SHA pin | `uses:`に未固定アクションがない |
| S-08 | Release未発行 | TagもReleaseも作成していない |

### 5.2 Unity / EditMode（Codex PR-02）

| ID | 内容 | 成功条件 |
|---|---|---|
| U-01 | SDKなしCompile | Core/Setupが成功、未導入SDKの参照エラー0 |
| U-02 | 初回診断 | 起動時読取のみ、UPM追加0件 |
| U-03 | 欠落SDK | MISSING、正しい導入Source/Tagを提示 |
| U-04 | 保留/拒否 | manifest/lock差分0、再診断可能 |
| U-05 | 承認の偽装 | AIのBoolean/無効Plan/別ProjectでUPM呼出0件 |
| U-06 | 実承認 | 対象・Source・Tag・Projectを確認後のみ追加 |
| U-07 | Version / Embedded競合 | 既存を無断上書きせずINCOMPATIBLE |
| U-08 | Offline / Git LFS不足 | 原因を区別し、tgzの手動案内 |
| U-09 | Domain Reload | 再通知ループなし、Job復元、重複Addなし |
| U-10 | UPM失敗 | FAILED/PARTIALを混同しない、復旧案内あり |
| U-11 | 並行要求 | 同時UPM mutation 0 |
| U-12 | 手動メニュー | 診断を再実行できる |
| U-13 | Batch Mode | ユーザーダイアログ無し、未承認Mutate無し |
| U-14 | READY判定 | 解決・Compile等を再検証してREADY |
| U-15 | Schema互換 | C#生成・検証したJSONがContract Testを通る |
| U-16 | Generated Files / Evidence | Gitに機密情報・巨大Assetを混入させない |

### 5.3 テストの種類を混同しない

- **単体**：Fake UPM/Clock/Project File Readerと一時ディレクトリで再現。Production codeからUnity Package Managerの操作を分離できる必要があるが、目的外の抽象層は増やさない。
- **EditMode**：Unity EditorでMenu/Bootstrap/Serialization/状態復旧の確認。
- **手動E2E**：SDKなしUnity Project → 導入提案 → 保留 → 実承認 → Git LFS/ネットワーク/Import可否を検証。**外部依存のインストールはユーザー承認後のみ**。
- **実行環境**：Unity exact version、OS、Package/SDK source/version、git SHA、実行コマンド、ExitCode、ログ/スクリーンショット、変更ファイル一覧をEvidenceとして記録。
- **判定**：`PASS / FAIL / BLOCKED / NOT_RUN`。`BLOCKED`と`NOT_RUN`をPASSにしない。

## 6. Phase 0 Definition of Done

### PR-01をMerge候補にできる条件

1. S-01〜S-08がPASS。GitHub Actions上のStatic CIがGreen。
2. Schemaのrequired/statusが元仕様と整合、Python Fixtureで陽陰テスト済み。
3. `README`に非公式免責、`LICENSE`既存著作権表示維持、third-party資産未同梱の明示。
4. `AGENTS.md`/Skills/Docsが未実装機能を『可能』と誤認させない。
5. 変更一覧、リスク、未実装・未検証、次PRの依存が記録済み。
6. WorkはPRだけ作り、**Mergeはしない**。

### PR-02をMerge候補にできる条件

1. SDKなしCompile成功、U-01〜U-05/U-09/U-12/U-13/U-15は実行証跡付きPASS。
2. 実承認後の導入U-06/U-14は安全な検証Projectで成功。実行できない場合はBLOCKEDであり、Phase 0全完了を名乗らない。
3. U-07/U-08/U-10/U-11/U-16で失敗・既存SDK・オフライン等の安全性を確認（Fakeでの検証と実E2Eを区別）。
4. 未承認の`manifest.json`/`packages-lock.json`変更0件。第三者Asset・機密情報のPublic混入0件。
5. Static CI Green、Unity EditMode Testと手動E2Eログ、Contract結果、Known IssuesをPRに掲載。
6. Workがコードをレビューし、**最終Mergeは本人のみ**。

## 7. 注意事項・人間のDecisionが必要な点

- `LICENSE`既存の`Daruma`表記を`DarumaPPAP`に変えるかは権利者判断。PR-01では変更不要。
- GitHub Settings（Actions許可リスト、SHA-pinポリシー、Read-only Workflow Token、fork実行承認、Secret scanning）は連携APIから未確認。既存設定を推測して説明しない。CI失敗時に本人へ最小限の確認を求める。
- PLATEAU SDK `v4.3.0`・Toolkitsの相性、CLI/MCPの実APIはPhase 0完了の前提に加えない。互換性の確約は後続PhaseでE2Eを要求。
- PR-01 Merge時点ではUPM導入の成功は確認されていない。PR-02の安全なUnity検証が完了するまで製品のREADMEに『動作確認済み』と書かない。

## 8. 正式資料（今回の設計確認先）

- Unity PackageManager `Client.List`: https://docs.unity3d.com/jp/current/ScriptReference/PackageManager.Client.List.html
- Unity PackageManager `Client.AddAndRemove`: https://docs.unity3d.com/ja/current/ScriptReference/PackageManager.Client.AddAndRemove.html
- Unity PackageManager `Events.registeredPackages`: https://docs.unity3d.com/jp/current/ScriptReference/PackageManager.Events-registeredPackages.html
- Unity asmdef Version Defines / Define Constraints: https://docs.unity3d.com/ja/6000.0/Manual/assembly-definition-includes.html
- GitHub Actions設定: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository
- GitHub Actions Secure Use: https://docs.github.com/en/actions/reference/security/secure-use
- Codex Goals: https://developers.openai.com/cookbook/examples/codex/using_goals_in_codex
- OpenAI Codex AGENTS/Skillsの運用例: https://developers.openai.com/blog/skills-agents-sdk

**最優先**: 安全な承認GateとSDKなしCompile。『大量の機能を実装した』ではなく、検証可能なPhase 0完成を目的とする。

## 実装参照

- [システム設計仕様書](system-spec.md)
- [Setup JSON契約](../contracts/README.md)
- [検証方針](../ai/verification-policy.md)
- [Codex Localへの引き継ぎ](../ai/development-handoff.md)
