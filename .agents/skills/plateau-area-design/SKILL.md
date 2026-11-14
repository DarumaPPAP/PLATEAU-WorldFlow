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

Phase1 `areas.plan`/`areas.preview`/`areas.preview_png`は実装済み。Phase2の自然言語に基づく範囲選択のCoordinatorは**未実装**。確認済みExtent/CRSのみを用い、Phase1の`NOT_COMPUTED`面積は`null`のまま表示。地点やCoverage変更時は古いPlan/PNGを失効。CityGML取得・Importは実行禁止。

## 参照契約

- [Phase2仕様](../../../docs/architecture/phase2-natural-language-planning.md)
- [Phase2計画契約案](../../../docs/contracts/phase2-planning.md)
- [Phase1範囲計画とPNG契約](../../../docs/contracts/phase1-readonly.md)
- [製品の正本](../../../docs/architecture/system-spec.md)
