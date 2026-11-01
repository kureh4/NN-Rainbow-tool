# NN - Rainbow tool

VRChatアバターのlilToonマテリアルへ、NDMFで非破壊にレインボー表現を追加するツールです。

## VCC / VPM

### BOOTHなどに貼るURL

https://kureh4.github.io/NN-Rainbow-tool/add-vcc.html

### Listing Web UI

https://kureh4.github.io/NN-Rainbow-tool/

### Repository URL

https://kureh4.github.io/NN-Rainbow-tool/index.json

## Unity内の構成

VCCで導入すると本体はPackagesへ入り、ユーザーが配置するPrefabは初回読み込み時にAssetsへ自動コピーされます。

```text
Packages/
└─ com.nekoneko.nn-rainbow-tool/
   ├─ Editor/
   ├─ Prefab/
   ├─ Resources/
   ├─ Runtime/
   └─ package.json

Assets/
└─ nekoneko/
   └─ NN - Rainbow tool/
      └─ NN_Rainbow.prefab
```

既にAssets側にPrefabが存在する場合は自動上書きしません。
