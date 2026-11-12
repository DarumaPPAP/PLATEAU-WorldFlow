---
name: plateau-city-import
description: "承認済み都市モデルImportの前提と中止条件を定義する。"
---

# plateau-city-import

## Trigger

承認済みの都市モデルImportを依頼されたとき。

## 入力

対象Project、地点、範囲、品質、利用条件と承認済み計画。

## 出力

Phase 0では未実装として報告し、判断済みの入力と後続Phaseの前提を記録する。

## 中止条件

Phase 0では実行禁止。CityGMLのダウンロード、SDK呼び出し、Asset生成、Scene変更を行わない。

## Phase境界

Phase 0ではImport本処理とAdapterは未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-install-plan.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
