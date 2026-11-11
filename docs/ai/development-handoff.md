# Codex Localへの引き継ぎ

PR-01を利用者がレビューし、手動でSquash Mergeした後にPR-02を開始します。実装根拠は [AGENTS.md](../../AGENTS.md)、[正本](../architecture/system-spec.md)、[Phase 0仕様](../architecture/phase0-implementation.md)、[JSON契約](../contracts/README.md)、[検証方針](verification-policy.md) です。

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

PR-01で用意したのは静的契約とPackage基盤です。Editor C#、Package導入、Unity Compile、EditMode、実操作E2EはPR-02の対象です。MCP／CLI Adapter、都市モデルImport、動画、Toolkits、Runtime Streamingは後続Phaseの対象です。PR-02でも最終Mergeは利用者が判断します。
