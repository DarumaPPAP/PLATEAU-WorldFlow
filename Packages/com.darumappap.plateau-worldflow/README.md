# PLATEAU WorldFlow

`com.darumappap.plateau-worldflow` は、PLATEAU都市モデルの調査・計画・Importを支援するUnity Editor向けPackageです。Phase 0ではSDKを参照せずに依存診断と承認付きSetupを行います。初期検証基準は Unity `6000.3.10f1` 以上です。

1. Editor起動後にSDKを読み取り診断します。`Tools > PLATEAU WorldFlow > Check Dependencies` で再診断できます。
2. SDKが欠落し、GitとGit LFSを利用できる場合は `Review and Install SDK` からProject、固定Commit、Package Version、影響を確認します。追加は標準ダイアログでの承認後に開始します。
3. 進行中のJobと診断・差分はProjectの `Library/PLATEAUWorldFlow/Setup/` に記録します。Reload後に結果が確定できない場合は追加を停止し、Package Managerと `Check Dependencies` で実Projectの状態を確認できます。

GitやGit LFSを利用できない場合は、公式Releaseの `.tgz` を選び、Unity Package Managerの `Install package from tarball` を利用できます。都市モデルのImportは後続Phaseの対象です。利用者向けのGit導入は動作確認後にPackage subfolderと安定Tagを指定して案内します。

本プロジェクトは独立した非公式OSSです。国土交通省およびProject PLATEAUによる開発、承認、公式サポートの対象外です。SDK、都市データ、第三者資産は同梱していません。[MIT License](LICENSE) · [第三者表示](THIRD_PARTY_NOTICES.md)
