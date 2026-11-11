---
name: plateau-location-research
description: "PLATEAUを利用する候補地点について出典と確認状態を記録する。"
---

# plateau-location-research

## Trigger

候補地点、座標、公開Datasetまたは出典を調査するとき。

## 入力

作品名・地名、利用目的、候補地域、参照可能な公式配布情報。

## 出力

地点・座標・出典URL・確認日時・Datasetの提供状況・確認状態を候補ごとに記録する。

## 中止条件

位置やDatasetの利用条件に根拠がない場合は未確認とし、確定や自動再配布を保留する。

## Phase境界

Phase 0では出典の記録契約まで。Dataset取得用の実装は未実装。

## 参照契約

- [Setup契約の範囲](../../../docs/contracts/README.md)
- [依存SetupのSchema](../../../docs/contracts/dependency-inventory.schema.json)
- [製品の正本](../../../docs/architecture/system-spec.md)

SetupのSchemaは依存関係専用です。地点、範囲、品質、ImportのJSONは後続Phaseで定義します。
