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

Phase 0ではワークフローと判断の定義まで。Unity接続やImportは未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-install-plan.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
