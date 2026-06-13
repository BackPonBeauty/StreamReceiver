# 🖥️ StreamReceiver v1.0.0

**Client app for [Supermodel3-PonMi-Streaming](https://github.com/BackPonBeauty/Supermodel3-PonMi-Streaming).**

Connect to a host running Supermodel3-PonMi-Streaming and play Sega Model 3 arcade titles over WAN.

---

## ✨ Features

- **Automatic host discovery** via Firebase matchmaking — no manual IP entry needed
- **H.264 / H.265 video decoding** via ffmpeg — codec is automatically selected to match the host
- **Opus audio playback**
- **XInput controller input** sent to host over UDP
- **Ping display** per host
- **Discord authentication** — only members of [discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) can launch the app. Re-authentication required after 30 days of inactivity.
- **F11 fullscreen**

---

## 💻 Requirements

| Item | Requirement |
|------|-------------|
| OS | Windows 10/11 64-bit |
| Runtime | .NET Framework 4.8 |
| Controller | XInput compatible controller |
| Network | 5 Mbps download or faster recommended |

---

## ⚙️ Specifications & Limitations

- **XInput only** — mouse and light gun input are not supported
- **AFK kick** — players with no input for 1 minute will be kicked
- **Esc key** — disconnect and exit the app
- **Up to 3 connections per host slot** — one player + up to 2 spectators
- If the current player disconnects, the next spectator becomes the player
- **Codec auto-switching** — the client automatically uses H.264 or H.265 depending on the host's configuration. No client-side setting required.

---

## 🚀 Setup

### 1. Download and extract

Extract the zip file to any folder.

### 2. Allow through Windows Firewall

Allow `StreamReceiver.exe` through Windows Firewall for both private and public networks.

### 3. Launch StreamReceiver.exe

StreamReceiver will launch and prompt you to authenticate with Discord.

### 4. Discord authentication

Only members of [discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) can proceed.  
Authentication is saved — you will not need to authenticate again unless 30 days have passed without launching the app.

### 5. Select a host and connect

Choose a host from the list and click Connect. The game will start streaming.

> ⚠️ ffmpeg is required. If it is not found, the app will open the download page automatically.

---

## 🔧 Related Repositories

- [Supermodel3-PonMi-Streaming](https://github.com/BackPonBeauty/Supermodel3-PonMi-Streaming) — Host emulator (required on the host side)
- [Supermodel3-PonMi](https://github.com/BackPonBeauty/Supermodel3-PonMi) — Base PonMi edition emulator

---

## ⚠️ Windows SmartScreen

A SmartScreen warning may appear on first launch due to the absence of a code signing certificate.  
Click **"More info" → "Run anyway"** to proceed.

---

## 📜 License

This application is distributed under the **GPL v3** license.  
ffmpeg is bundled in compliance with the GPL v3 license.

### Third-party Libraries

- [ffmpeg-2026-06-01-git-bf608f16fd-essentials_build](https://www.gyan.dev/ffmpeg/builds/) — [LGPL 2.1 / GPL 2.0](https://ffmpeg.org) / Build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)
- ViGEm — BSD License
- Newtonsoft.Json — MIT License
- LiteDB — MIT License
- Firebase.Auth — MIT License

---

## 💬 Community

- [BackPonBeauty](https://github.com/BackPonBeauty) — GitHub
- [patreon.com/PonMi](https://patreon.com/PonMi) — Patreon
- [discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) — Discord
- [back_pon_beauty](https://twitch.tv/back_pon_beauty) — Twitch

---
---

# 🇯🇵 日本語

# 🖥️ StreamReceiver v1.0.0

**[Supermodel3-PonMi-Streaming](https://github.com/BackPonBeauty/Supermodel3-PonMi-Streaming) 用クライアントアプリです。**

Supermodel3-PonMi-Streamingを起動しているホストに接続して、WANでセガ Model 3 アーケードタイトルをプレイできます。

---

## ✨ 機能

- **Firebase自動マッチメイキング** — ホストを自動検出、手動でのIP入力は不要
- **H.264 / H.265 映像デコード**（ffmpeg使用）— ホストの設定に合わせてコーデックを自動切り替え
- **Opusオーディオ再生**
- **XInputコントローラ入力** をUDPでホストへ送信
- **Ping表示**
- **Discord認証** — [discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) のメンバーのみ起動可能。30日間未使用の場合は再認証が必要です。
- **F11フルスクリーン**

---

## 💻 動作環境

| 項目 | 要件 |
|------|------|
| OS | Windows 10/11 64bit |
| ランタイム | .NET Framework 4.8 |
| コントローラ | XInput対応コントローラ |
| 回線 | ダウンロード 5Mbps 以上推奨 |

---

## ⚙️ 仕様・制限事項

- **XInput対応タイトルのみ** — マウス操作・ライトガン使用タイトルは非対応
- **無操作キック** — 1分間入力がないプレーヤーは自動的にキックされます
- **Escキー** — 切断してアプリを終了します
- **各ホストスロットへの接続は最大3人まで** — プレーヤー1人＋観戦者最大2人
- プレーヤーが切断した場合、次の観戦者が自動的にプレーヤーになります
- **コーデック自動切り替え** — ホスト側の設定に応じてH.264またはH.265を自動的に選択します。クライアント側での設定は不要です。

---

## 🚀 セットアップ

### 1. ダウンロードして展開

zipファイルを任意のフォルダに展開してください。

### 2. Windowsファイアウォールで許可

`StreamReceiver.exe` をプライベート・パブリック両方のネットワークで通信を許可してください。

### 3. StreamReceiver.exe を起動

起動するとDiscord認証が求められます。

### 4. Discord認証

[discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) のメンバーのみ起動できます。  
認証情報は保存されるため、毎回認証する必要はありません。30日間起動しなかった場合は再認証が必要です。

### 5. ホストを選択して接続

ホスト一覧からホストを選択してConnectをクリックするとストリーミングが始まります。

> ⚠️ ffmpegが必要です。見つからない場合は自動的にダウンロードページが開きます。

---

## 🔧 関連リポジトリ

- [Supermodel3-PonMi-Streaming](https://github.com/BackPonBeauty/Supermodel3-PonMi-Streaming) — ホスト側エミュレータ（ホスト側に必要）
- [Supermodel3-PonMi](https://github.com/BackPonBeauty/Supermodel3-PonMi) — ベースとなるPonMi editionエミュレータ

---

## ⚠️ Windows SmartScreen について

コード署名証明書が未取得のため、初回起動時に SmartScreen の警告が表示される場合があります。  
「詳細情報」→「実行」で起動できます。

---

## 📜 ライセンス

本アプリケーションは **GPL v3** ライセンスのもとで配布されています。  
ffmpeg は GPL v3 ライセンスを遵守して同梱しています。

### サードパーティライブラリ

- [ffmpeg-2026-06-01-git-bf608f16fd-essentials_build](https://www.gyan.dev/ffmpeg/builds/) — [LGPL 2.1 / GPL 2.0](https://ffmpeg.org) / ビルド配布元：[gyan.dev](https://www.gyan.dev/ffmpeg/builds/)
- ViGEm — BSD License
- Newtonsoft.Json — MIT License
- LiteDB — MIT License
- Firebase.Auth — MIT License

---

## 💬 コミュニティ

- [BackPonBeauty](https://github.com/BackPonBeauty) — GitHub
- [patreon.com/PonMi](https://patreon.com/PonMi) — Patreon
- [discord.gg/mNjPJHTTen](https://discord.gg/mNjPJHTTen) — Discord
- [back_pon_beauty](https://twitch.tv/back_pon_beauty) — Twitch
