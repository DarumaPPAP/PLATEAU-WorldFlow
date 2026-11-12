# 検証方針

| 区分 | 内容 | 証跡 |
| --- | --- | --- |
| Static CI | JSON Schema、Fixture、Skill frontmatter、相対リンク、Package・権利、Workflow設定、Tag照合 | コマンド、SHA、終了コード、Actionsのjob URL |
| Unity Local | SDKなしCompile、C# JSON相互運用、EditMode、Fake UPM、ClockとProject照合、Reload・並行制御 | Unity/OS/Test Framework版、コマンド、Editorログ、テスト結果 |
| 実操作E2E | 検証用Projectで初回診断、保留／拒否、明示承認、UPM導入・再診断 | Projectの匿名識別、SDKの取得元・版、前後のmanifest/lock差分、Job ID |

## 静的検証の実行

Python 3.12とGitを使用します。CIと同じ依存が揃った仮想環境で、Repository rootから実行します。追跡ファイルの検査対象はGit indexです。新規ファイルも検査する場合は作業ブランチでステージしてください。

```sh
python3 -m pip install --only-binary=:all: --no-deps -r tools/requirements-static.txt
python3 tools/validate_contracts.py
python3 tools/validate_repository.py
python3 tools/validate_package.py
python3 -m unittest discover -s tests -v
python3 tools/validate_package.py --tag v0.1.0
```

最後のコマンドは予定Tag名のdry-runです。Release検証はTagイベントでも同じ照合を行います。版を変えるPRでは新しい予定Tagで実行します。TagやReleaseは検証コマンドから作成されません。

公開ファイルの検査では代表的な秘密文字列の形式、大容量ファイル、禁止パスを検出します。公開前には差分と履歴を別途確認します。

## 結果の記録

- `PASS` は実行し成功を確認したものです。
- `FAIL` は実行したが期待結果を満たさなかったものです。失敗条件と再実行結果を残します。
- `BLOCKED` は実行の前提を満たせないものです。権限、承認、環境など不足した前提を残します。
- `NOT_RUN` は実行していないものです。対象外や後続PRの予定を記載します。
- 静的CIの成功からUnityの互換性を推定しません。実操作は承認済みの検証Projectで行います。
- 公開ログから認証情報、絶対パス、私的なURL、第三者データを除きます。

## GitHub Actions

- `Static validation`、`Package validation`、`Release version validation` を固定job名にします。
- いずれもGitHub-hosted runner、`contents: read`、checkout認証情報の永続化なしで動かします。外部PRでUnityライセンスや秘密を使用しません。
- 初回成功後、RulesetのRequired status checksへ登録するかは利用者が判断します。Actions許可リストやfork実行承認などの設定は実際の管理画面または実行結果から確認します。
