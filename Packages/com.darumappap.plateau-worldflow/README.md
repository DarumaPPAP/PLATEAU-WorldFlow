# PLATEAU WorldFlow

`com.darumappap.plateau-worldflow` は、PLATEAU都市モデルの調査・計画・Importを支援するUnity Editor向けPackageです。Phase 0ではSDKを参照せずに依存診断と承認付きSetupを行います。初期検証基準は Unity `6000.3.10f1` 以上です。

1. Editor起動後にSDKを読み取り診断します。`Tools > PLATEAU WorldFlow > Check Dependencies` で再診断できます。
2. SDKが欠落し、GitとGit LFSを利用できる場合は `Review and Install SDK` からProject、固定Commit、Package Version、影響を確認します。追加は標準ダイアログでの承認後に開始します。
3. 進行中のJobと診断・差分はProjectの `Library/PLATEAUWorldFlow/Setup/` に記録します。結果が不明な場合は再照合後に追加を停止します。Package Managerで処理が終わったことを確認し、`Check Dependencies` でJobを再照合できます。

Jobの結果を引き続き確定できない場合は、Editorを閉じてPackage Managerの処理が終了していることを確認してください。SDKが未導入で、manifestとlockがPlanの記録と一致することを確認できた場合に限り、`pending.json` を証跡として別名で保管し、Editorを開いて新しいPlanを作成できます。SDKが存在する場合やファイルに差分がある場合は、既存のPackage構成とJob記録を保全して状況を確認してください。

GitやGit LFSを利用できない場合は、公式Releaseの `.tgz` を選び、Unity Package Managerの `Install package from tarball` を利用できます。都市モデルのImportは後続Phaseの対象です。利用者向けのGit導入は動作確認後にPackage subfolderと安定Tagを指定して案内します。

最小構成のProjectでは、SDK 4.3.0のコンパイルにUnityの組み込みモジュール `Terrain Physics` と `Unity Web Request Texture` が必要です。該当するコンパイルエラーが出た場合は、Package Managerでモジュールを有効にしてから `Check Dependencies` を再実行してください。

本プロジェクトは独立した非公式OSSです。国土交通省およびProject PLATEAUによる開発、承認、公式サポートの対象外です。SDK、都市データ、第三者資産は同梱していません。[MIT License](LICENSE) · [第三者表示](THIRD_PARTY_NOTICES.md)
