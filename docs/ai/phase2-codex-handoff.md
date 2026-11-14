# Codex Local — Phase 2 自然言語による場所選定・計画 実装引き継ぎ

> この文書は**レビュー用実装計画**。まだPhase2機能は実装/検証されていない。PRは分割し、ユーザーの承認なしにMerge/Release/SDK追加/Importしない。

## 0. 実装開始条件

1. 現在の`origin/main`を`git fetch origin && git switch main && git pull --ff-only origin main`で取得。未コミット変更があれば停止して保全。Baseline SHAをPRへ記録。
2. `AGENTS.md` → [system-spec](../architecture/system-spec.md) → [Phase2仕様](../architecture/phase2-natural-language-planning.md) → [契約](../contracts/phase2-planning.md) → [調査ソース](../compatibility/phase2-research-source-evidence.md) → [Golden](phase2-golden-cases.md) → [検証方針](verification-policy.md) の順に読む。
3. Phase1 `worldflow_readonly` / `worldflow readonly` の6操作は既存APIとして扱う。Phase2のためにSchemaやPhase0承認Gateを勝手に変更しない。
4. 初回検証候補はWindows / Unity 6000.3.12f1 / SDK 4.3.0 / UnityMCP 10.3.0 / CLI 1.0.0-beta.17 / Pipeline 1.0.0。環境を変える場合は実際のVersionを記録する。検証用Projectの利用承認を前提とする。

## 1. PR分割・受入基準

| PR | 責務 | Done |
| --- | --- | --- |
| **A: Contract + State** | Session/候補/出典/Decision/PlanのSchema・最小検証器・Golden、Phase1参照の失効 | Fakeデータで状態遷移/失効/無承認操作禁止を再現、既存静的CIとUnity EditMode回帰 |
| **B: Research/Source** | AIクライアントから公式PLATEAU MCPを**任意**ソースとして検証し、自治体/外部ID→Phase1取得元への同定境界を定義 | 公式MCPあり/なし、timeout/結果打切り/ソース矛盾のPoC。必要ならAdapterは後続の小PRへ分ける |
| **C: Skills/Planner** | 既存6 Skillsの担当/入出力をPhase2契約へ接続、Fact/Decisionの対話フロー、品質比較 | 明示選択のない`AREA_SELECTED`不可、LLM依存しないValidator、Import禁止 |
| **D: Integration/Eval** | WorldFlow Phase1両経路、PNG Manifest/PlanID、Golden LLM・Live E2E | Provenance、SDK有無、Reload、MCP/CLI条件、Read-only不変性、エラー縮退を証跡付きで確認 |

依存PRはStackedしないことを推奨: **AをMerge→B→C→D**。並行化する場合も後続PRは新mainへの整合・差分・CIを検証し、Squash Merge後の履歴競合を防ぐ。

## 2. PR-Aに渡す最初のGoal

~~~text
/goal DarumaPPAP/PLATEAU-WorldFlow のPhase2「自然言語による場所選定・計画」に向けた
PR-A（Contract + State）だけを実装する。

必ず最新origin/main SHAをBaselineにする。
正本: AGENTS.md / docs/architecture/system-spec.md /
docs/architecture/phase2-natural-language-planning.md /
docs/contracts/phase2-planning.md /
docs/compatibility/phase2-research-source-evidence.md /
docs/ai/phase2-golden-cases.md / docs/ai/verification-policy.md。

実装:
- Phase2のSession/PlaceCandidate/Evidence/Decision/AreaChoice/QualityChoice/Resultを
  既存Phase1とは別のJSON Schema Draft 2020-12として定義。
- プロジェクトID・取得元同一性・Phase1 Snapshot/Plan/Revisionに基づく
  有効期限と上流Decision変更時の失効を最小検証器で実装。
- 固有名詞に依存しないFake Goldenと契約テストを追加。
- Phase1の6 Read-only API / Phase0 Setup / Editor既存テストは維持。
- Import/Scene変更/UPM変更/外部Provider呼出し/LLM API導入はPR-Aで禁止。
- 既存Skillは最初のPRで大幅書換えせず、Schemaが確定してから接続。
- Python Static CI と Unity EditModeで重要ケースを検証。
- PR本文にBaseline、変更内容、PASS/FAIL/BLOCKED/NOT_RUN、
  実行Version、匿名証跡、未確認事項を記載。

制限:
- mainへの直接Push/無承認Merge/Tag/Release禁止。
- Unity Projectの変更系実行は明示承認が必要。
- 既存Architectureを複製しない。冗長なManager/Factory/Interfaceを増やさない。

Done: Phase2契約とRevision/失効ValidatorがFakeで再現し、
Phase1の全回帰と静的CIが成功し、PR-Aがレビュー可能なこと。
~~~

## 3. PR-Bの実証順（調査結果で判断）

1. [公式PLATEAU MCP](https://docs.plateauview.mlit.go.jp/mcp/overview/)の対応ツールを調べ、`plateau_search_areas`→`plateau_search_datasets`→`plateau_get_dataset`の実JSONを匿名化して保存。
2. 取得した外部IDとPhase1の`datasets.list`/`coverage.inspect`を比較。ID/年度/Sourceの同一性を未証明なら「候補」扱い。
3. 100件打切り/ネットワーク不達/仕様変更のFakeと実例を分けて検証。
4. Phase2の設計に外部MCPの導入を強制しない。公式Agent Skillsの構造を参考にする場合はライセンス/更新運用をレビュー。
5. 位置特定のProviderは作品公式/自治体/施設公式の出典を優先し、利用条件未確認なGeocoding APIを自動実装しない。

## 4. CodexとWorkの分担

- Work: 仕様の改善、Providerの一次資料調査、合同Goldenレビュー、PRの内容確認（実機PASSを偽らない）。
- Codex: Local UnityでSchema/State/C#最小実装、UnityMCPテスト、同じProject SessionでPhase1 read-only E2E、検証ログ、PR作成。
- ユーザー: 変更系依存導入のEditor承認、重要な地点/品質/利用範囲のDecision、PRレビュー/最終Merge。
- エージェントはGitHub権限を持っていても、ユーザーに代わってPhase3 Import承認を出してはいけない。

## 5. 最終PRに必須の証拠

- Contract: JSON SchemaとPython/C# GoldenのCase ID、PASS数、元Versionとの互換性。
- Local: Compile/EditModeの数、Unity/SDK/CLI/MCP Version、Project不変性の匿名差分。
- LLM: Model/Prompt/Skill SHA、Ground Truth、違反0件の判定方法、実行回数。
- Live: URL/自治体/取得日時、Phase1 Dataset ID照合、PNG Manifest、ClientにPNGがinline可能か／`ARTIFACT_ONLY`か。
- Security: Prompt Injectionに対してMutate=0、秘密出力=0。権利不明資料を同梱しない。
- Merge: 最終承認は利用者。前PR完了後に次PRのBaselineを更新。
