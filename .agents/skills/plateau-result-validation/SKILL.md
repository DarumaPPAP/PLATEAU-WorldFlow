---
name: plateau-result-validation
description: "検証結果と証跡を照合し、未検証項目を記録する。"
---

# plateau-result-validation

## Trigger

静的検証や将来のUnity実行結果を確認するとき。

## 入力

対象SHA、検証コマンドと結果、証跡の参照、期待した結果。

## 出力

検証区分ごとのPASS、FAIL、BLOCKED、NOT_RUNと未確認項目。

## 中止条件

実操作の証跡がないときはE2EをPASSにしない。機密情報が含まれる証跡は公開を止める。

## Phase境界

Phase0/1で静的検証・Unity EditMode・Phase1概略PNG生成のE2Eを実施済み。Phase2のLLM/自然言語E2E、Phase3 Import結果検証と動画撮影は**未実装**。設計資料の追加を実行済みと誤認しない。

## 参照契約

- [Phase2 Golden](../../../docs/ai/phase2-golden-cases.md)
- [Phase2計画契約案](../../../docs/contracts/phase2-planning.md)
- [検証方針](../../../docs/ai/verification-policy.md)
- [Phase1 Read-only契約](../../../docs/contracts/phase1-readonly.md)
- [製品の正本](../../../docs/architecture/system-spec.md)
