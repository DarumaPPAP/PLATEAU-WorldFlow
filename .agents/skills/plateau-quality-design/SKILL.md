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

## 責務と実行境界

範囲と重複方針の選択後に `quality` イベントで比較案を渡す。選択した利用可能メッシュすべてで確認できる LOD と地物を別々に照合する。Read-only APIは地物ごとの LOD の対応を返さないため、組合せの確認が要る場合は未確認と伝える。確認外の LOD や地物を希望された場合は `unverifiedRequirements` を提示し、既知の値で再選択するか保留する。LOD 無指定は空配列。Texture、容量、Memory、所要時間は未計測として扱う。ImportやLOD変換はこのSkillで実行しない。

このSkillは品質案の比較と採用まで。CityGML Importは[Import Skill](../plateau-city-import/SKILL.md)のPlan再照合とEditor承認が必要。

自動 Preset は全選択メッシュで確認済みの LOD から計画側で生成する。具体的な品質希望は `CUSTOM` として渡す。`FEATURE_LOD_ASSOCIATION` が残る要求は採用できないため、組み合わせの確認を待つか、利用者に条件の見直しを求める。

## 参照契約

- [Planning契約](../../../docs/contracts/planning.md)
- [Coverage契約](../../../docs/contracts/readonly.md)
- [製品の正本](../../../docs/architecture.md)
