# Setup JSON契約

[システム設計仕様書](../architecture/system-spec.md) §2.8のJSON表現です。すべてDraft 2020-12、UTF-8、`schemaVersion: 1`です。未知プロパティは全階層で拒否します。名前の重複、非JSONの数値、整数フィールドへの小数・指数表記、対にならないUnicodeサロゲートも受け付けません。SchemaのIDは識別子として使用し、検証時にネットワークから取得しません。

| 契約 | 用途 | Schema |
| --- | --- | --- |
| Inventory | 読み取り専用の依存診断 | [dependency-inventory](dependency-inventory.schema.json) |
| Install Plan | 追加候補と承認対象の固定 | [dependency-install-plan](dependency-install-plan.schema.json) |
| Install Result | 終端状態と安全化済み証跡の参照 | [dependency-install-result](dependency-install-result.schema.json) |

## 共通表現

- 時刻はUTCの `YYYY-MM-DDTHH:mm:ssZ` に統一します。
- `projectFingerprint` は実Projectとの照合に使う小文字64桁の不透明な識別子です。絶対パスを公開せず、Project移動・別Project・複製で既存承認を再利用できない算出と永続化をPR-02で検証します。
- 診断は `code`、`location`、`retryable` のみ出力します。生のUPMエラーや端末パスは安全化してからProject-localのログに記録します。
- Planの説明・警告は管理された文言を使い、未処理の外部入力、トークン、認証情報、ユーザー固有パスを埋め込みません。

## Inventory

- `READY`、`MISSING`、`INCOMPATIBLE`、`BLOCKED`、`UNKNOWN` を区別します。
- `packageList: OFFLINE_CACHE_MAY_BE_STALE` は `Client.List` のoffline結果を意味します。manifest/lockと照合して裏付けが取れない場合は `UNKNOWN` とします。各記録の `evidence` に判断に使った情報源を列挙します。
- `source` は導入の種別です。公開可能な導入元を `sourceUri`、Gitの要求Tag／Refを `sourceRef`、実解決Commitを `resolvedCommit` に記録し、要求Version・実解決Versionと区別します。
- 不明な値や公開できない導入元・Refには `null` を使います。URLの認証情報、クエリ、ユーザー固有パスは出力しません。ToolはPackageと区別して `tools` に格納します。

## Install Plan

- `sourceUri` は認証情報やクエリのないHTTPSのGit識別子です。末尾の `#` に固定TagまたはCommit SHAを記録し、`versionOrCommit` と一致させます。追加対象は管理されたPackage ID・URL・固定Ref・期待するPackage Version・ライセンスURLの組で照合します。承認画面には固定Refと `expectedVersion` の両方を表示します。
- `baselineManifestHash` と `baselineLockHash` は計画作成時のファイルの生バイトに対するSHA-256です。lock未作成時の値は `null` です。実行直前にファイルの有無も照合します。
- `warnings` に再コンパイルや推移的依存の変更可能性を示します。公式tarballの手動導入案内は、この自動追加Planと独立した案内です。
- 正本にある承認状態はEditor内でPlanのID・Revision・Hash・Projectに紐づけて保持します。公開JSONに承認状態や承認トークンを含めず、`approved` フィールドも拒否します。
- 有効な時間範囲は `createdAt <= now < expiresAt` です。Planの更新やProject・manifest/lockの変化により既存承認を失効させます。

### Plan Hash

1. 形状を検証したPlanのルートから `planHash` だけを除きます。
2. 全オブジェクトのキーを序数の昇順に並べます。契約のキーはASCIIです。配列は宣言順を保持します。
3. 区切りの空白・BOM・末尾改行を付けないUTF-8のJSONにします。整数は10進で記述します。文字列中の引用符とバックスラッシュだけをJSONエスケープし、許可された非ASCII文字はUTF-8のまま出力します。Unicode正規化は行いません。制御文字は文字列として受け付けません。
4. SHA-256を計算し、小文字の16進64桁で表します。

[正常Plan](../../tests/fixtures/dependency-install-plan/valid.json) と [正規化済みバイト列](../../tests/fixtures/dependency-install-plan/golden-canonical.txt) はPythonとC#で共有します。PR-02のEditMode Testでは生成バイト列とHashを両方比較してください。

## Install Result

- Jobの終端状態は `COMPLETED`、`PARTIAL`、`FAILED`、`CANCELLED` です。実行中やReload復旧中の状態はEditor内に保存します。
- Package単位の結果は `ADDED`、`UNCHANGED`、`FAILED`、`NOT_ATTEMPTED` で記録します。Job成功後もInventoryの `READY` はCompileとCapabilityの確認まで保留します。
- `evidencePaths` は `Library/PLATEAUWorldFlow/Setup/<jobId>/<file>.json` 形式のProject相対パスです。実Projectの所定ディレクトリ内に収まること、`jobId` が一致すること、ファイルの中身が安全化済みであることを共有前に確認します。

## 検証境界

| 検証 | PR-01の静的検証 | PR-02の実行前検証 |
| --- | --- | --- |
| 必須項目、型、enum、未知キー、URL・パスの形 | Schemaと陽性・陰性Fixture | 実際のC#出力で互換性確認 |
| Plan Hash | Pythonで再計算しGolden Fixture照合 | C#で同じバイト列とHashを確認 |
| 期限、Project、導入元 | 固定した匿名のテスト入力で境界を再現 | 実時刻、実Project、実Allowlistとの照合 |
| Manifest/Lock、既存版、重複Package、Evidenceの到達先 | 文書化 | Unity側の実データで照合 |
| ユーザー承認・実UPM変更 | `approved` の公開JSON混入を拒否 | Editor内の証跡と結合し、承認なしの変更が0件と確認 |

[Fixture index](../../tests/fixtures/cases.json) はケースごとの期待結果と固定時刻を定義します。静的検証を通過したJSONは形状と該当するHashが正しい状態です。実Projectへの変更にはEditor側の検証を通す必要があります。
