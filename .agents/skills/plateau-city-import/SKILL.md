---
name: plateau-city-import
description: "採用済み計画からEditor承認付きCityGML Importを案内し、Jobと証跡を照合する。"
---

# plateau-city-import

## Trigger

採用済みの読み取り専用Planをもとに、都市モデルのImport、実行状態の確認、失敗後の復旧を依頼されたとき。

## 入力

対象Projectの識別子、採用済みPlanningResult（PLAN_READY / READY）、出典・メッシュ・地物/LOD、座標系、取得元、保存先と利用者の選択。

## 出力

現在のImport Planと確認事項、Unity Editorの承認状態、Job ID・Job状態、保存Scene・PNG・Evidenceの検証結果、残る未確認事項。

## 中止条件

PlanningResultが未採用/失効/改ざん、現在のDatasetや地物LOD/座標系の照合が不十分、Projectや許可取得元が不一致ならImportへ進まない。**無承認Importは実行禁止**。AIによる同意代行、JSONのapprovedフィールド、Batch ModeによるEditor承認の代用をしない。

`NEEDS_RECONCILIATION`、`PARTIAL`、未保存の作業Scene、他Projectへの変更が疑われる場合、勝手に削除/再実行しない。Editor内の検査・利用者確認を要求する。

## 責務と実行境界

地点・範囲・品質の計画は読み取り専用。Import時は`worldflow_import`（UnityMCP）または`worldflow import`（公式CLI）のJSON文字列入口を使う。現在のPlanに対して`import.prepare`→`import.inspect`→`import.request_approval`を実行し、**Unity Editorで利用者が承認**した後だけ`import.start`を呼ぶ。実行中は`import.status`、成功後は`import.verify`と`import.artifacts`で証跡を照合する。`import.cancel`は要求であって即時成功確定ではない。

`planHash`とProjectを固定した同一Plan以外を流用しない。Jobの状態確認がタイムアウトしたら同一Jobの状況を再照会し、自動再送で追加Jobを作ろうとしない。具体的な形式と手順は以下の契約を参照。

## 参照契約

- [Import契約](../../../docs/contracts/import.md)
- [Import利用・復旧手順](../../../docs/guides/import.md)
- [互換性と制約](../../../docs/compatibility.md)
- [Planning契約](../../../docs/contracts/planning.md)
- [Setup契約](../../../docs/contracts/README.md)
- [製品の正本](../../../docs/architecture.md)
