---
name: plateau-quality-design
description: "確認済みの範囲に対するLODと品質の選択肢を比較する。"
---

# plateau-quality-design

## Trigger

範囲承認後、LOD、用途、予算や実測対象を比較するとき。

## 入力

承認済み範囲、配布元のLOD情報、対象用途、計測済みの予算。

## 出力

品質候補、採用理由、想定コストと測定方法。推定値には前提を添え、出典で確認した値と区別する。

## 中止条件

DatasetのLODや対象範囲が不明なら数値を確定せず、根拠を確認する。

## Phase境界

Phase 0では比較と測定計画まで。LOD変換やSDK経由の品質検査は未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-inventory.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
