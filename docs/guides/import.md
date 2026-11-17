# CityGMLのImportと復旧

採用済みの計画を現在のDatasetと照合し、**Unity Editorで利用者が承認した後**に新しいSceneへ取り込みます。詳細なJSON形式は[Import契約](../contracts/import.md)を参照してください。

## 開始前

- [セットアップ](../../Packages/com.darumappap.plateau-worldflow/README.md)でPLATEAU SDKの状態を確認してください。Importには公式SDK `4.3.0`のGit直接依存・固定Commit一致が必要です（[互換性](../compatibility.md)）。
- [地点・範囲・品質の計画](planning.md)を採用し、`stage=PLAN_READY`かつ`status=READY`の計画を取得してください。
- LocalのCityGMLを使う場合は、Editorプロセスに`WORLDFLOW_LOCAL_DATASET_ROOT`、`WORLDFLOW_LOCAL_DATASET_ID`、`WORLDFLOW_LOCAL_DATASET_VERSION`を設定します。
- 対応範囲は[互換性](../compatibility.md)を確認してください。

## 実行フロー

```text
採用済み計画
    ↓
import.prepare（GML・範囲・品質・保存先を照合）
    ↓
import.inspect（計画の有効性を検査）
    ↓
import.request_approval（Unity Editorで確認画面を表示）
    ↓
ユーザーがEditorで明示承認
    ↓
import.start（非同期Job開始）→ import.status（進捗を照会）
    ↓
import.verify / import.artifacts（Scene・PNG・結果を検証）
```

UnityMCPの入口は`worldflow_import`、公式Unity CLIは`worldflow import`です。どちらも共通のJSON文字列を送ります。Project・取得元・地物/LOD・座標系・保存先を確認し、問題なければ**Editor上で承認**してください。チャットの「OK」やJSONの`approved`ではImportを開始できません。

| 結果 | 保存先 |
| --- | --- |
| 新規Scene | `Assets/PLATEAUWorldFlow/Imports/<importPlanId>/City.unity` |
| Job/Result/Evidence/PNG | `Library/PLATEAUWorldFlow/Import/jobs/<jobId>/` |

`import.start`は完了を待たずに戻ります。`COMPLETED`（Import成功）と`captureStatus`（PNGの状態）は別々に確認してください。PNGは自動でチャットに表示されず、Project内のファイルとして参照します。

## 保存失敗・中断した場合

1. Unityに保存エラーが出たら内容を確認し、ダイアログを閉じます。
2. 元のJobを`import.status`で照会し、`NEEDS_RECONCILIATION`なら`Tools > PLATEAU WorldFlow > 中断したImportを照合`を実行します。
3. Scene/Assetの残存を確認し、存在する場合は`PARTIAL`、存在しないと確認できる場合は`FAILED`に確定します。不明な場合は照合待ちを維持します。
4. 未保存Sceneが残る場合、必要な作業結果を専用の保存先に手動保存してから次へ進みます。
5. 無断の自動削除・自動再実行はしません。再試行が許可される場合も、**新しいPlanとEditor承認**が必要です。

Import終了後はSceneの再読込、既存Sceneとの分離、GML別結果、PNG Hashの整合性を確認します。SDKの終了通知だけを成功とみなしません。
