# Codex Localへの引き継ぎ（Phase 0完了記録）

**Phase 0のPR-01/02はMerge済みです。** 本文は当時の実装引き継ぎ記録として保持します。**現在の作業開始資料は [Phase 1 Codex handoff](phase1-codex-handoff.md)** です。開発時は [AGENTS.md](../../AGENTS.md)、[システム仕様](../architecture/system-spec.md)、[検証方針](verification-policy.md) を参照してください。

## Baselineと作業環境

| 項目 | 値または記入内容 |
| --- | --- |
| PR-01の分岐元 `main` | `b466f6a4d7577ab90214da45a4be186a2686ecec` |
| PR-02のBaseline | 手動Merge後に `git rev-parse origin/main` で取得したSHA |
| 作業ブランチ | `feature/phase0-setup-bootstrap` |
| 環境 | Windows、Unity Editorの正確なVersion、Test Framework、SDK、Git／Git LFS |
| 検証Project | 利用者が指定する使い捨てProject。公開ログでは匿名化 |

## 実装順序

1. 既存のUPM基礎構成にCore／Setup／TestsのEditor専用Assemblyを追加し、SDKなしCompileを確認する。
2. 初回と手動のRead-only診断、InventoryとInstall Planを実装する。キャッシュ情報と実Projectを照合する。
3. 標準EditorダイアログでPlanと対象Projectへの承認を取得し、変更直前にPlanを再検証する。
4. Jobを永続化し、非同期UPM処理とReload後の照合を実装する。結果不明時の再追加を抑止する。
5. Pythonと同じ [Golden Fixture](../../tests/fixtures/dependency-install-plan/golden-canonical.txt) をC#で検証する。実Projectと現時刻、Allowlist、承認を使うテストはPythonの模擬ケースと分ける。
6. U-01〜U-16を記録し、利用者が承認した場合だけ検証Projectで実導入を行う。UPM解決後にVersion、Source、Compile、Capabilityを確認する。

## PR-02の記録欄

| 検証 | コマンド／操作 | 結果 | 証跡 |
| --- | --- | --- | --- |
| S-01〜S-08 | 検証方針の静的コマンドとGitHub Actions | | |
| SDKなしCompile | 実行したEditorコマンドとProject条件 | | |
| EditMode | 実際の起動コマンドとテストフィルタ | | |
| 実操作E2E | 診断・保留・承認・再診断の操作とU-ID | | |

上記の記録欄はPhase 0の履歴です。Phase 1の実装・確認手順は[専用引き継ぎ](phase1-codex-handoff.md)を利用してください。CityGML Import、動画、Toolkits、Runtime Streamingは後続Phaseの対象です。
