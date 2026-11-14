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

Phase1の`coverage.inspect`で確認できたLOD/地物を品質候補の根拠とする。Phase2の自然言語品質比較は**未実装**。Presetを使っても実証していないLOD/Texture/容量/Memory/速度を数値として確定しない。実際のImportやLOD変換はPhase3以降。

## 参照契約

- [Phase2仕様](../../../docs/architecture/phase2-natural-language-planning.md)
- [Phase2計画契約案](../../../docs/contracts/phase2-planning.md)
- [Phase1 Coverage契約](../../../docs/contracts/phase1-readonly.md)
- [製品の正本](../../../docs/architecture/system-spec.md)
