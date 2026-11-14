# Phase 2 — 地点・PLATEAU調査ソースの事実と採用判断

- 調査日: 2026-11-14
- 区分: 公式文書で確認できたFact / Phase2の採用案 / 未検証E2Eを区別する。
- これは調査結果であり、PLATEAU MCPをWorldFlow/Unityへインストールまたは実接続した証跡ではない。

## 1. 正本候補の比較

| 用途 | ソース | 確認したFact | Phase2での扱い |
| --- | --- | --- | --- |
| PLATEAU自治体・Dataset候補 | [公式PLATEAU MCP](https://docs.plateauview.mlit.go.jp/mcp/overview/) | HTTP MCPで地域検索とDataset検索等を提供 | **任意の調査経路**。試験提供なので必須依存にしない |
| PLATEAU Dataset一覧 | [公式Open Data](https://www.mlit.go.jp/plateau/open-data/) | 自治体とデータへの公開導線がある | 配布元・年度の照合、サイトが示す一覧と実ファイル索引を区別 |
| 公式配信のメタデータ | [PLATEAU Dataset API](https://docs.plateauview.mlit.go.jp/api/rest/operations/datacatalogplateau-datasets/) | `GET /datacatalog/plateau-datasets` のJSONデータを提供 | MCP不達時の検証候補。URLやAPI形状を固定実装する前にPoC |
| 開発エージェント向け知識 | [公式Agent Skills](https://docs.plateauview.mlit.go.jp/skills/overview/) | 静的なAPI利用ノウハウ、MIT License | 参照・比較対象。既存6 Skillsを無検証で置換しない |
| Unityで実際に利用できる地域メッシュ | [WorldFlow Phase1](../contracts/phase1-readonly.md) | `datasets.list` / `coverage.inspect` が公開済み | **プロジェクト固有の利用可否の正本** |
| 作品舞台・POIの位置 | 作品公式/自治体/施設公式等 | 出典に依存、統一APIは未確定 | Web調査＋複数候補検証。存在と作品との関連を分離 |

## 2. 公式MCPの確認済み操作

[公式MCP Server文書](https://docs.plateauview.mlit.go.jp/mcp/overview/)では `plateau_search_areas`（`search_text`、`area_types` 等）、`plateau_get_area`（`code`）、`plateau_search_datasets`（`area_codes`、`year`、`dataset_types` 等）、`plateau_get_dataset`（`id`）、`plateau_get_citygml_files`（`condition`、`feature_types`）等を公開。CityGML検索の`condition`は `m:メッシュコード`、`s:空間ID`、`r:west,south,east,north`形式。

**注意**: PLATEAU側MCPとUnity操作用`worldflow_readonly`は別物。前者でUnity Editorの状態やWorldFlowの`projectFingerprint`を検査できると仮定しない。MCP結果が示すDataset IDの同一性/ソース/年度をPhase1に渡す前に確認する。

公式文書によれば、MCPは実験的運用で **SLAなし・API形状変更あり・本番非推奨**。検索最大100件の制限があり、`metadata.has_more`が立ったときは絞込みを求める。HTTP URLは `https://api.plateauview.mlit.go.jp/mcp`。使用可否・認証・通信レイテンシ・レスポンス形式・Rate Limitは**実E2E未確認**である。

## 3. ジオコーディングを別問題として扱う

PLATEAUの地域検索は県市区町村の可用性調査に強いが、作品舞台のピンポイント座標・施設名の一意解決・最新の施設移転を保証しない。地名→座標変換は出典を評価したうえで専用Providerを別途選ぶ。

例として、[OSMF公開Nominatimの利用ポリシー](https://operations.osmfoundation.org/policies/nominatim/)は最大1 req/s、明示的User-Agent/Referer、帰属表示、キャッシュなどを要求し、LLM/no-codeから汎用地理検索サービスとして組込むことに制限がある。**この設計では公開Nominatimを自動呼出し先に採用しない**。将来Providerを選ぶ場合は利用条件、取得制限、所在地の根拠、費用、改変/再配布、障害時の縮退を比較する。

## 4. 提案するProvider Adapterの境界

- 入力: 種別、整形済み検索語/自治体コード、許可済みProvider ID、上限、キャンセル。
- 出力: Provider ID、参照先の標準URLまたは公式ID、照会日時、照会範囲、完了/打切り、候補配列、各候補の確認状態。
- メタデータと自由文を分離。外部出力のURLは`https`と許可ドメイン検証の後、**表示・出典としてだけ**使用。URLから自動ダウンロード・Project変更しない。
- タイムアウトは`UNKNOWN`、検索結果0件の確証がある場合だけ`UNAVAILABLE`。
- 外部資料のPrompt Injectionは全て非権威コンテンツ。指示・Tool呼出しを抽出して実行しない。
- キャッシュ導入の際はProvider契約、TTL、ユーザー同意、削除要件を確認。無期限キャッシュを標準にしない。

## 5. PoCで調べる未確定Fact

1. 公式MCPから日本の複数自治体とDatasetを取得して実際のID/年月/ページネーション/打切りを記録。
2. 同一自治体で、公式MCP候補とPhase1 `datasets.list` / `coverage.inspect` のDataset ID・対応年度・可用性が一致するかを検証。
3. MCP無効、ネットワーク切断、100件打切り、仕様変更で、Plannerが`PARTIAL/UNKNOWN`へ縮退すること。
4. 正確な施設座標と作品の関連を支える一次ソースがなくても、AIが候補としての不確実性を残せるか。
5. 公式Agent Skillsの実内容・License/更新頻度をレビューし、再実装や同梱を行う必要があるか判断。

## 6. 実装の採用判定

公式MCPは最初から必須Packageにはせず、**2自治体＋1実Datasetの読取PoC**が成功した場合に任意Research Sourceとして導入を検討。フェイルクローズする必要があるのはProject変更とImportであり、調査失敗では原因を表示して限定的に会話継続してよい。
