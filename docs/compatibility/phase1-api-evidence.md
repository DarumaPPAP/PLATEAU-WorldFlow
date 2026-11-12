# Phase 1 — API互換性の根拠と検証ゲート

- 調査日: 2026-11-12
- Phase 1対象: PLATEAU SDK 4.3.0（Commit `a9830e9a86e8c5b28486ab0e318714ca4a20364c`）、CoplayDev unity-mcp 10.3.0（Tag `v10.3.0`）、Unity CLI/Pipeline（Unity公式の実験的機能、導入環境のVersionは未確認）。
- **区別**: 以下の「ソース確認」は外部リポジトリ中のAPIの存在確認であり、このProjectでのリンク/Compile/E2E成功を意味しない。
- 実装仕様: [Phase 1 Read-only PoC](../architecture/phase1-readonly-poc.md)。

## 1. 確認した事実

| 外部システム | ソース・公式資料で確認した事実 | 注意 |
| --- | --- | --- |
| PLATEAU SDK v4.3.0 | `PLATEAU.Network.Client.Create` と `GetDatasetMetadataGroup()` でDatasetメタデータGroupを照会できるC# APIがある | ネットワーク/認証、取得数、実応答は未実行 |
| PLATEAU SDK v4.3.0 | `DatasetSource.Create(IDatasetSourceConfig)` はLocal/Remote設定を受け取り `Accessor` を返す | Native DLLの依存と解放が必要 |
| PLATEAU SDK v4.3.0 | `DatasetAccessor.GridCodes` / `GetAllGmlFiles` / `FilterByGridCodes` を公開 | Server時のPackage/LODについて未照会値は推測禁止 |
| PLATEAU SDK v4.3.0 | `GridCode.StringCode` / `Extent` / `IsValid` がある | 精度/座標系と計測コストは実検証 |
| PLATEAU SDK v4.3.0 | 標準 `AreaSelectorBehaviour` はSceneViewカメラ、GameObject、地図ローダーを利用 | Project Read-onlyの境界を超えるため流用しない |
| CoplayDev unity-mcp v10.3.0 | `McpForUnityToolAttribute`、Custom Tool、`HandleCommand(JObject)`が存在 | 実際のASM参照とToolディスカバリは検証が必要 |
| CoplayDev unity-mcp | `execute_custom_tool` / `run_tests` / `get_test_job` 等を公開 | Screenshot応答でPNGがAIに見えると決めつけない |
| Unity公式CLI + Pipeline | CLIは単体バイナリ、Editor制御には `com.unity.pipeline` が関係し、`[CliCommand]` が利用される | 実験的・Version別破壊的変更の可能性あり |
| Unity CLI docs | `com.unity.pipeline 0.9.0-exp.1` 以降でコマンド命名規約が変更 | 環境未確認でAPI名やCLI形式を決め打ちしない |
| Unity asmdef | Version Defines / Define ConstraintsでPackageの存在とVersionに応じたAssembly切替が可能 | 実際の参照先asmdef名とExpressionはUnityで検証 |

## 2. 一次情報への固定参照

### PR-Bの読取経路

- Unity側はSDK v4.3.0の `libplateau` を版条件付きで参照する。ローカルでは許可済みUDXの既存ファイル名を走査し、`GmlFile` と `GridCode` で地物種別と境界を取得する。本文を開かず、LODと測地系は未確認として返す。
- リモートでは [公開Datasetカタログ](https://docs.plateauview.mlit.go.jp/api/rest/operations/datacatalogplateau-datasets/) と [公式CityGMLファイル索引](https://docs.plateauview.mlit.go.jp/api/rest/operations/datacatalogcitygmlconditions/) をHTTPSで照会する。`plateau-public` のDataset IDは最新CityGML一覧の5桁自治体コード。索引の地物別LOD件数が正の場合だけ、そのLODの存在を記録する。SDKはメッシュの妥当性と境界の取得に使用する。
- Nativeクライアントの公開実装では一覧呼び出しの証明書検証が無効で、通信失敗時に空のベクターが返る場合がある。上記HTTPS経路では成功したHTTP応答だけを採用し、リダイレクト、応答サイズ超過、タイムアウト、応答構造の不一致を照会失敗として扱う。索引が返すCityGMLのURLは取得しない。

### PLATEAU SDK 4.3.0

- [PLATEAU SDK v4.3.0固定ツリー](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/tree/a9830e9a86e8c5b28486ab0e318714ca4a20364c)
- [DatasetSource.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/libplateau/CSharpPLATEAU/Dataset/DatasetSource.cs)
- [DatasetAccessor.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/libplateau/CSharpPLATEAU/Dataset/DatasetAccessor.cs)
- [GridCode.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/libplateau/CSharpPLATEAU/Dataset/GridCode.cs)
- [Network/Client.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/libplateau/CSharpPLATEAU/Network/Client.cs)
- [ServerDatasetFetchGUI.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/Editor/Window/Main/Tab/ImportGuiParts/ServerDatasetFetchGUI.cs)
- [AreaSelectorBehaviour.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/Runtime/CityImport/AreaSelector/AreaSelectorBehaviour.cs)
- [GridCodeList.cs](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/a9830e9a86e8c5b28486ab0e318714ca4a20364c/Runtime/CityImport/Config/GridCodeList.cs)
- [公式Import手引](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/blob/v4.3.0/Documentation~/manual/ImportCityModels.md)

### CoplayDev unity-mcp 10.3.0

- [v10.3.0ソース](https://github.com/CoplayDev/unity-mcp/tree/v10.3.0)
- [McpForUnityToolAttribute.cs](https://github.com/CoplayDev/unity-mcp/blob/v10.3.0/MCPForUnity/Editor/Tools/McpForUnityToolAttribute.cs)
- [Custom Tool公式Guide](https://coplaydev.github.io/unity-mcp/guides/custom-tools)
- [Tool Reference](https://coplaydev.github.io/unity-mcp/reference/tools)

### 公式Unity CLI / Pipeline

- [CLI/Pipeline役割比較](https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-cli-pipeline-package)
- [Pipelineの導入と接続](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package)
- [コマンド定義](https://docs.unity.com/en-us/unity-cli/command-name-conventions/define-command)
- [コマンド名の規約](https://docs.unity.com/en-us/unity-cli/command-name-conventions)
- [CLIでのコマンド実行](https://docs.unity.com/en-us/unity-cli/command-name-conventions/run-command-cli)
- [Unity asmdef Version Defines](https://docs.unity3d.com/ja/6000.0/Manual/assembly-definition-includes.html)

## 3. CodexがPR-A/B/Dで必ず埋める観測表

| 項目 | 記録するもの | 判定 |
| --- | --- | --- |
| Unity Editor/OS | Exact Version、OS、Package Manager実解決 | 未実行 |
| PLATEAU SDK | Package ID、Tag要求、Resolved SHA、Assembly名、Native DLLの動作 | 未実行 |
| Dataset実照会 | Source、取得件数、GridCode、実在するLOD、時刻、匿名化ログ | 未実行 |
| Native lifecycle | success/error/cancelでDisposeとハンドル保持を検査 | 未実行 |
| CoplayDev | SDK実Version、Custom Tool発見、入力JSON、出力JSON、画像返却可否 | 未実行 |
| Official CLI/Pipeline | `unity --version`、`unity pipeline list`、Pipeline実Version、Custom Command発見・結果 | 未実行 |
| No-dependency variants | SDKなし、MCPなし、CLI/Pipelineなし、双方あり等のCompile | 未実行 |
| Project不変 | Assets/Scene/manifest/lock/ProjectSettingsの前後証跡 | 未実行 |

## 4. 実装前に解くべき技術的不確実性

1. **SDK/External Assemblyの参照名**: ソースファイルがあることと、Unityのasmdefから安全に参照できることは異なる。実ProjectでAssembly名、Version Defines、SDKなしCompileを確認。
2. **Geo/LOD確度**: Dataset MetadataだけではLODやグリッドの可用性を証明できない。SDKのAccessor/Files/MaxLodに対し、I/Oコストと副作用を確認して限定照会。
3. **UnityMCP Custom Tool登録**: `McpForUnityTool` のasmdef名、Newtownsoft/JObject参照、Tool戻り値、client側の発見とread-only保証を対象タグで確認する。
4. **CLI/Pipelineの破壊的変更**: 実験的バージョンを必ず固定・証跡化し、存在しない `CliCommand` の引数や `CommandResult` APIを想像で使わない。
5. **画像配送**: PNGファイルを書けることと、AIクライアントが画像を認識できることは別。Capability DiscoveryとE2Eで `INLINE_IMAGE` / `ARTIFACT_ONLY` を区別。
6. **Read-onlyの範囲**: SDKの通信・内部Cacheが発生し得る。Scene/Assets/Packageファイルは不変、Evidence以外に意図しない変更があれば失敗と記録。

**上記未実行事項を先に「確認済み」に変更しない。** 実APIを確認できない場合はその経路をBLOCKEDで止め、他の検証済み経路を継続する。
