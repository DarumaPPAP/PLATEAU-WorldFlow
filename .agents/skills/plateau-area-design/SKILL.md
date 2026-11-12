---
name: plateau-area-design
description: "採用地点に対する対象範囲、分割、重複処理の選択肢を定義する。"
---

# plateau-area-design

## Trigger

地点の承認後、取得範囲や重複の統合・共有・分離を比較するとき。

## 入力

承認済み地点、候補範囲、座標参照系、Datasetの出典・粒度。

## 出力

候補範囲、重複の扱い、影響と承認対象。座標系が揃った根拠がある場合だけ面積を比較する。

## 中止条件

地点が未確定、座標系が不明、広域取得やScene書込みの承認が未取得なら実行に進まない。

## Phase境界

Phase 0では計画と承認境界の定義まで。範囲計算や取得処理は未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-install-plan.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
