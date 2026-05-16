# アイコン候補

フォト ビューアー アプリのアイコン案 10 パターンです。

| ファイル | コンセプト | 配色 |
|---|---|---|
| `icon01_camera.svg` | クラシックカメラ | ブルー系 |
| `icon02_frame.svg` | 木製フォトフレーム＋山の風景 | ブラウン・グリーン |
| `icon03_grid.svg` | 2×2 フォトグリッド | マルチカラー |
| `icon04_magnify.svg` | ルーペで写真を拡大 | パープル |
| `icon05_polaroid.svg` | ポラロイド写真 | ホワイト |
| `icon06_filmstrip.svg` | フィルムストリップ | ダークグレー |
| `icon07_gallery.svg` | ギャラリー（重なった写真） | シアン |
| `icon08_eye.svg` | 瞳の中の風景 | ピンク |
| `icon09_folder.svg` | フォルダーから写真 | オレンジ |
| `icon10_minimal.svg` | ミニマルフラット（グラデーション） | ブルー→パープル→ピンク |

## .ico への変換

SVG を Windows アプリ用の `.ico` に変換するには以下のいずれかを使用します。

- **Inkscape** (無料): ファイル → エクスポート → PNG で 256×256 を出力後、ImageMagick で変換
- **ImageMagick**: `magick icon01_camera.svg -resize 256x256 icon01_camera.ico`
- **オンラインツール**: svg2ico など
