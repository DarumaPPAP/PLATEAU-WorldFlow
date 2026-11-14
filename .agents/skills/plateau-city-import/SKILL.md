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

Phase 2ではImportは未実装として報告し、計画案の入力とPhase 3の前提だけを記録する。

## 中止条件

Phase 2では実行禁止。CityGMLのダウンロード、Import SDK呼び出し、Asset生成、Scene変更を行わない。ユーザーの計画採用をPhase3のImport承認に読み替えない。

## Phase境界

Phase1のRead-only Adapterは実装済み。Phase3のImport本処理/Job/承認Gateは**未実装**。

## 参照契約

- [Phase2計画契約案](../../../docs/contracts/phase2-planning.md)
- [Phase2仕様](../../../docs/architecture/phase2-natural-language-planning.md)
- [Phase0 Setup契約](../../../docs/contracts/README.md)
- [製品の正本](../../../docs/architecture/system-spec.md)
