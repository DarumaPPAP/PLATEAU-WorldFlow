---
name: plateau-result-validation
description: "読み取り専用PlanとImport後のScene・PNG・構造化証跡を照合し、未検証項目を区別する。"
---

# plateau-result-validation

## Trigger

読み取り専用計画の採用とImportの結果、静的CI、Unity実機での検証結果を確認するとき。

## 入力

対象SHA、検証環境とコマンド、PlanningResultまたはImport Job ID/Plan Hash、Result・EvidenceとProject内の生成物。

## 出力

検証区分ごとのPASS、FAIL、BLOCKED、NOT_RUNと、確認済みの証跡参照・未確認項目。

## 中止条件

実操作の証跡がないときはE2EをPASSにしない。Scene/Asset・PNGの実体やHashが一致しない場合は成功を断定しない。機密情報が含まれる証跡は公開を止める。

## 責務と実行境界

計画の検証時は`python tools/planning.py --state <記録> --validate`で計画と参照を検査し、採用直前は同じEditorの`areas.preview`と`adoptionCheck.status = READY`を確認する。失効時は再照会する。概略PNGはManifestのSHA-256と実ファイルサイズを照合し、`ARTIFACT_ONLY`ではProject相対パスで開く。

Import後は`import.status`、`import.verify`、`import.artifacts`によりJob、保存Scene/Asset、GML別結果、PNGの検査を行う。Importの`COMPLETED`とPNGの`captureStatus`は別に報告する。`NEEDS_RECONCILIATION`は利用者のEditor確認なく最終成功扱いにしない。Planと公開証跡にローカル絶対パスや個人情報を含めない。動画の検証は現時点で対象外。

## 参照契約

- [Import契約](../../../docs/contracts/import.md)
- [互換性と制約](../../../docs/compatibility.md)
- [計画テスト](../../../tests/test_planning.py)
- [Planning契約](../../../docs/contracts/planning.md)
- [検証方針](../../../AGENTS.md)
- [Read-only契約](../../../docs/contracts/readonly.md)
