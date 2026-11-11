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

Phase 0では検証結果の判定規則まで。PNG・動画撮影やImport結果の実検証は未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-install-result.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
