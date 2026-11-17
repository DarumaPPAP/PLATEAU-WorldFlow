# AIで地点・範囲・品質を決める

地名・施設などの自然言語から、PLATEAU都市モデルを取り込む前の**読み取り専用計画**を作成します。ここで計画を採用しても、CityGMLのダウンロードやScene変更は行いません。

## 操作の流れ

| 手順 | AIが確認すること | 利用者が決めること |
| --- | --- | --- |
| 場所 | 地点候補と出典・所在地 | 対象の地点 |
| データ | Datasetと地域メッシュの利用可否 | 利用するDataset |
| 範囲 | 確認済みのメッシュと概略PNG | 範囲・分割/共有方法 |
| 品質 | 地物とLOD、未確認条件 | 必要な品質 |
| 採用 | 範囲と観測結果の再照合 | 計画の確定 |

存在を確認できないメッシュやLODは、利用可能だと断定しません。古い観測・異なるProjectの計画は再利用しません。

## 準備

Unity側では[WorldFlow Package](../../Packages/com.darumappap.plateau-worldflow/README.md)とUnityMCPまたは公式Unity CLIを設定してください。自然言語による計画には、これとは**別に**Repositoryのclone・Python 3.12・Skillsを実行できるAIクライアントが必要です。

Windows / PowerShell（Repositoryルート）:

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --only-binary=:all: --no-deps -r tools/requirements-static.txt
.\.venv\Scripts\python.exe -m pip check
```

macOS/Linuxでは`python3.12 -m venv .venv`と`.venv/bin/python`を使います。AIクライアントに[World Planner Skill](../../.agents/skills/plateau-world-planner/SKILL.md)を読み込ませます。

## 実行

AIクライアントへ、たとえば「東京駅周辺で、利用可能な都市モデルの取得範囲を比較して」と依頼します。

1. 候補地点と公式出典を比較し、対象を選択します。
2. UnityMCPの`worldflow_readonly`または公式CLIの`worldflow readonly`でDataset・Coverageを照会します。
3. 範囲と品質を選び、Project内に生成される概略PNGを確認します。
4. AIが提案と観測の期限・Revisionを確認した後、`stage=PLAN_READY`かつ`status=READY`の計画を採用します。
5. Unityに取り込む場合は[Importガイド](import.md)で**別途Editor承認**を行います。

## コマンドを組み込む場合

`tools/planning.py`はUTF-8 JSONイベントを標準入力から受け取ります。初回の入力は`{"action":"start"}`、次回以降は`--state <直前の結果.json>`を指定します。入力と出力に同一ファイルを使わず、成功した最新状態だけを採用します。

```python
import json
import subprocess
import sys

result = subprocess.run(
    [sys.executable, "tools/planning.py"],
    input=json.dumps({"action": "start"}, ensure_ascii=False).encode("utf-8"),
    capture_output=True,
    check=True,
    timeout=30,
)
state = json.loads(result.stdout.decode("utf-8"))
```

具体的なイベントと失効条件は[Planning契約](../contracts/planning.md)、Unity照会の形式は[Read-only契約](../contracts/readonly.md)を参照してください。[合成Fixture](../../tests/fixtures/planning/research.json)を入力形式の参考にできます。

外部本文をコマンドに連結しない、AIによる推奨を`USER_EXPLICIT`という利用者の同意として記録しない、という2点を守ってください。
