# 動作環境・互換性

| 対象 | 実機で確認した構成 |
| --- | --- |
| Unity Editor | Windows / `6000.3.12f1` |
| PLATEAU SDK for Unity | `4.3.0`（Git導入、固定Commit `a9830e9a86e8c5b28486ab0e318714ca4a20364c`） |
| UnityMCP | `10.3.0` |
| Unity CLI | `1.0.0-beta.17` / Pipeline `1.0.0` |
| CityGML | 許可したLocal / 公式RemoteでImport、Scene再読込、証跡照合 |

[実装PR #12](https://github.com/DarumaPPAP/PLATEAU-WorldFlow-Archive/pull/12)の実機検証報告に基づきます。Packageの初期Unity基準は`6000.3.10f1`以上ですが、すべてのEditor版での動作保証ではありません。

## 未確認・提供範囲外

- UnityMCPのみ、Unity CLIのみの**片側Adapter構成**は個別のCompile・実操作E2Eを未実施です。
- Scene初回保存にはUnity内部のScene Templateコールバックに対する一時制御を利用しています。**他Unity版での互換性は未確認**です。
- 実SDK処理中のDomain Reload単独ケースは未検証です。失敗時は[手動復旧](guides/import.md#保存失敗中断した場合)を参照してください。
- MP4録画、PLATEAU Toolkits固有連携、Runtime Streamingは提供していません。
- CityGML ImportはSDKの**Git取得・直接依存・固定Commit一致**を実行前に要求します。tarball（`.tgz`）によるSDK導入はこの条件を満たさないため、現行実装ではImportできません。診断では非互換として扱われます。
- 安定版のGit Tag / GitHub Releaseは発行していません。

既知の安全制約は[Import契約](contracts/import.md)、利用方法は[ガイド](README.md)を参照してください。
