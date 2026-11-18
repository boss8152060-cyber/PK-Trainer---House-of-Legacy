# PK Trainer - House of Legacy

In-game trainer for **House of Legacy (吾今有世家)** on Steam (Windows 64-bit).
It runs as a [BepInEx](https://github.com/BepInEx/BepInEx) plugin and edits values in the **running** game — no save reloading needed.

เทรนเนอร์ในเกมสำหรับ **House of Legacy (吾今有世家)** แก้ค่าในเกมที่กำลังรันได้ทันที ไม่ต้องโหลด save ใหม่ — [คู่มือภาษาไทยด้านล่าง](#ภาษาไทย)

## Features

- **Player:** Coin, Gold, Slaves, Renown — set / add / subtract
- **Characters** (Lineage, Spouse, Retainer): Age, Health, Mood, Renown, Charisma, Luck, Writing, Might, Business, Arts, Cunning
- One-click helpers: Health + Mood = 100, set all skills
- Rename the family, yourself and any other character
- Thai / English switch (remembered between sessions)

## Install

1. Download [`PK Trainer - House of Legacy v1.3.0.zip`](releases/PK%20Trainer%20-%20House%20of%20Legacy%20v1.3.0.zip) (in the `releases/` folder; also on the **Releases** page if published) and **extract it**.
2. Close the game, then double-click **`Install.bat`**.
   It finds your Steam game folder automatically (or asks you to pick it) and installs BepInEx + the plugin
   (or just the plugin if BepInEx 5 is already there).
3. Start the game, load a save, then press **Insert** / **F9** or click **PK Trainer [Ins]** at the bottom-left.

Uninstall: double-click `Uninstall.bat`.

> Tested with game version **0.9.03**. A game update can move the data and break the trainer.
> Back up your saves first: `%USERPROFILE%\AppData\LocalLow\S3Studio\House of Legacy`. Single-player use only.

## Build from source

Requires the .NET SDK and a copy of the game with BepInEx 5 installed (the plugin references the game's `Assembly-CSharp.dll`).

```powershell
dotnet build src/PKTrainer/PKTrainer.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\吾今有世家"

# build + create the release zip in dist\
powershell -ExecutionPolicy Bypass -File tools\package.ps1 -GameDir "<game folder>"
```

## How it works

The game keeps all state in static fields of `Mainload` (strings in lists). The plugin edits those fields directly and the UI refreshes on its own.
Known field layout (indices into the game's lists) is in `src/PKTrainer/Plugin.cs`. Notes: Coin is `CGNum[0]` (overflow in `CGNum[2]`), Gold `CGNum[1]`,
Slaves `Mainload.NuLiNum`, Renown via `FormulaData.FamilyLvSW`.

## License

MIT for this project's code — see [LICENSE](LICENSE). Bundled third-party components: [THIRD-PARTY.md](THIRD-PARTY.md).
Unofficial fan-made tool; not affiliated with the game's developers.

---

## ภาษาไทย

**ติดตั้ง**
1. ดาวน์โหลดไฟล์ zip จากโฟลเดอร์ [`releases/`](releases/PK%20Trainer%20-%20House%20of%20Legacy%20v1.3.0.zip) (หรือหน้า **Releases**) แล้ว **แตกไฟล์**
2. ปิดเกมให้หมด แล้วดับเบิลคลิก **`Install.bat`** (หาโฟลเดอร์เกมจาก Steam ให้เอง ถ้าหาไม่เจอจะให้เลือกเอง)
3. เปิดเกม โหลด save แล้วกด **Insert** / **F9** หรือคลิกปุ่ม **PK Trainer [Ins]** มุมซ้ายล่างของจอ

**ถอนการติดตั้ง:** ดับเบิลคลิก `Uninstall.bat`

**ข้อควรรู้:** ทดสอบกับเกมเวอร์ชัน 0.9.03 · สำรอง save ก่อนใช้ · ใช้กับโหมดเล่นคนเดียวเท่านั้น
