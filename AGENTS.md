# PLATEAU-WorldFlow 開発ガイド

## 正本とPhase境界

- 製品方針は[システム設計仕様書](docs/architecture/system-spec.md)を正本とする。
- 各Phaseの範囲は正本の第9章、検証結果の扱いは[検証方針](docs/ai/verification-policy.md)に従う。
- Phase 1の実装に入る際は[Read-only PoC仕様](docs/architecture/phase1-readonly-poc.md)、[Read-only契約](docs/contracts/phase1-readonly.md)、[API根拠](docs/compatibility/phase1-api-evidence.md)、[Codex引き継ぎ](docs/ai/phase1-codex-handoff.md)を読む。
- 現行のSetup JSONは[契約と実行前条件](docs/contracts/README.md)を参照する。
- 作業開始前に対象Phase、実装Baseline、対象Project、未解決の設計判断を確認する。

## 目的

- 場所を調査し、地点、範囲、品質、実行をユーザーが順に判断できるようにする。
- 実行前に対象と影響を示し、実行後は構造化データと証跡で確認する。
- 調査済みの事実、推定、ユーザーが決める項目を区別する。
- AIチャットを主UIとし、常設のSetup Windowは要件にしない。

## 現在の実装境界

- Phase 0（Repository基盤、依存Setup/承認Gate、Editor Test）はPR #1・#2で実装済み。既存の動作と証跡を壊さない。
- Phase 1はRead-only Core、PLATEAU Dataset照会、Scene非変更の範囲PNG、任意MCP/公式CLI Adapter。仕様がMergeされても実装完了と扱わない。
- 実装順序と最小テストは[Phase 1 Codex handoff](docs/ai/phase1-codex-handoff.md)に従う。最初はCore/ContractだけのPRとし、後続を一度に実装しない。
- CityGML Import、Sceneへのモデル配置、動画、Toolkits、Runtime StreamingはPhase 1で禁止。

## 固定識別子

| 項目 | 値 |
| --- | --- |
| Repository | `DarumaPPAP/PLATEAU-WorldFlow` |
| Package ID | `com.darumappap.plateau-worldflow` |
| Package root | `Packages/com.darumappap.plateau-worldflow/` |
| Display name | `PLATEAU WorldFlow` |
| C# namespace | `DarumaPPAP.PlateauWorldFlow` |
| 初期検証基準 | Unity `6000.3.10f1` 以上 |

## 設計と実装

- Core、Unity Editor Setup、将来のAdapterは責務と依存方向を分ける。
- 既存のクラスと機能で解決できる場合は再利用し、汎用ManagerやSingletonを目的化しない。
- SDK、MCP、CLIのAPI名やVersion別の動作は公式資料と対象環境で確認する。
- 仕様内の設計名を実装済みの外部APIとして案内しない。
- Contractのrequired項目、enum、版、既存Fixtureへの影響をPRで示す。
- 意味論や上流Decisionが変わる場合は根拠と互換性を示し、利用者の承認を得る。

## C# XMLドキュメントコメント規約

- 今後のすべてのPRで、新規C#クラス、構造体、インターフェース、列挙型の宣言直前に `<summary>` を記載する。型の責務と目的を日本語で簡潔に説明し、型名を言い換えるだけの説明は記載しない。
- 公開API、他の型から利用する重要なメソッド、名前だけでは振る舞いが分かりにくいメソッドにも `<summary>` を記載する。
- 引数の意味を説明する必要がある場合は `<param>`、戻り値の意味を説明する必要がある場合は `<returns>` を記載する。型宣言、コンストラクタ、`void` メソッドには `<returns>` を付けない。
- 空のXMLドキュメントタグや実装と一致しない説明は禁止する。既存コードの修正時は変更対象の型に適用し、無関係な既存コードへの一括追加は求めない。
- CodexはC#の実装・修正・レビューで、この規約への準拠を確認する。

## Project変更

- 読み取り診断と計画の作成はProjectを変更せずに行う。Phase 1の明示的なPNG Evidence保存は `Library/PLATEAUWorldFlow/Preview/` に限定し、Assets/Scene/Packageは不変とする。
- Package追加・更新・削除、Registry変更、Import、AssetやSceneの変更・上書きは事前承認が必要。
- Planの内容、Project、Revision、Source、TagまたはSHA、影響を固定してから承認を求める。
- Editor内の実ユーザー承認を信頼境界に記録する。JSONの `approved` は承認証跡として使わない。
- Batch Modeなど非対話環境ではPackageを変更しない。
- 承認済みPlanと実Project、期限、manifest/lock、導入元を実行直前に再照合する。
- Domain Reload後はJobと実Projectを照合する。結果不明のまま追加を再発行しない。
- 既存SDKの置換・ダウングレードや自動Rollback、OSへのGit LFS自動インストールを行わない。
- 外部Git依存をWorldFlowの `package.json` に直接記載しない。

## 静的検証とUnity検証

- 実行コマンド、環境、対象SHA、終了コード、証跡、未実行理由をPRに記録する。
- `PASS`、`FAIL`、`BLOCKED`、`NOT_RUN` を区別する。
- 静的CI、Unity Compile／EditMode、実操作E2Eは[検証方針](docs/ai/verification-policy.md)に従う。
- 実Projectの権限、接続、承認が得られない場合は該当する検証を `BLOCKED` とする。
- Package Managerの処理完了に加え、Version、Source、Compile、Capabilityを再検査してから `READY` を報告する。

## PRと公開

- 作業は `feature/*`、`fix/*`、`chore/*`、`release/*` のブランチで行う。
- `main` への直接Push、Auto Merge、エージェントによるMergeを禁止する。最終判断は利用者が行う。
- GitHub設定の変更、Tag作成、Release発行は明示承認を得る。
- Public PRのCIは読み取り権限のGitHub-hosted runnerで実行する。
- PRに秘密情報、権限付きrunner、Unityライセンスを渡さない。
- トークン、非公開URL、個人パス、生成Asset、Dataset、PNG、MP4はGit履歴や公開証跡に残さない。
- 第三者コード・資産の追加前に、[権利方針](docs/legal/LICENSE_POLICY.md)と[出典の規則](docs/legal/DATA_ATTRIBUTION.md)を確認する。
- 配布前にPackage ID、TagとVersion、ライセンス、配布物の中身を検証する。

## Skillsと引き継ぎ

- 計画と承認の入口は [world planner](.agents/skills/plateau-world-planner/SKILL.md) を参照する。
- [.agents/skills](.agents/skills) の各定義を作業時に読み、記載されたPhase境界と中止条件に従う。
- Skillの利用環境が自動読込に対応するかは環境ごとに確認する。
- EvidenceはProject-localに保存し、共有前に認証情報・絶対パス・非公開URLを除く。
- PRには[テンプレート](.github/PULL_REQUEST_TEMPLATE.md)を使い、変更点、検証結果、残る判断を記載する。

## 作業の順序

1. `git status` と分岐元SHAを確認する。
2. 該当する仕様、契約、Skillと権利条件を読む。
3. 変更する動作と対象ファイルを決める。
4. 陽性と陰性のFixtureを更新し、期待する失敗も検証する。
5. 静的CIを実行し、必要なUnity検証を実行する。
6. 差分に権限の拡張や無関係な生成物が含まれないか確認する。
7. PRを作成し、未検証事項と次工程を残す。

Phase全体の受入可否は、個別PRの静的CI結果とUnity実行証跡の両方から判断する。
