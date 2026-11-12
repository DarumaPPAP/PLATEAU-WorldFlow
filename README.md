# PLATEAU-WorldFlow

PLATEAUの3D都市モデルをUnity Editorへ取り込むため、場所の調査、範囲・品質の計画、承認付きImport、結果の検証をAIチャットから支援するOSSです。

**Phase 0 / Repository Foundation**。現在は設計、依存Setup契約、静的検証、UPMのメタデータを整備しています。Editor機能は未実装で、Unityへの導入・動作は未検証です。初期検証基準は Unity `6000.3.10f1` 以上です。

## 開発

- [システム設計仕様書](docs/architecture/system-spec.md)
- [Phase 0実装仕様](docs/architecture/phase0-implementation.md)
- [開発ルール](AGENTS.md)
- [Setup JSON契約](docs/contracts/README.md)
- [静的CIとUnity検証](docs/ai/verification-policy.md)
- [UPM Package](Packages/com.darumappap.plateau-worldflow/README.md)

Package IDは `com.darumappap.plateau-worldflow` です。利用者向けのGit URL導入は動作を検証した安定Tagの発行後に案内します。Phase 0では第三者SDKやツールをPackageから自動導入しません。初回Setupの依存追加には対象Projectと固定Planへのユーザー承認が必要です。

## ライセンス

本プロジェクトは独立した非公式OSSです。国土交通省およびProject PLATEAUによる開発、承認、公式サポートの対象外です。自作コードは[MIT License](LICENSE)です。第三者SDK、都市データ、ロゴおよびサンプル資産は同梱していません。データと資産には提供元の利用条件が適用されます。[第三者表示](THIRD_PARTY_NOTICES.md) · [データ出典](docs/legal/DATA_ATTRIBUTION.md)
