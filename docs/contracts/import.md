# CityGML Import契約と実行境界

本書はCityGML Importの**データ契約・承認条件・Jobの状態遷移**をまとめた技術リファレンスです。フィールド形式はJSON Schema、実行時の判定はソースコードを正本とします。利用者向けの操作は[Importガイド](../guides/import.md)を参照してください。

## 設計上の選択と安全制約

| Decision | 採用した動作 | 禁止・制約 |
| --- | --- | --- |
| D1：配置先 | WorldFlow所有の新規Additive Sceneを作り、新規`.unity` Assetへ保存 | 既存Sceneへの無承認混入・上書き |
| D2：データソース | 明示許可したLocal、またはID/版/URL対応を実証した公式Remote | 任意URL、曖昧なDataset IDの流用 |
| D3：保存 | Sceneと必要な生成Assetを検証・保存 | FBX/Prefabへの無承認変換 |
| D4：実行承認 | 対話可能なUnity Editor内で利用者がPlanを確認して明示承認 | チャットのYes、JSONのapproved、Batchによる代用 |
| D5：途中失敗 | 残存物を保持し、Jobの実体を照合してから手動で復旧判断 | 自動削除・無条件の全件再試行 |

## 入力とAdapter

- 採用済みPlanningResult（`stage=PLAN_READY`かつ`status=READY`）、`planDigest`、地点/範囲/品質の明示的な利用者Decisionを前提とする。**計画採用はImportの承認Tokenではない**。
- UnityMCPの入口は`worldflow_import`、公式Unity CLIは`worldflow import`。両方とも**JSON文字列1件**を共通Coordinatorへ渡す。
- Envelopeは`schemaVersion: 1`、`requestId`（UUID）、`projectFingerprint`（SHA-256）、`operation`、`input`の5フィールド。Project不一致・改ざん・期限切れは副作用前に拒否する。
- `import.prepare`の`input`には`planningSessionId`、`planRevision`、`planningResult`（採用済みPlanningResultのJSON**文字列**）、`coordinateZoneId`（1〜19）、`featureSettings`を渡す。Import Planの有効期間は現行実装で5分。期限切れやEditor再起動後はPrepareからやり直す。
- Local入力はEditorプロセスに設定した`WORLDFLOW_LOCAL_DATASET_ROOT`、`WORLDFLOW_LOCAL_DATASET_ID`、`WORLDFLOW_LOCAL_DATASET_VERSION`との一致を検査する。Rootは絶対パスとし、Project外や非許可ファイルを無条件で読み込ませない。
- Remoteは公式配布索引とSDKが選択したDataset/URL/版/メッシュ/地物LODを照合する。取得時は承認時に固定した内容を再検証する。不明な場合は`SOURCE_MAPPING_UNVERIFIED`等として停止する。
- SDKは`4.3.0`のGit直接依存と固定Commit一致を必要とする。`.tgz`等のローカル導入は現在のImport実行条件を満たさない。

## 操作と状態

| 操作 | 意味 | 副作用の境界 |
| --- | --- | --- |
| `import.prepare` | 採用Plan・GML・設定・出力先を再照合してImport Plan生成 | Read-only |
| `import.inspect` | `importPlanId` + `planHash`で現行Plan再検査 | Read-only |
| `import.request_approval` | 同じPlanをUnity Editorの確認画面に提示 | Editor利用者の操作が必要 |
| `import.start` | 承認済みPlanを消費しJob予約、次のEditor更新で実行開始 | Scene/Asset/取得を変更し得る |
| `import.status` | `jobId` + `planHash`で状態・進捗照会 | Read-only |
| `import.cancel` | 同Jobのキャンセル要求 | 停止を確認するまで最終状態を保証しない |
| `import.verify` | 結果・Scene/Asset・PNG Hashを照合 | Read-only |
| `import.artifacts` | 検証済み証跡のProject相対パスを返す | Read-only |

- `import.inspect`、`import.request_approval`、`import.start`は`input = {importPlanId, planHash}`、Job操作は`input = {jobId, planHash}`。任意の`approved`フィールドを付けても承認にはならない。
- Import前：`PREPARED → AWAITING_APPROVAL → APPROVED`。Jobは`RESERVED → RUNNING → VERIFYING`を経て実体照合で`COMPLETED` / `PARTIAL` / `FAILED` / `NEEDS_RECONCILIATION`へ遷移する。キャンセル要求は`CANCELLING`であり、即`CANCELLED`確定ではない。
- 1 ProjectでアクティブなImportは最大1件。同じScopeの要求を再送しても既存Jobを返し、重複Importしない。`PARTIAL`や照合待ちは自動再実行しない。
- Scene保存失敗で実体が不明なら`NEEDS_RECONCILIATION`を維持。Editorで残存物ありを確定すると`PARTIAL`、残存物なしを確定すると`FAILED`。帰属不明なら確定しない。`FAILED`から同Scope再試行には別のEditor確認と新Plan/承認が必要。
- SDKのTaskが正常終了しただけではImport成功としない。GML別結果、Scene帰属、保存後再読込、Asset参照、必要に応じPNG/ManifestのHashを照合する。

## 生成物と永続化

- 新規Scene：`Assets/PLATEAUWorldFlow/Imports/<importPlanId>/City.unity`（計画ごとに新規、既存ファイル上書き禁止）。
- Import JobとEvidence：`Library/PLATEAUWorldFlow/Import/jobs/<jobId>/`。Plan、Journal、GML別結果、Result、Evidence、PNG/Manifestを必要に応じて保持する。
- Schemaは[Import Plan](import-plan.schema.json)、[Import Job](import-job.schema.json)、[Import Result](import-result.schema.json)、[Import Evidence](import-evidence.schema.json)。計画の[計画契約](planning.md)とは別契約。
- 実際のエントリ・分岐は`Packages/com.darumappap.plateau-worldflow/Editor/Entry/ImportSession.cs`、CLI/MCP Adapter、`Core/Journal.cs`を参照。実装変更時は[検証ポリシー](../../AGENTS.md)に従う。

## 既知の互換性範囲

Sceneの初回保存はUnity 6000.3.12f1の内部Scene Templateコールバックに対する同期的な一時制御を利用するため、別Unity版での動作は未確認。未保存作業Sceneが残ると次のAdditive Scene作成が停止し得る。対応と復旧は[利用手順](../guides/import.md)、実測の範囲は[検証記録](../compatibility.md)を参照。
