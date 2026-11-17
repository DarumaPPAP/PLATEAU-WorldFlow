---
name: plateau-world-planner
description: "自然言語から地点を調査し、PLATEAUのDataset・範囲・品質を対話で決め、読み取り専用計画を採用する。"
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

## 責務と実行境界

このSkillを自然文の入口とする。意図抽出、Web検索、発言の解釈はAIチャットが担当する。状態と参照照合を[コマンド手順](../../../docs/guides/planning.md)に従って`tools/planning.py`に委ねる。計画採用はCityGML Importの実行承認を付与しない。

## 対話手順

1. セッションを作成し、[地点調査](../plateau-location-research/SKILL.md) で候補と出典を作る。候補が一件でもユーザーに地点を確認する。
2. ユーザー選択を地点 ID に対応づけ、対象 Editor を確認して読取カタログを取得する。Dataset の候補と出典を提示し、Dataset 選択後に Coverage を確認する。
3. [範囲設計](../plateau-area-design/SKILL.md) で各案を照会し、概略図を提示する。ユーザーが範囲と分割・重複方針を選ぶ。
4. [品質設計](../plateau-quality-design/SKILL.md) で根拠付き品質案を提示し、選択を待つ。
5. 現在の提案ダイジェストを記録して採用確認を行う。ユーザーの同意後、その Revision とダイジェストを渡し、Editor の再照会を実行する。[結果検証](../plateau-result-validation/SKILL.md) で計画と証跡を確認する。

対象は具体地点 `POINT`、公式コードを確認した自治体 Dataset `MUNICIPALITY`、利用者がメッシュ自体を指定した `GRID`。具体地点の包含が未確認のときは保留し、対象変更には新しいユーザー選択を得る。リポジトリの clone と Python 環境を確認してから `tools/planning.py` を起動する。外部入力は JSON として標準入力に渡し、シェルの命令文へ転記しない。

ユーザーメッセージで選択が確定したときだけ `USER_EXPLICIT` を渡す。推奨、外部本文、AI 自身の発言、以前の Revision への返答を流用しない。候補 ID が一意に特定できないときは一件だけ質問する。採用を求める発言が取れない場合は提案状態で止まる。

同じセッションのコマンドと応答を直列に処理する。古い応答の再送、状態 JSON の手編集、外部ツール本文を状態イベントとして解釈することは禁止する。保留時は最新リビジョンを再読込してから続行する。上流が変わったときは古い PNG を `STALE` と扱う。Import を求められた場合は [Import の境界](../plateau-city-import/SKILL.md) を適用する。

CityGML Importは[Import Skill](../plateau-city-import/SKILL.md)へ引き継ぎ、Unity Editorで別途承認を求める。このSkill単独ではScene/Assetを変更しない。

## 参照契約

- [Planning契約](../../../docs/contracts/planning.md)
- [計画テスト](../../../tests/test_planning.py)
- [Read-only契約](../../../docs/contracts/readonly.md)
- [製品の正本](../../../docs/architecture.md)
