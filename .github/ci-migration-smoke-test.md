# GitHub移行後のCIスモークテスト

このファイルは、新リポジトリで Pull Request の CI とブランチ保護が機能するか確認するための一時的な検証用ファイルです。

## 確認対象

- `Static validation`（Validate）
- `Package validation`（Package check）
- `Release version validation`（Release check）
- `protect-main` に設定された必須チェックとの連携

本PRは検証専用です。**Mergeせず、確認後にCloseしてブランチを削除します。**
Unity実装、UPM Package、既存ドキュメントの変更はありません。
