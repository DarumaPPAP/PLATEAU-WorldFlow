# データ出典と加工履歴

Datasetごとに配布元で利用条件を確認します。出典・権利者・適用条件・加工を記録し、確認できない項目には `unverified` を使用します。再配布の条件が確認できるまで公開PackageやGit履歴へ含めません。

## ATTRIBUTION.mdの記録項目

| 項目 | 記録内容 |
| --- | --- |
| `datasetId`、`datasetVersion` | 配布元の識別子と版 |
| `publisher`、`sourceUrl` | 権利者・提供者、取得先 |
| `licenseId`、`licenseUrl` | Datasetに明記された条件と参照先 |
| `retrievedAt` | 取得時刻 |
| `modifications` | 切り出し、座標変換、属性変更など実施した加工 |
| 出典表示 | Datasetが指定する文言と追加条件 |

参照先は [PLATEAUサイトポリシー](https://www.mlit.go.jp/plateau/site-policy/) と対象Datasetの配布ページです。データ本体、地図タイル、画像、加工結果の扱いは各配布条件で確認します。
