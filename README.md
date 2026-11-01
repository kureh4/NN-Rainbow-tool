# NN - Rainbow tool

VRChatアバター向けの lilToon レインボー化ツールです。  
NDMFを利用して非破壊でExpression Menu / FX / Parametersへ統合します。

## VCC / VPM

### VCCへ追加

BOOTHなどには次のURLを貼れます。

```text
vcc://vpm/addRepo?url=https%3A%2F%2Fraw.githubusercontent.com%2Fkureh4%2FNN-Rainbow-tool%2Fmain%2Findex.json
```

### 手動追加用リポジトリURL

```text
https://raw.githubusercontent.com/kureh4/NN-Rainbow-tool/main/index.json
```

VCCの **Settings → Packages → Add Repository** に上のURLを追加したあと、対象プロジェクトの **Manage Project** から **NN - Rainbow tool** を追加してください。

## VPMパッケージ構成

```text
Packages/
└─ jp.nekoneko.nn-rainbow-tool/
   ├─ Editor/
   ├─ Prefab/
   │  └─ NN_Rainbow.prefab
   ├─ Resources/
   │  └─ NN_RainbowGradient.png
   ├─ Runtime/
   └─ package.json
```

VCCから導入すると、Unity上では `Packages/jp.nekoneko.nn-rainbow-tool/` に入ります。

## 依存パッケージ

- VRChat Avatars SDK
- NDMF
- lilToon

VCC/VPM導入時は依存関係として解決されます。

## 旧unitypackage版からの移行

VPM manifest の `legacyFolders` に旧フォルダを登録しているため、旧 `Assets/nekoneko/NN - Rainbow tool` / `Assets/nekoneko/NN_Rainbow` との二重導入を避ける構成です。
