# PLATEAU WorldFlow — Unity Package

`com.darumappap.plateau-worldflow` は、PLATEAU都市モデルの調査、範囲・品質の選択、Unity Editorへの承認付きImportを支援するPackageです。全体の機能は[プロジェクトREADME](../../README.md)を参照してください。

## セットアップ

1. UnityのPackage Managerから`package.json`を指定してPackageを追加します。初期のUnity基準は`6000.3.10f1`以上、実機検証済みは`6000.3.12f1`です。
2. Unity Editorの`Tools > PLATEAU WorldFlow > Check Dependencies`からPLATEAU SDKの状態を診断します。
3. SDKが未導入の場合は、提示された取得元・Version・Project変更を確認してから「SDKを追加」を承認します。後で追加する場合は`Review and Install SDK`から再開できます。
4. `Check Dependencies`でSDK VersionとCompile・Capabilityを確認してください。

SDKのGit導入にはGitとGit LFSが必要です。**現行のImport機能は、公式SDK `4.3.0`をGit直接依存として導入し、固定Commitまで一致することを要求します。** 公式`.tgz`を手動で導入できる場合でも、Packageの取得元がGitにならないため、WorldFlowのImportは実行できません。Git/LFSが使えない環境は対応待ちとして扱い、SDKの導入方式を無断変更しないでください。

依存Jobの記録はProjectの`Library/PLATEAUWorldFlow/Setup/`に保存されます。結果不明の場合は重複追加せず再照合してください。

## AI・Importを使う

- [AIから地点・範囲・品質を計画する](../../docs/guides/planning.md)：Repositoryのclone、Python、Skillsを別途準備します。
- [CityGMLをImportする](../../docs/guides/import.md)：採用済みPlanを照合し、Unity Editorで**別途承認**して新規Sceneへ取り込みます。
- [検証済み環境と制約](../../docs/compatibility.md)

SDK 4.3.0の最小Projectでは、Unity組込モジュール`Terrain Physics`と`Unity Web Request Texture`が必要になる場合があります。該当Compileエラーがある場合はこれらを有効化し、依存診断を再実行してください。

本Packageは非公式OSSです。SDK/都市データ等の第三者素材は同梱していません。[MIT License](LICENSE)・[第三者表示](THIRD_PARTY_NOTICES.md)。
