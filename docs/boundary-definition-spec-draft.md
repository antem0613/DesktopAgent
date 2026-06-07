# 境界線定義 仕様書（ドラフト）

## 文書情報
- 文書名: DesktopAgent 境界線定義仕様書
- プロジェクト名: DesktopAgent
- バージョン: 0.4.8-draft
- 作成日: 2026-05-30
- 更新日: 2026-05-30
- 作成者: 
- レビュー担当: 
- 承認者: 
- ステータス: Draft

## 1. 目的
- UIプロセスを中央制御とし、Characterプロセスを1体1プロセスで分離する。
- 既存の単体制御集中構造を、管理責務と個体責務に分割する。
- STT、TTS、LLM、ウィンドウ追跡の境界を明確化し、機能拡張の土台を作る。

## 2. スコープ
### 2.1 対象範囲
- 対象機能:
  - キャラクター生成、破棄、再起動
  - キャラクターのデスクトップ/壁紙間遷移（ユーザー指示/自律判断）
  - 右クリックメニュー要求
  - 音声入力、音声出力、会話中継
  - 壁紙滞在中の呼びかけ応答
  - ウィンドウ追跡と着地判定
- 対象プロセス:
  - UIプロセス
  - Characterプロセス
- 対象シーン:
  - UI
  - Character
- 対象モジュール:
  - CharacterManager
  - CharacterCompanion（新規）
  - BackendManager
  - STTManager
  - TTSManager
  - LLMProvider

### 2.2 非対象範囲
- 壁紙プロセスの本実装（ただし境界仮定の定義は本書の対象）
- SDK公開用ドキュメント整備
- 課金モデル連携

### 2.3 リリース対象境界（v1 / vNext）
| 項目 | v1 | vNext |
|---|---|---|
| UI中央制御 + Character分離 | 対象 | - |
| ZeroMQ control/state/window/bubble | 対象 | - |
| Habitat遷移の本実装（desktop/wallpaper） | - | 対象 |
| wallpaper_control チャネル本運用 | - | 対象 |
| Character SDK（`.dac`） | 対象 | - |
| Wallpaper Level SDK（`.daw`） | 対象（定義のみ） | 対象（本運用） |
| 壁紙プロセス本実装 | - | 対象 |

## 3. 用語定義
| 用語 | 定義 |
|---|---|
| UIプロセス | 画面UI表示と中央制御を行うプロセス |
| Characterプロセス | キャラクター1体の表示と挙動を担当するプロセス |
| CharacterManager | UIシーンでキャラクター群を管理する総合管理コンポーネント |
| CharacterCompanion | Characterプロセスで1体分の挙動を制御するコンポーネント |
| IPC | プロセス間通信 |
| WindowSnapshot | UI側で収集したウィンドウ情報のスナップショット |
| BubbleCommand | Character側の吹き出し表示命令 |
| VoiceRequest | TTS再生や音声状態更新の命令 |
| Habitat | キャラクターの存在領域（desktop または wallpaper） |
| WallpaperPortal | 壁紙とデスクトップを出入りするための遷移点 |
| WallpaperAnchor | 壁紙上で座る/寝そべることが許可された固定ポイント |

## 4. 全体アーキテクチャ
### 4.1 構成図
- 図URLまたは添付先: TODO
- 補足: UIプロセスがオーケストレータとして Character プロセス群を制御する。

### 4.2 プロセス一覧
| プロセス名 | 主責務 | 保有データ | 外部依存 | 備考 |
|---|---|---|---|---|
| UI | 総合管理、IPCルーティング、音声制御、ウィンドウ追跡 | キャラ台帳、会話ルーティング状態、WindowSnapshot | FastAPI、Coeiroink、OS API | 常駐 |
| Character | 1体分の挙動制御、描画、吹き出し表示 | 個体状態、モデル、Animator、ローカル行動状態 | OS API、UI IPC | 複数起動 |
| Wallpaper | 背景描画レイヤ管理、壁紙演出の適用（将来） | レイヤ状態、描画プリセットID | OS API、UI IPC（将来） | 本実装は非対象、境界仮定のみ定義 |

### 4.3 境界の原則
- 単一責務の原則: 管理責務はUI、個体挙動はCharacter。
- 所有権の原則: 正本オーナーを必ず1つにする。
- 同期/非同期の原則: UIからCharacterへの指示は非同期前提。
- 互換性の原則: IPCメッセージはバージョン付きで後方互換を維持する。

## 5. 責務分担
### 5.1 UIプロセス責務
- 必須責務:
  - Characterプロセスの起動、監視、停止
  - STT、TTS、LLMの中央制御
  - キャラ台帳管理（characterId、pid、port、state）
  - WindowSnapshot収集と配布
- 任意責務:
  - 共通UI表示（入力欄、マイク状態、メニュー）
- 禁止責務:
  - 個体挙動ロジックの直接実行

### 5.2 Characterプロセス責務
- 必須責務:
  - モデルロードとAnimator初期化
  - 落下、着地、歩行、ドラッグ、スクロール
  - ユーザー指示または自律判断による Habitat 遷移要求の発行
  - 吹き出し表示
  - 個体状態の報告
  - 壁紙滞在中も呼びかけ応答に必要な音声対話状態を維持
- 任意責務:
  - 個体ごとの追加演出
- 禁止責務:
  - 他キャラ管理
  - STT、TTS、LLMの中央管理

### 5.3 共有責務
- 境界横断で許可される責務:
  - 状態通知
  - 画面操作要求
  - 音声再生命令
- 境界横断で禁止する責務:
  - 正本データの無断更新

### 5.4 Wallpaperプロセス境界仮定（将来実装）
- 必須責務（仮定）:
  - UI から受け取った壁紙レイヤ命令の適用
  - 壁紙描画状態（activePresetId, layerOpacity）の通知
  - `WallpaperPortal` と `WallpaperAnchor` に基づく配置制約の適用
- 禁止責務（仮定）:
  - STT、TTS、LLM の管理
  - Character への直接命令（UI を経由しない制御）
- 所有権（仮定）:
  - 描画プリセット定義の正本は UI が持つ
  - Wallpaper は適用結果のみを保持する
  - `WallpaperPortal` / `WallpaperAnchor` は SDK 作成時定義を正とし、実行中編集は不可とする

## 6. データ所有権
### 6.1 所有データ一覧
| データ名 | 正本オーナー | 参照可能者 | 更新権限 | 同期方式 |
|---|---|---|---|---|
| characterId | UI | UI/Character | UI | IPC配布 |
| CharacterState | Character | UI/Character | Character | イベント通知 |
| WindowSnapshot | UI | UI/Character | UI | 周期配信 |
| BubbleCommand | UI | UI/Character | UI | 要求送信 |
| VoiceRequest | UI | UI/Character | UI | 要求送信 |
| WallpaperTopology（Portal/Anchor） | SDK定義 | UI/Character/Wallpaper | SDK定義のみ | 起動時ロード |

### 6.2 一貫性要件
- 強整合が必要なデータ:
  - characterId と process 対応表
  - キャラの生死状態
- 遅延許容可能なデータ:
  - WindowSnapshot
  - 表示専用UI状態
- 最大許容遅延:
  - WindowSnapshot: 100msから250ms
  - BubbleCommand反映: 300ms以内

### 6.3 永続化方針（ハイブリッド）
- 基本方針:
  - 正本データは SQLite に保存する
  - 人が編集する設定入力は JSON を使用する
  - UIプロセスのみが永続データの書き込み権限を持つ
- 採用理由:
  - SQLite で整合性と復旧性を確保し、JSON で運用編集性を確保するため

#### 6.3.1 データ配置
| データ種別 | 保存先 | 正本 | 更新主体 | 用途 |
|---|---|---|---|---|
| CharacterDefinition | SQLite | SQLite | UI | キャラ定義（モデル、音声、エージェント） |
| CharacterRuntimeSession | SQLite | SQLite | UI | 起動中セッション（pid、port、heartbeat） |
| AppEditableSettings | JSON | JSON（入力源） | UI/運用者 | 人手編集が必要な設定（音声上書き、エージェント割当を含む） |
| RuntimeResolvedConfig | SQLite | SQLite | UI | 実行時に解決された設定スナップショット |

- 非永続データ方針:
  - キャラクターの位置、スケール、アニメーション状態は DB 正本としては保存しない
  - これらはプロセス生存期間中のメモリ状態として扱う

#### 6.3.1.1 設定入力境界（音声/エージェント）
- 音声設定:
  - SDK定義とアプリ設定の両方から設定可能とする
  - 競合時の優先順位は `アプリ設定 > SDK定義` とする
  - UI起動時に解決結果を `RuntimeResolvedConfig` に保存する
- エージェント割当:
  - アプリ設定からのみ設定可能とする
  - SDK定義によるエージェント指定は受け付けず、指定があっても無視して Warn ログを出す
  - 有効な割当が未設定の場合は起動を継続しつつ、対話機能を `agent-unassigned` の縮退状態で動作させる
- 壁紙トポロジ定義（Portal/Anchor）:
  - SDK定義からのみ読み込む
  - アプリ設定による上書きは不可とする

#### 6.3.2 同期ルール
- 起動時:
  1. UI が JSON を読み込む
  2. UI が JSON を検証し RuntimeResolvedConfig に反映する
  3. CharacterDefinition と差分があれば SQLite に適用する
- 実行時:
  - Character からの状態通知は UI メモリ上でのみ保持する
  - 位置/アニメーション情報は永続化しない
- 終了時:
  - RuntimeSession を close する

#### 6.3.3 復旧ルール
- UI 再起動時:
  - open 状態の RuntimeSession を異常終了として close する
  - CharacterDefinition から再起動計画を再生成する
- Character 異常終了時:
  - UI が RuntimeSession を更新し、再起動ポリシーに従って復旧する
- JSON 破損時:
  - 直近の RuntimeResolvedConfig（SQLite）で縮退起動し、警告ログを出す

#### 6.3.4 ID 方針
- characterId は UUIDv7 形式を採用する
- sessionId は起動単位で UUIDv7 を新規発行する

#### 6.3.5 SQLite スキーマ定義
- DBファイル名: `desktopagent_runtime.db`
- 保存先: `Application.persistentDataPath/Settings/`

| テーブル名 | 主キー | 主なカラム | 説明 |
|---|---|---|---|
| character_definitions | character_id | display_name, model_path, voice_profile, agent_profile_json, is_enabled, created_at, updated_at | キャラ定義の正本 |
| character_sessions | session_id | character_id, process_id, control_endpoint, state_endpoint, status, started_at, closed_at, close_reason, last_heartbeat_at | 起動中/履歴セッション |
| runtime_configs | config_key | config_value_json, source_hash, updated_at | JSON入力から解決した実行設定 |
| schema_migrations | version | applied_at, description | スキーマ適用履歴 |

- インデックス方針:
  - `character_sessions(character_id, status)`
  - `character_sessions(last_heartbeat_at)`

#### 6.3.6 トランザクション境界
- キャラ起動時:
  1. `character_sessions` に `status=starting` で INSERT
  2. 起動成功後に `status=running` + `process_id` 更新
  3. 上記は同一 requestId で冪等化する
- 終了時:
  1. `character_sessions` を `status=closed` へ更新
  2. `closed_at`, `close_reason` を記録
- UI再起動時の回復処理:
  - `status in (starting, running)` のセッションを異常終了として `closed` に更新してから再起動計画を組む

#### 6.3.7 SQLite マイグレーション手順
- 適用単位:
  - マイグレーションは 1 ファイル 1 バージョンで管理する
  - 命名規則は `V{major}_{minor}_{patch}__{description}.sql` とする
- 適用順序:
  1. UI 起動時に `schema_migrations` を読み込む
  2. 未適用 SQL をバージョン昇順で適用する
  3. 各 SQL 成功後に `schema_migrations` へ適用履歴を INSERT する
- トランザクション規約:
  - 各マイグレーション SQL は単独トランザクションで実行する
  - 失敗時は当該 SQL のみロールバックし、起動を停止してエラーを返す
- 互換性規約:
  - 破壊的変更は 2 段階で実施する
  - 第 1 段階で新カラム追加と二重書き込みを導入する
  - 第 2 段階で旧カラム参照を除去し、最終削除する
- バックアップ規約:
  - major 更新を含む適用前は DB ファイルを 1 世代バックアップする
  - 復旧時はバックアップ復元後に `schema_migrations` と整合チェックを行う

## 7. 通信仕様
### 7.1 通信方式
- 通信手段: ZeroMQ
- 想定パターン: ROUTER/DEALER（制御系）、PUB/SUB（配信系）
- 選定理由: 低レイテンシ、接続管理の容易さ、複数プロセス拡張への適合性
- フォールバック: 再接続、自動再送（アプリ側制御）、再同期要求

### 7.1.1 ソケット役割
| チャネル名 | UI側 | Character側 | 用途 | 備考 |
|---|---|---|---|---|
| control | ROUTER(bind) | DEALER(connect) | 生成、破棄、設定反映、ヘルス応答 | requestId 必須 |
| state | SUB(connect) | PUB(bind) | 個体状態通知 | characterId トピック配下 |
| window | PUB(bind) | SUB(connect) | WindowSnapshot 配信 | 最新のみ適用 |
| bubble | PUB(bind) | SUB(connect) | 吹き出し表示命令 | ttlMs で期限管理 |
| wallpaper_control（将来） | ROUTER(bind) | DEALER(connect) | 壁紙レイヤ命令、適用結果通知 | Wallpaper 追加時に有効化 |

### 7.1.2 エンドポイント方針
- transport: `tcp` + `ipc` 併用
- transport選択方針:
  - 開発環境: `tcp`（可観測性とデバッグ容易性を優先）
  - 本番環境: 原則 `ipc`（同一ホスト内性能を優先）
  - フォールバック: `ipc` 利用不可時は `tcp` へ自動切替
- host: `127.0.0.1`（tcp利用時）
- base port: `5600` から用途別に固定割当
  - control: `5601`
  - window: `5602`
  - bubble: `5603`
  - state: Characterごとに `5700 + characterIndex`
  - wallpaper_control: `5604`（vNextで有効化）
- ipcエンドポイント命名規則:
  - control: `ipc://desktopagent-control.sock`
  - window: `ipc://desktopagent-window.sock`
  - bubble: `ipc://desktopagent-bubble.sock`
  - state: `ipc://desktopagent-state-<characterId>.sock`
  - wallpaper_control: `ipc://desktopagent-wallpaper-control.sock`（vNextで有効化）
- 本番で衝突する場合は設定ファイルで上書き可能にする

### 7.1.3 トピック命名
- 形式: `desktopagent/<channel>/v1/<characterId>`
- 例:
  - `desktopagent/window/v1/char-001`
  - `desktopagent/bubble/v1/char-001`
  - `desktopagent/state/v1/char-001`

### 7.1.4 メッセージ封筒
- すべてのメッセージに共通ヘッダを持たせる
  - schemaVersion
  - messageType
  - requestId
  - traceId
  - timestampUnixMs
  - characterId（対象がある場合）
  - seq（順序管理が必要な場合）
- payload は messageType ごとに個別定義する

### 7.2 メッセージ契約
| メッセージ名 | 方向 | 必須項目 | 任意項目 | 返信要否 |
|---|---|---|---|---|
| SpawnCharacter | UI -> Character | characterId, modelPath, ipcPort | initialScale | 要 |
| DespawnCharacter | UI -> Character | characterId | reason | 要 |
| WindowSnapshotUpdate | UI -> Character | characterId, snapshotVersion, windows | focusedWindow | 否 |
| BubbleCommand | UI -> Character | characterId, text, bubbleType | ttlMs | 否 |
| VoiceRequest | UI -> Character | characterId, text, voiceProfile | emotion | 否 |
| CharacterStateReport | Character -> UI | characterId, state, timestamp | debugFields | 否 |
| HealthPing | 双方向 | processType, timestamp | seq | 要 |
| WallpaperLayerUpdate（将来） | UI -> Wallpaper | presetId, layerOpacity, transitionMs | zOrderHint | 要 |
| WallpaperStateReport（将来） | Wallpaper -> UI | presetId, appliedAt | diagnostics | 否 |
| HabitatTransitionRequest（将来） | Character -> UI | characterId, fromHabitat, toHabitat, triggerSource | reason | 要 |
| HabitatTransitionCommit（将来） | UI -> Character/Wallpaper | characterId, toHabitat, portalId | anchorId | 要 |

### 7.2.1 v1 メッセージ payload 型定義
- 共通型:
  - `characterId`: string（UUIDv7）
  - `requestId`: string（UUIDv7）
  - `timestampUnixMs`: int64
  - `ttlMs`: int32（1-60000）
- `SpawnCharacter.payload`:
  - `characterId`: string（必須）
  - `modelPath`: string（必須、空文字不可）
  - `ipcPort`: int32（必須、1024-65535）
  - `initialScale`: float32（任意、0.1-10.0）
- `DespawnCharacter.payload`:
  - `characterId`: string（必須）
  - `reason`: string（任意、最大128文字）
- `WindowSnapshotUpdate.payload`:
  - `characterId`: string（必須）
  - `snapshotVersion`: int64（必須、1以上）
  - `windows`: array<object>（必須）
  - `focusedWindow`: object|null（任意）
- `BubbleCommand.payload`:
  - `characterId`: string（必須）
  - `text`: string（必須、1-500文字）
  - `bubbleType`: enum（`normal`, `thought`, `system`）
  - `ttlMs`: int32（任意、100-10000）
- `VoiceRequest.payload`:
  - `characterId`: string（必須）
  - `text`: string（必須、1-1000文字）
  - `voiceProfile`: string（必須）
  - `emotion`: string（任意、最大32文字）
- `CharacterStateReport.payload`:
  - `characterId`: string（必須）
  - `state`: enum（`idle`, `walking`, `falling`, `landing`, `dragging`, `speaking`）
  - `timestamp`: int64（必須）
  - `debugFields`: object（任意）

### 7.3 エラーハンドリング
- タイムアウト: 200msから1000ms
- リトライ回数: 3回
- 冪等性ルール: Spawn/Despawn は requestId で冪等化
- 重複受信時の扱い: 最新シーケンスのみ適用
- 順序保証: 同一characterId内は seq で整列

### 7.3.1 ACK/NACK ルール
- control チャネルの要求は ACK/NACK を必須とする
- ACK:
  - requestId
  - accepted=true
  - optional data
- NACK:
  - requestId
  - accepted=false
  - errorCode
  - errorMessage
- window、bubble、state は基本 fire-and-forget とし、必要時のみ再同期要求で補正する

### 7.3.2 再接続ルール
- 連続タイムアウト 3回でソケット再生成
- 再接続完了後は `ResyncRequest` を送信し、最新状態を再取得する
- Character 側は再接続後の最初の state 通知を full snapshot で送る

### 7.4 バージョン管理
- スキーマバージョン: major.minor
- 互換方針: minorは後方互換、majorは互換なし可
- 非互換変更時の移行手順: 旧新メッセージ併用期間を設ける

### 7.5 競合優先順位ルール（確定）
#### 7.5.1 Z 順序の基本優先順位
| 優先度 | レイヤ | ルール |
|---|---|---|
| 1 | UI | 常に最前面。操作中と非操作中で優先度は不変 |
| 2 | Character（desktop habitat） | desktop 滞在中は Wallpaper より前面 |
| 3 | Wallpaper | 背景レイヤとして最背面 |

#### 7.5.2 Habitat 遷移競合時の解決順序
| 競合ケース | 優先される要求 | 解決ルール |
|---|---|---|
| user 指示遷移 と autonomous 遷移が同時発生 | user | user を採用し、autonomous はキャンセルする |
| 遷移要求 と UI クリティカル操作（設定保存、終了処理）が同時発生 | UI クリティカル操作 | UI 完了後に遷移要求を再評価する |
| 遷移先 anchor が一時利用不可 | 現在 habitat 維持 | 遷移を保留し、再試行上限到達で NACK を返す |
| 複数 Character が同一 portal を同時要求 | 先着 requestId | requestId 昇順で直列化し、後続はキュー待ち |

#### 7.5.3 応答継続ルール
- Character が wallpaper habitat に滞在中でも呼びかけ応答は継続する
- STT/TTS/LLM 経路は habitat 非依存で維持し、遷移中は発話開始のみ一時抑止して遷移完了後に再開する
- 遷移中に受けた呼びかけは最大 1 件を保留し、完了後に優先処理する

## 8. ライフサイクル
### 8.1 起動シーケンス
1. UIプロセス起動
2. UIが設定を読み込み、キャラ台帳を初期化
3. UIがCharacterプロセスを必要数起動
4. Characterが初期化完了を通知
5. UIがWindowSnapshot配信を開始

### 8.2 終了シーケンス
1. UIがDespawnCharacterを送信
2. Characterが終了通知を返す
3. UIがプロセス終了を確認
4. UIが最終的に自身を終了

### 8.3 再起動・復旧
- プロセス異常終了時: UIが再起動を試行
- 通信断時: HealthPing失敗閾値で再同期または再起動
- 依存サービス異常時: UIが音声/会話を縮退運転に切替

### 8.4 Habitat 遷移シーケンス（将来）
1. Character が `HabitatTransitionRequest` を UI に送信（triggerSource は user または autonomous）
2. UI が `WallpaperPortal` / `WallpaperAnchor` 制約を検証
3. UI が `HabitatTransitionCommit` を Character と Wallpaper に配信
4. Character は遷移後も STT/TTS/LLM 経路を維持し、呼びかけ応答を継続する

## 9. 監視と運用
### 9.1 ログ仕様
- ログレベル: Debug, Info, Warn, Error
- 必須ログ項目: processType, characterId, traceId, eventName
- traceId / characterId の付与ルール: UI起点の要求で traceId を発行し伝播

### 9.2 メトリクス
| 指標名 | 収集先 | 目標値 | 閾値 | 対応 |
|---|---|---|---|---|
| IPC遅延 | UI | p95 100ms以下 | p95 250ms超 | 通信再設定 |
| 受信失敗率 | UI | 0.1%以下 | 1%超 | 再試行/復旧 |
| 再起動回数 | UI | 0から1回/日 | 3回/日超 | 障害調査 |
| CPU/Memory | UI/Character | 目標内 | しきい値超 | 負荷調整 |

### 9.3 運用手順
- 日次確認: エラー件数、再起動件数、遅延分布
- 障害一次切り分け: traceIdベースでUIとCharacterを突合
- ロールバック条件: 重大クラッシュ、データ不整合、操作不能

## 10. セキュリティ・安全性
- 入力検証方針: 受信メッセージの型と範囲を検証
- 不正メッセージ対策: 不明メッセージ破棄、ログ記録
- DoS対策: レート制限とキュー上限
- 機密情報の扱い: トークンや鍵はログ出力禁止
- SDKパッケージ検証方針:
  - `.dac` / `.daw` は署名付きマニフェストを必須とする
  - 署名アルゴリズムは `Ed25519` を採用し、公開鍵フィンガープリントを許可リスト管理する
  - 署名検証失敗時はロード中止し `SDK-SIGN-001` を返す
  - 許可外の署名者はロード拒否し `SDK-SIGN-002` を返す

## 11. パフォーマンス要件
- 起動時間目標: UI起動から初回キャラ表示まで 5秒以内
- 同時キャラ数目標: 3体以上（初期目標）
- 1秒あたりメッセージ処理量: 50メッセージ以上
- フレーム影響の上限: UI側の主処理でフレーム落ちを常時発生させない

## 12. テスト要件
### 12.1 単体テスト
- 対象: メッセージ変換、台帳管理、状態遷移
- 合格条件: 主要パスの自動テストが成功

### 12.2 結合テスト
- シナリオ一覧:
  - UI起動 -> Character起動 -> 状態同期
  - 右クリック -> メニュー表示
  - STT入力 -> LLM -> TTS -> 吹き出し
- 合格条件: 主要シナリオで再現性を確認

### 12.3 負荷・耐久試験
- 条件: 複数キャラ + 連続音声入力 + ウィンドウ変化
- 合格条件: 致命的クラッシュなし、しきい値内

### 12.4 回帰テスト
- 対象機能: キャラ挙動、音声、UI表示、IPC
- 自動化状況:
  - ACC-001 から ACC-010 は CI 自動化対象
  - ACC-011 から ACC-015 は nightly CI 自動化対象
  - 手動確認が必要な項目はリリース候補時に再実施する

### 12.5 CI テストケース対応表（ACC-ID -> ジョブ）
| ACC-ID | テストジョブ名 | 実行頻度 | 必須ゲート | 成果物 |
|---|---|---|---|---|
| ACC-011 | `ci-acceptance-sdk-pivot-hierarchy` | nightly | Gate-B | `artifacts/acc/ACC-011/report.json` |
| ACC-012 | `ci-acceptance-sdk-shared-assets-complete` | nightly | Gate-B | `artifacts/acc/ACC-012/report.json` |
| ACC-013 | `ci-acceptance-sdk-unity-reference-restore` | nightly | Gate-B | `artifacts/acc/ACC-013/report.json` |
| ACC-014 | `ci-acceptance-sdk-normalization-dedup` | nightly | Gate-B | `artifacts/acc/ACC-014/report.json` |
| ACC-015 | `ci-acceptance-sdk-deterministic-output` | nightly | Gate-B | `artifacts/acc/ACC-015/report.json` |

- 運用規約:
  - `OPEN-V1-001` は上表 5 ジョブが 3 連続成功し、成果物が保存確認できた時点でクローズ可能とする
  - 失敗ジョブがある場合、Gate-B は未達として実装開始許可を保留する

## 13. 移行計画
- 旧構成から新構成への切替手順:
  1. 旧構成に互換レイヤを入れる
  2. CharacterCompanion を導入
  3. CharacterManager を台帳型へ縮小
  4. UI中央制御へ切替
- 段階リリース計画:
  - 1キャラ固定の先行リリース
  - 複数キャラ対応を後続リリース
- 検証環境と本番環境の差分:
  - 検証環境: transport は `tcp` 優先、詳細ログ有効、デバッグUI有効
  - 本番環境: transport は `ipc` 優先、PIIマスクログ、デバッグUI無効
  - 共通条件: 同一 SDK 互換ポリシー、同一 DB マイグレーションルール

## 14. 未解決事項
| ID | 論点 | 影響 | 期限 | 担当 |
|---|---|---|---|---|
| OPEN-VNEXT-001 | 壁紙プロセス本実装時の `wallpaper_control` 再接続戦略 | vNext品質 | 2026-06-30 | |
| OPEN-VNEXT-002 | Habitat遷移の本実装における portal 混雑制御（キュー破棄条件） | vNext安定性 | 2026-06-30 | |
| OPEN-V1-001 | セクション 12.5 の ACC-011 から ACC-015 ジョブを 3 連続成功させ成果物保存を確認 | リリース判定 | 2026-06-10 | QA/CI |

## 15. 意思決定ログ
| 日付 | 決定内容 | 理由 | 影響範囲 | 決定者 |
|---|---|---|---|---|
| 2026-05-30 | UIを中央制御、Characterを個体制御に分離 | 責務明確化と拡張性確保 | 全体 | |
| 2026-05-30 | IPCにZeroMQを採用 | 複数プロセス拡張と通信管理の整合性確保 | 通信仕様、実装基盤 | |
| 2026-05-30 | ZeroMQ の基本パターンを ROUTER/DEALER + PUB/SUB とする | 制御系と配信系の責務分離を明確化 | 通信設計、実装詳細 | |
| 2026-05-30 | ZeroMQ transport を tcp + ipc 併用方針に確定 | 開発容易性と本番性能を両立するため | 通信実装、運用設計 | |
| 2026-05-30 | 永続化をハイブリッド（SQLite + JSON）で採用 | 整合性、復旧性、運用編集性を両立するため | 永続化設計、運用設計 | |
| 2026-05-30 | SQLite の基本テーブル構成とトランザクション境界を確定 | OPEN-002 を実装粒度まで具体化するため | 永続化実装、復旧処理 | |
| 2026-05-30 | 音声設定は SDK とアプリ設定の両対応、エージェント割当はアプリ設定のみと確定 | 運用柔軟性と責務境界を両立するため | 設定解決、起動時検証、運用手順 | |
| 2026-05-30 | 前回状態（位置/アニメーション）の復元を行わない方針に確定 | 永続化コスト削減と起動単純化のため | 永続化設計、復旧処理 | |
| 2026-05-30 | SQLite マイグレーション手順を確定し OPEN-002 をクローズ | バージョンアップ時の障害リスクを低減するため | 起動処理、DB運用、障害復旧 | |
| 2026-05-30 | 壁紙プロセス境界を仮定仕様として定義し OPEN-003 を具体化 | 将来実装時の責務衝突を先に回避するため | プロセス境界、通信仕様、運用設計 | |
| 2026-05-30 | キャラクターの desktop/wallpaper 遷移、SDK固定 Portal/Anchor、壁紙滞在中の呼びかけ応答を要件化 | ユーザー操作性と将来自律行動を両立するため | 機能要件、通信仕様、SDK設計 | |
| 2026-05-30 | Z順序競合と Habitat 遷移競合の優先順位を確定し OPEN-003 をクローズ | 遷移時の挙動不定を解消するため | 表示制御、遷移制御、対話継続性 | |
| 2026-05-30 | 実装前の仕様凍結パッケージ（SDK詳細、検証規約、受け入れ基準）を定義 | 実装後戻りを最小化し、SDK公開品質を確保するため | SDK、ロード処理、CI、リリース判断 | |
| 2026-05-30 | SDKをキャラクター作成用と壁紙レベル作成用の2種に分離し、各SDK出力を単一独自拡張子ファイルへ統一 | 配布と読み込みを単純化し、運用ミスを低減するため | SDKツール、ロード処理、配布運用 | |
| 2026-05-30 | Character SDKのエクスポート対象をCharacterDescriptor付きルートオブジェクトに固定し、子階層保持・pivot着地点・root Animator必須を確定 | 実行時挙動とアセット構造の不一致を防ぐため | SDKエクスポート、ロード、バリデーション | |
| 2026-05-30 | 両SDKに共通して使用されるテクスチャ/マテリアル/シェーダー情報を同梱必須に確定 | 実行時依存欠落と見た目不整合を防ぐため | SDKパッケージ、検証、ロード処理 | |
| 2026-05-30 | sharedAssets の要素スキーマ（ID規約、ハッシュ、参照整合）を確定 | SDK検証と再現性を実装可能な粒度にするため | SDKバリデータ、ロード処理、CI試験 | |
| 2026-05-30 | sharedAssets スキーマを Unity 資産参照仕様（GUID/fileID、Importer、ShaderLabプロパティ）準拠に改訂 | Unity 実資産との互換性と再現性を確保するため | SDKバリデータ、アセット復元、描画互換性 | |
| 2026-05-30 | sharedAssets 正規化規約（重複解決、ID再マップ、決定的パッケージ化）を確定 | 同入力で同一出力を保証し差分管理を容易にするため | SDKエクスポーター、CI差分、配布運用 | |
| 2026-05-30 | 実装移行ブロッカー解消のため v1/vNext 境界、通信型定義、署名検証、テスト運用条件を明確化 | 実装着手時の解釈差と完了判定矛盾を解消するため | スコープ管理、通信実装、セキュリティ、QA運用 | |
| 2026-05-30 | ACC-ID と CI ジョブの対応表および `OPEN-V1-001` クローズ条件を確定 | v1 リリース判定を客観化し、Gate-B 判定を自動化可能にするため | QA運用、CI運用、実装開始判定 | |

## 16. 変更履歴
| バージョン | 日付 | 変更内容 | 作成者 |
|---|---|---|---|
| 0.1.0-draft | 2026-05-30 | 初版ドラフト作成 | |
| 0.1.1-draft | 2026-05-30 | IPC方式をZeroMQに確定、関連項目を更新 | |
| 0.2.0-draft | 2026-05-30 | ZeroMQのソケット役割、トピック、封筒、ACKルールを追記 | |
| 0.2.1-draft | 2026-05-30 | transport方針を tcp + ipc 併用で確定 | |
| 0.3.0-draft | 2026-05-30 | 永続化方針をハイブリッドで確定し、同期/復旧ルールを追記 | |
| 0.3.1-draft | 2026-05-30 | SQLiteスキーマ定義とトランザクション境界を追記 | |
| 0.3.2-draft | 2026-05-30 | 音声/エージェントの設定入力境界を確定 | |
| 0.3.3-draft | 2026-05-30 | 前回状態復元を廃止し、位置/アニメーションの永続化を対象外に変更 | |
| 0.3.4-draft | 2026-05-30 | SQLiteマイグレーション手順を追記し OPEN-002 を解消 | |
| 0.3.5-draft | 2026-05-30 | 壁紙プロセスの境界仮定と将来IPC契約を追記し OPEN-003 を具体化 | |
| 0.3.6-draft | 2026-05-30 | Habitat遷移要件、SDK固定Portal/Anchor、壁紙滞在中の呼びかけ応答要件を追記 | |
| 0.3.7-draft | 2026-05-30 | Z順序/遷移競合の優先順位を確定し OPEN-003 を解消 | |
| 0.4.0-draft | 2026-05-30 | 実装前仕様凍結パッケージとしてSDK詳細仕様と受け入れゲートを追記 | |
| 0.4.1-draft | 2026-05-30 | SDKを2種に分離し、各SDK出力を単一独自拡張子ファイルに統一 | |
| 0.4.2-draft | 2026-05-30 | Character SDKのルート/階層/pivot/Animator/Descriptor仕様を確定 | |
| 0.4.3-draft | 2026-05-30 | 両SDKの共通テクスチャ/マテリアル/シェーダー情報同梱を必須化 | |
| 0.4.4-draft | 2026-05-30 | sharedAssets スキーマ詳細とGo判定整合を追記 | |
| 0.4.5-draft | 2026-05-30 | sharedAssets を Unity 準拠スキーマへ改訂し検証項目を拡張 | |
| 0.4.6-draft | 2026-05-30 | sharedAssets 正規化と決定的パッケージ化ルールを追記 | |
| 0.4.7-draft | 2026-05-30 | 実装移行向けに v1/vNext 境界、通信型、署名検証、テスト運用条件を追記 | |
| 0.4.8-draft | 2026-05-30 | ACC-011 から ACC-015 の CI対応表、成果物規約、`OPEN-V1-001` クローズ条件を追記 | |

## 17. 実装前仕様凍結パッケージ
### 17.1 凍結対象一覧
| 区分 | 凍結対象 | 凍結レベル | 変更許可条件 |
|---|---|---|---|
| IPC | メッセージ名、必須フィールド、ACK/NACK規約 | Hard | major更新のみ |
| データ | SQLiteテーブル名、主キー、必須インデックス | Hard | マイグレーション提案と承認 |
| SDK | manifestスキーマ、Portal/Anchor定義、パッケージ構造 | Hard | SDKバージョン更新 |
| 挙動 | Habitat遷移優先順位、応答継続ルール | Hard | 互換性影響評価の承認 |
| 運用 | 受け入れ基準、Go/No-Go判定項目 | Soft | リリース責任者承認 |

### 17.2 仕様凍結ゲート
- 適用原則:
  - v1 実装判定ではセクション 2.3 で `v1 対象` とした項目のみを必須評価対象とする
- Gate-A（設計凍結）:
  - セクション 6、7、18、19、20 の必須項目が空欄なし
  - 変更履歴に凍結版タグを記録
- Gate-B（実装開始許可）:
  - 受け入れテスト項目の 100% がテストケース化済み
  - SDKバリデータのエラーコード一覧が確定
- Gate-C（SDK公開許可）:
  - 後方互換試験に合格
  - サンプルSDKパックでの起動/遷移/呼びかけ応答が合格

## 18. SDK 詳細仕様
### 18.1 SDK 種別と出力ファイル
| SDK種別 | 目的 | 出力ファイル | 拡張子 | 出力数 |
|---|---|---|---|---|
| Character SDK | キャラクター作成 | Character Package | `.dac` | 1ファイルのみ |
| Wallpaper Level SDK | 壁紙レベル作成 | Wallpaper Level Package | `.daw` | 1ファイルのみ |

- 共通規約:
  - SDK出力は圧縮コンテナ形式の単一ファイルとする
  - 実行環境はコンテナ内部を展開せず、ストリーム読込で検証/ロードする
  - 外部参照ファイル（別json、別asset）を要求してはならない
  - 両SDKには共通アセット情報 `sharedAssets` を必ず含める
  - `sharedAssets` には `textures[]`, `materials[]`, `shaders[]` の識別情報と参照関係を含める
  - 共通アセットはパッケージ外参照を禁止し、当該 `.dac` / `.daw` 内で自己完結させる

### 18.2 Character SDK パッケージ仕様（`.dac`）
- ルートドキュメント: `character.manifest`
- エクスポート対象オブジェクト規約:
  - シーン上の `CharacterDescriptor` がアタッチされたオブジェクトをルートとして出力する
  - ルート配下の子オブジェクトは、親子関係を維持したまま同梱する
  - ルートオブジェクトの pivot 位置を当該キャラクターの着地点として扱う
  - `Animator` はルートオブジェクトにアタッチされていなければならない
- ルート `CharacterDescriptor` 設定項目:
  - `coeiroinkSpeakerId`（テキスト）
  - `characterCallName`（テキスト）
  - `characterPersonality`（テキスト）
- 必須フィールド:
  - `sdkPackageId`（逆ドメイン形式）
  - `sdkPackageVersion`（semver）
  - `desktopAgentCompatibility`（例: `>=0.4.1 <1.0.0`）
  - `characterId`（UUIDv7推奨）
  - `displayName`
  - `model.entry`
  - `capabilities`（`desktopHabitat`, `wallpaperHabitat`, `voiceInteraction`）
  - `sharedAssets`（`textures[]`, `materials[]`, `shaders[]`）
- 任意フィールド:
  - `defaultVoiceProfile`
  - `defaultAgentHint`（注: 実行時割当には使用しない）
  - `tags`

### 18.3 Wallpaper Level SDK パッケージ仕様（`.daw`）
- ルートドキュメント: `wallpaper.manifest`
- 必須定義:
  - `levelId`
  - `levelVersion`
  - `desktopAgentCompatibility`
  - `portals[]`
    - `portalId`
    - `entryPoint`（x, y, z）
    - `exitPoint`（x, y, z）
    - `direction`（`desktopToWallpaper`, `wallpaperToDesktop`, `bidirectional`）
  - `anchors[]`
    - `anchorId`
    - `position`（x, y, z）
    - `poseType`（`sit`, `lie`）
    - `capacity`（同時利用可能数）
  - `sharedAssets`（`textures[]`, `materials[]`, `shaders[]`）
- 制約:
  - `portalId` と `anchorId` は level 内で一意
  - `anchors.capacity` は 1 以上
  - `poseType` は列挙値のみ

### 18.4 SDK バリデーション規約
- エラーコード規約: `SDK-{DOMAIN}-{NNN}`
- 主要エラー:
  - `SDK-MANIFEST-001`: 必須フィールド欠落
  - `SDK-MANIFEST-002`: semver不正
  - `SDK-PACKAGE-001`: 単一ファイル規約違反
  - `SDK-PACKAGE-002`: 拡張子不一致（`.dac`/`.daw`以外）
  - `SDK-PACKAGE-003`: `sharedAssets` 欠落
  - `SDK-PACKAGE-004`: 共有アセット参照不整合（material->shader / material->texture）
  - `SDK-PACKAGE-005`: 共有アセットの外部参照検出
  - `SDK-PACKAGE-006`: sharedAssets ID命名規約違反
  - `SDK-PACKAGE-007`: sharedAssets contentHash 不正
  - `SDK-PACKAGE-008`: Unity GUID または fileID が不正
  - `SDK-PACKAGE-009`: Unity assetPath 形式不正（`Assets/...` 非準拠）
  - `SDK-PACKAGE-010`: ShaderLab プロパティ名不正
  - `SDK-PACKAGE-011`: Unity importer 設定値不正
  - `SDK-PACKAGE-012`: sharedAssets 正規化後の参照不整合
  - `SDK-PACKAGE-013`: 決定的パッケージ化規約違反（並び順/キー順/圧縮条件）
  - `SDK-CHAR-001`: CharacterDescriptor を持つルートオブジェクト不在
  - `SDK-CHAR-002`: ルートオブジェクトに Animator 未アタッチ
  - `SDK-CHAR-003`: CharacterDescriptor 必須項目（coeiroinkSpeakerId/callName/personality）欠落
  - `SDK-CHAR-004`: ルート pivot が不正（未定義または非数値）
  - `SDK-CHAR-005`: エクスポート時に親子階層を保持できない
  - `SDK-TOPO-001`: portalId 重複
  - `SDK-TOPO-002`: anchorId 重複
  - `SDK-TOPO-003`: poseType 不正値
  - `SDK-ASSET-001`: model.entry 未解決
  - `SDK-COMPAT-001`: DesktopAgent 互換範囲外
- 警告コード:
  - `SDK-WARN-001`: voice preset 未指定
  - `SDK-WARN-002`: thumbnail 未指定

### 18.5 SDK とアプリ設定の優先順位
| 設定項目 | 1位 | 2位 | 備考 |
|---|---|---|---|
| 音声設定 | アプリ設定 | SDK | 既定値のみSDKから採用 |
| エージェント割当 | アプリ設定のみ | - | SDK指定は無視してWarn |
| Portal/Anchor | Wallpaper Level SDKのみ | - | 実行中上書き不可 |

### 18.6 SDK パッケージ構造（標準）
```text
character-sdk output:
  <package-name>.dac

wallpaper-level-sdk output:
  <level-name>.daw
```

### 18.7 sharedAssets 要素スキーマ（確定）
- Unity 準拠の前提:
  - 参照識別子は Unity の `guid`（32桁hex）と `fileID`（long）を正とする
  - `assetPath` は `Assets/...` の Unity プロジェクト相対パス形式とする
  - shader プロパティ名は ShaderLab のプロパティ名（例: `_BaseMap`, `_BaseColor`）を使用する
- `textures[]` 要素:
  - `guid`（32桁hex）
  - `fileID`（long, 通常はメインオブジェクト）
  - `assetPath`（`Assets/...`）
  - `importer`:
    - `textureType`（`Default`, `NormalMap`, `Sprite`, `SingleChannel`）
    - `sRGBTexture`（bool）
    - `mipmapEnabled`（bool）
    - `filterMode`（`Point`, `Bilinear`, `Trilinear`）
    - `wrapModeU`, `wrapModeV`（`Repeat`, `Clamp`, `Mirror`, `MirrorOnce`）
    - `anisoLevel`（0-16）
  - `source`（`png`, `jpg`, `tga`, `psd`, `exr`, `hdr`, `ktx2`）
  - `contentHash`（SHA-256, 64桁hex）
- `materials[]` 要素:
  - `guid`（32桁hex）
  - `fileID`（long）
  - `assetPath`（`Assets/...`）
  - `shaderRef`:
    - `guid`（32桁hex）
    - `fileID`（long）
    - `shaderName`（例: `Universal Render Pipeline/Lit`）
  - `renderQueue`（int）
  - `enabledKeywords[]`（string）
  - `floatProperties`（`propertyName -> float`）
  - `colorProperties`（`propertyName -> {r,g,b,a}`）
  - `vectorProperties`（`propertyName -> {x,y,z,w}`）
  - `textureProperties[]`:
    - `propertyName`（ShaderLabプロパティ名）
    - `textureRef`（`guid`, `fileID`）
    - `scale`（`x`, `y`）
    - `offset`（`x`, `y`）
  - `contentHash`（SHA-256, 64桁hex）
- `shaders[]` 要素:
  - `guid`（32桁hex）
  - `fileID`（long）
  - `assetPath`（`Assets/...` または内蔵識別名）
  - `shaderName`
  - `renderPipeline`（`BuiltIn`, `URP`, `HDRP`）
  - `targetSubShaders[]`（任意）
  - `contentHash`（SHA-256, 64桁hex）
- 共通制約:
  - 各要素は `guid + fileID` の組で一意であること
  - `materials[].shaderRef` は `shaders[]` に必ず解決すること
  - `materials[].textureProperties[].textureRef` は `textures[]` に必ず解決すること
  - `assetPath` の拡張子は Unity で許容される形式と一致すること
  - 同一 `contentHash` の重複要素は同一アセットとして正規化してよい

### 18.8 sharedAssets 正規化規約（確定）
- 同一判定:
  - 第1キーは `contentHash`
  - 第2キーは `guid+fileID`
  - 両方一致時は同一アセットと見なす
- 重複解決:
  - 同一 `contentHash` の複数要素が存在する場合、`assetPath` 昇順で代表要素を選ぶ
  - 非代表要素は `normalizedFrom` に代表要素の `guid+fileID` を記録する
- ID再マッピング:
  - エクスポート時に `guid+fileID` は保持し、内部インデックスのみ再採番してよい
  - 再採番後の参照は `materials[].shaderRef` / `textureRef` へ一括反映する
- 決定的パッケージ化:
  - `textures`, `materials`, `shaders` は `assetPath` 昇順で直列化する
  - JSONキー順は辞書順で固定する
  - 圧縮レベルは固定値を使用し、同入力時にバイナリ同一となることを目標とする

## 19. 実行時契約（SDKロードから遷移まで）
### 19.1 ロード手順
1. UI が `.dac` と `.daw` のルートマニフェストを検証する
2. 互換範囲を確認し、失敗時はロード中止
3. `.dac` / `.daw` の `sharedAssets` を検証し、依存グラフを解決する
4. `sharedAssets` の解決失敗時はロード中止（`SDK-PACKAGE-004` または `SDK-PACKAGE-005`）
5. `.daw` から Portal/Anchor をメモリへロードする
6. 音声設定はアプリ設定で上書き解決する
7. `.dac` のルートオブジェクトを階層保持で復元し、ルート pivot を着地点として登録する
8. Character 起動後に capability と habitat 初期値を通知する

### 19.2 Habitat 遷移契約
- 遷移要求は必ず UI を経由する
- 直接遷移（Character -> Wallpaper直結）は禁止
- 遷移決定は `HabitatTransitionCommit` のみを正とする
- commit 前に portal 解決失敗した場合は NACK を返す

### 19.3 呼びかけ応答契約
- habitat が `wallpaper` でも `voiceInteraction=true` の場合は応答必須
- UI は STT イベントを habitat 非依存でルーティングする
- 応答不能時は `degraded-voice-routing` を発行し運用ログへ記録する

## 20. 受け入れ基準（実装前確定版）
### 20.1 必須テストシナリオ
| ID | シナリオ | 合格条件 |
|---|---|---|
| ACC-001 | SDKロード成功 | `.dac`/`.daw` の検証が通過し起動可能 |
| ACC-002 | SDK互換外拒否 | `SDK-COMPAT-001` で起動拒否 |
| ACC-003 | desktop->wallpaper 遷移（user） | request から commit まで 1 秒以内 |
| ACC-004 | wallpaper->desktop 遷移（autonomous） | 競合時に user 指示優先が成立 |
| ACC-005 | 壁紙滞在中の呼びかけ応答 | 3 回連続で応答成功 |
| ACC-006 | Anchor 競合 | requestId 昇順で直列処理される |
| ACC-007 | voice 優先順位 | アプリ設定がSDK設定を上書きする |
| ACC-008 | agent 割当制約 | SDK由来agent指定が無視されWarn記録 |
| ACC-009 | Character ルート規約検証 | CharacterDescriptor付きルートが必須で、欠落時は `SDK-CHAR-001` |
| ACC-010 | root Animator 必須検証 | root に Animator がない場合は `SDK-CHAR-002` |
| ACC-011 | pivot/階層復元検証 | 着地点に root pivot を使用し、親子階層が一致する |
| ACC-012 | 共有アセット同梱検証 | `.dac`/`.daw` の双方で `sharedAssets` が解決でき、外部参照がない |
| ACC-013 | Unity参照整合検証 | `guid+fileID`、`assetPath`、ShaderLabプロパティ、Importer設定が妥当である |
| ACC-014 | 正規化安定性検証 | 同一入力を2回エクスポートして sharedAssets 正規化結果が一致する |
| ACC-015 | 決定的出力検証 | 同一入力で `.dac`/`.daw` のバイナリハッシュが一致する |

### 20.2 定量目標
- 遷移 commit 遅延: p95 800ms 以下
- 呼びかけ応答開始遅延（wallpaper滞在時）: p95 1500ms 以下
- SDKロード失敗率（有効パック）: 0.1% 以下
- 共有アセット解決失敗率（有効パック）: 0.1% 以下
- Unity参照検証失敗率（有効パック）: 0.1% 以下
- 正規化不一致率（同一入力再出力）: 0%（許容 0 件）

### 20.3 Go/No-Go 判定
- Go 条件:
  - ACC-001 から ACC-015 の全合格
  - Critical/P1 不具合が 0 件
- No-Go 条件:
  - Habitat遷移でデッドロックが再現
  - 壁紙滞在時の呼びかけ応答成功率が 95% 未満

