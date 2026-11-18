# PLATEAU-WorldFlow

**AIとの対話で3D都市モデルの場所・範囲・品質を選び、Unity Editorへの取り込みと結果確認までを支援するツールです。**

[Project PLATEAU](https://www.mlit.go.jp/plateau/) の都市モデル（CityGML）を扱う Unity Editor向けの非公式OSSです。PLATEAU SDK for Unityを置き換えるものではなく、**AIによる計画と、Unity上での承認付きImportをつなぎます**。

[利用ガイドと技術資料](docs/README.md)

## 主な機能

| 機能 | できること |
| --- | --- |
| **依存関係の診断・導入** | PLATEAU SDKの有無・Versionを確認。不足時は内容を表示し、Unity Editorで承認してからSDKを追加 |
| **場所・都市データの調査** | 地名・施設などの候補を調べ、対応するDataset、地域メッシュ、地物・LODの提供状態を確認 |
| **自然言語での計画** | AIとの対話で地点・データセット・範囲・品質を順に選択。根拠がないものは未確認として扱い、勝手に決定しない |
| **範囲プレビュー** | 対象メッシュの選択範囲・利用可否を概略PNGで確認 |
| **CityGMLのImport** | 承認済み計画を実データと照合し、ローカルまたは確認済みの公式配信データをUnityへ取り込む |
| **Sceneの分離・保存** | 専用の新しいAdditive Sceneを作成して保存。既存Sceneの無断上書きを防止 |
| **実行状態・結果の検証** | Import Jobの状態、GML別結果、Scene/Assetの実体、PNG証跡を照合。中断時の手動復旧を支援 |
| **AIツール連携** | UnityMCP `worldflow_import` と公式Unity CLI `worldflow import` から共通のImport処理を利用 |

## 利用イメージ

たとえば「東京駅周辺の都市モデルをUnityに取り込みたい」と依頼した場合、次のように進めます。

1. **候補地を確認**：地点の所在地と出典を調査し、選択します。
2. **取り込み範囲を決定**：利用可能なDataset・メッシュを確認し、概略PNGを見て選びます。
3. **品質を選択**：提供状況に基づき、地物やLODを選びます。
4. **Import内容を確認**：Unity Editorで取得元、対象GML、座標系、保存先などを表示します。
5. **明示的に実行**：ユーザーがEditorで承認した場合のみ、CityGMLを新規SceneにImportします。
6. **結果を検証**：保存したSceneとモデルの実体、Job結果、PNG証跡を確認します。

**AIとの会話で計画を採用しただけではImportを実行しません。** Unity Editorでの操作承認が別途必要です。対象エリアで都市データが提供されていない場合や、出典・LODなどを確認できない場合は、利用可能だと断定せず停止します。

## 必要な環境

| 項目 | 条件 |
| --- | --- |
| Unity | Packageの初期基準は `6000.3.10f1` 以上。**実機検証済みはWindows / Unity `6000.3.12f1`** |
| PLATEAU SDK for Unity | CityGML Importには `4.3.0` を使用（検証済み構成） |
| AIからのUnity操作 | UnityMCP `10.3.0` または公式Unity CLI `1.0.0-beta.17`（Pipeline `1.0.0`）を利用 |
| 自然言語の計画 | RepositoryのAgent Skillsを読み込めるAIクライアント、Git clone、Python `3.12` が必要 |
| Git / Git LFS | 現行Importで必要な固定コミット版PLATEAU SDKの導入に必須 |

UnityMCPとUnity CLIの両方を同時に導入することは必須ではありません。ただし、Importで**片方のみ**を導入した組合せの個別検証は未実施です。

## 導入・使い方

### Unity Package ManagerからGit URLでインストール（推奨）

1. Unityを起動し、`Window > Package Manager` を開きます。
2. `+` メニューで `Install package from git URL` を選択します。
3. 次のURLを入力して `Install` を押します。

```text
https://github.com/DarumaPPAP/PLATEAU-WorldFlow.git?path=/Packages/com.darumappap.plateau-worldflow
```

PackageはRepositoryのサブフォルダーにあるため、**`?path=/Packages/com.darumappap.plateau-worldflow` が必須**です。URLを指定するPCにはGitクライアント（2.14.0以上、WindowsではPATH設定）が必要です。導入後はPackage Managerの `In Project` に `PLATEAU WorldFlow` が表示されることを確認してください。

Unity Projectの `Packages/manifest.json` に直接記述する場合は、`dependencies` に次の項目を追加しても導入できます（既存の項目は維持してください）。

```json
"com.darumappap.plateau-worldflow": "https://github.com/DarumaPPAP/PLATEAU-WorldFlow.git?path=/Packages/com.darumappap.plateau-worldflow"
```

**Git URLから取得されるのはUnity Packageフォルダーだけです。** AIによる自然言語計画に必要なRepository直下の `.agents/skills`、`tools`、Pythonのスクリプトは含まれません。これらを利用する場合は別途Repositoryをcloneしてください。

### ローカルCloneからインストール（開発用）

Repositoryをcloneし、Unity Package Managerの `Install package from disk` から `Packages/com.darumappap.plateau-worldflow/package.json` を選択する方法も継続して利用できます。

### 初期セットアップとAI連携

1. Unity Editorの `Tools > PLATEAU WorldFlow > Check Dependencies` で依存関係を確認します。SDKが必要な場合は、取得元・固定Commit・変更内容を確認して承認し、Git経由で導入します。
2. AIから自然言語で計画する場合は、[自然言語による計画の手順](docs/guides/planning.md)に従って、別途Python環境とAgent Skillsを準備します。
3. 計画を採用したら、[Importと証跡確認の手順](docs/guides/import.md)に従ってUnity EditorでImportを承認・実行します。

WorldFlowのGit URLインストールと、PLATEAU SDKの承認付き導入は**別の操作**です。SDKのGit依存にはGit LFSも必要です。WorldFlowの `package.json` からSDKを無承認で追加することはありません。

**安定版のGit Tag / GitHub Releaseはまだ発行していません。** 上記URLは `main` の開発版を取得します。Git URLからのUnity Editor実機インストールは現時点で未検証のため、対応環境は[互換性・制約](docs/compatibility.md)も確認してください。

### 利用ガイド

- [Unity Packageのセットアップ](Packages/com.darumappap.plateau-worldflow/README.md)
- [AIによる地点・範囲・品質の選択](docs/guides/planning.md)
- [CityGML Importと保存失敗後の復旧](docs/guides/import.md)

## 制約・注意事項

- **Unity Editor向け**です。Runtimeでの都市データストリーミング、MP4録画、PLATEAU Toolkitsとの連携は現時点で提供していません。
- 範囲プレビューは**メッシュの概略図**であり、正確な地図やGIS上の位置の代用ではありません。PNGはProject内のファイルとして確認します。AIチャットへの自動画像配送は保証しません。
- Importは新規Sceneを使用し、既存Scene・Assetの無断上書き、自動削除、失敗後の無条件再実行は行いません。
- Importの途中で保存に失敗した場合、生成物を保持して手動照合が必要になる場合があります。保存済みの証跡と中断状況を確認してから再試行してください。
- 初期の実機検証範囲外のUnity版では、Scene保存に使うUnity内部コールバックの互換性は確認できていません。すべてのOS・Unity版での動作は保証しません。
- 現行Importは固定CommitのSDKをGit直接依存として確認します。tarball等のローカルSDK導入ではImportできません。

## ライセンス・権利

WorldFlowの自作コードは [MIT License](LICENSE) です。これは国土交通省およびProject PLATEAUの公式製品ではなく、公式サポート対象でもありません。

PLATEAU SDK、都市モデル、第三者コード・画像などは同梱していません。それぞれの配布元の利用条件を確認してください。

[第三者表示](THIRD_PARTY_NOTICES.md) · [データ出典と利用条件](docs/legal/DATA_ATTRIBUTION.md)
