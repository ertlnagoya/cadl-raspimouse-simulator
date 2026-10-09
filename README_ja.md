# CADL Raspimouse Simulator

[English](README.md) | 日本語

[CADL](https://github.com/ertlnagoya/cadl)（Contract Architecture Description
Language）のハンズオン講座で使うシミュレータです。5 台の Raspberry Pi Mouse が
グラフ状の道路網の上で配送を行う協調型 System of Systems（C-SoS）を動かし、
CADL / SoS-DSL で書いた契約を実行中に監視します。

このリポジトリには講座に必要なものだけを収録しています。手順は
[ハンズオン教材](https://www.ertl.jp/cadl-spec/ja/docs/handson/main-textbook)
に沿って進めてください。以下はリポジトリの案内です。

## 構成

| パス | 内容 | 使う場面 |
|---|---|---|
| `cadl/runtime/` | SoS-DSL 契約（ライフサイクル・期限・モニタ）の Python 参照ランタイム。スクリプト化したデモとテスト付き | Step 5、演習 |
| `unity/` | Unity プロジェクト。C-SoS シーン、ロボットと道路のプレハブ、生成した C# を置く `Assets/Scripts/SoSDsl/` | Step 5〜6 |
| `arbitrator/C-SoS/` | NATS 経由で道路区間の許可と配送の割り当てを行う Go 製アービトレータ | Step 6 |
| `unity-mcp-custom/MCPForUnity/` | `unity/Packages/manifest.json` が参照する Unity パッケージ | （Unity が読み込む） |

## 動作確認

Python ランタイムは Python 3.9 以上があれば動きます。

```bash
python3 -m cadl.runtime.multi_robot_demo --summary
python3 -m pytest cadl/runtime/tests -q
```

リポジトリのルートで実行してください。ディレクトリ名 `cadl` はコンパイラの
Python パッケージと同名ですが、ルートで実行すればこちらが使われます。

アービトレータには Go 1.21 以上と、起動済みの NATS サーバーが必要です。

```bash
nats-server &
cd arbitrator/C-SoS/main
go run main.go -config ../../../unity/Assets/streamingAssets/cadl_config.json
```

`unity/` は **Unity 6000.2.9f1**（Unity 6.2。プロジェクトを保存したバージョン）で開き、
`Assets/Scenes/C-SoS.unity` を開いてください。

## 契約コードの生成

`unity/Assets/Scripts/SoSDsl/` の `Generated/` と `Runtime/` は CADL コンパイラの
生成物です。`.cadl` ファイルから作り直すには、
[cadl](https://github.com/ertlnagoya/cadl) のチェックアウトで次を実行します。

```bash
./scripts/sos_dsl_handson_e2e.sh examples/sos_dsl_robot_delivery.cadl \
    --unity ../cadl-raspimouse-simulator/unity
```

## このリポジトリについて

シミュレータ本体は別の研究用リポジトリで開発しており、このリポジトリは講座で
使う部分を定期的に書き出したものです（元のコミットは `.export-source` に記録
しています）。Issue は歓迎します。`cadl/`、`unity/`、`arbitrator/`、
`unity-mcp-custom/` への変更は次回の書き出しで上書きされるため、修正の提案は
プルリクエストではなく Issue でお知らせください。

## ライセンス

[Apache License 2.0](LICENSE)。第三者のコンポーネントはそれぞれのライセンスに
従います。[NOTICE](NOTICE) を参照してください。
