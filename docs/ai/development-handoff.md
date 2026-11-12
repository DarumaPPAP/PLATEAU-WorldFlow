# Codex Local 開発ハンドオフ

このファイルは開発フェーズ別の入口です。現在の実装対象は**Phase 1: Core & Read-only PoC**です。

## 現行（Phase 1）

- [AGENTS.md](../../AGENTS.md) — 不変の開発/承認/PR規約
- [システム設計仕様書](../architecture/system-spec.md) — 製品方針の正本
- [Phase 1実装仕様](../architecture/phase1-readonly-poc.md) — Scope、Assembly、Data/PNG、PR分割、Definition of Done
- [Phase 1 Read-only Contract](../contracts/phase1-readonly.md) — Json意味論とエラー/成果物
- [API根拠・未検証一覧](../compatibility/phase1-api-evidence.md) — 固定Tagの実在APIとUnity側の検証項目
- [Phase 1 Codex Goalと作業手順](phase1-codex-handoff.md) — **実装着手はこちら**

実装PRのBaselineは、仕様PRの手動Merge**後**に `git rev-parse origin/main` で取得してください。設計文書の固定Baselineと混同しないでください。

## Phase 0（完了記録）

- [PR #1](https://github.com/DarumaPPAP/PLATEAU-WorldFlow/pull/1)：Repository/Context/Setup契約/静的CI
- [PR #2](https://github.com/DarumaPPAP/PLATEAU-WorldFlow/pull/2)：SDK非依存Editor Setup、承認Gate、復旧、Unity検証
- Setupの既存SchemaとGolden/Testは維持。Phase 1で破壊的に変更しない。

Codexは新規PRを作成して検証結果と未実行を記録し、**Merge・Tag・Releaseは利用者に委ねてください**。
