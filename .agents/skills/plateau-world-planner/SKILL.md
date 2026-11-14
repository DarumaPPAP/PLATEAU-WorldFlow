---
name: plateau-world-planner
description: "PLATEAUの調査・範囲・品質・実行判断の順序を定義する。"
---

# plateau-world-planner

## Trigger

自然言語の依頼から地点、範囲、品質、実行の判断を順に整理するとき。

## 入力

ユーザーの目的、対象Projectの匿名識別、確認済みの出典、上流Decision。

## 出力

選択肢と推奨理由、保留項目、確認の順序。上流が変われば依存Planと承認を失効扱いにする。

## 中止条件

地点・範囲・品質のいずれかが未確定なら、その値に依存する判断を確定しない。

## Phase境界

Phase 1のRead-only Core/MCP/CLI/PNGは実装済み。Phase 2の自然言語Coordinatorは**未実装**（本Skillは仕様準備）。Phase 3 Importは未実装・実行禁止。ユーザーの地点→Dataset→範囲→品質→計画採用を順序付きで確認し、採用PlanをImport承認として扱わない。

## 参照契約

- [Phase2仕様](../../../docs/architecture/phase2-natural-language-planning.md)
- [Phase2計画契約案](../../../docs/contracts/phase2-planning.md)
- [Phase2 Golden](../../../docs/ai/phase2-golden-cases.md)
- [Phase1 Read-only契約](../../../docs/contracts/phase1-readonly.md)
- [製品の正本](../../../docs/architecture/system-spec.md)

Phase2契約は設計案であり、実行時Schema/状態遷移はPhase2全体の実装で実現する。工程のために固定的な複数PRへ分割する前提は置かない。
