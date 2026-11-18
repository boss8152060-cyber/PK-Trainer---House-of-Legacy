PK Trainer - House of Legacy  (v1.2.1)
=======================================

[ภาษาไทย]
เทรนเนอร์สำหรับเกม House of Legacy (吾今有世家) เวอร์ชัน Steam / Windows 64-bit
แก้ค่าในเกมที่กำลังรันได้ทันที ไม่ต้องโหลด save ใหม่

ฟีเจอร์
- ผู้เล่น: เหรียญ, ทอง, ทาส, ชื่อเสียง (ตั้งค่า / เพิ่ม / ลด)
- ตัวละคร (สายตระกูล, คู่สมรส, ผู้ติดตาม): อายุ, สุขภาพ, อารมณ์, ชื่อเสียง, เสน่ห์, โชคลาภ,
  อักษร, ยุทธ์, การค้า, ศิลป์, กลยุทธ์
- เปลี่ยนชื่อตระกูล ชื่อตัวเอง และชื่อตัวละครอื่น
- ค้นหาคน: กรอง เพศ/อายุ/ชนชั้น ให้คะแนนจากค่าพลังที่ปรับน้ำหนักได้ (จ้างงาน/แต่งงาน ฯลฯ)
- สลับภาษา ไทย / EN ได้ในหน้าต่าง

วิธีติดตั้ง (ง่ายที่สุด)
1. แตกไฟล์ zip ออกมาก่อน (ห้ามรันจากในไฟล์ zip โดยตรง)
2. ปิดเกมให้หมด แล้วดับเบิลคลิก  Install.bat
3. ตัวติดตั้งจะหาโฟลเดอร์เกมจาก Steam ให้เอง (ถ้าหาไม่เจอจะให้เลือกโฟลเดอร์)
   - ถ้ายังไม่มี BepInEx จะติดตั้ง BepInEx + เทรนเนอร์ให้
   - ถ้ามี BepInEx 5 อยู่แล้ว จะติดตั้งเฉพาะเทรนเนอร์
4. เปิดเกม โหลด save เข้าหน้าเมือง

วิธีติดตั้งเอง (ถ้า Install.bat ใช้ไม่ได้)
- เปิด Steam > คลิกขวาเกม > Manage > Browse local files
- คัดลอก "ทุกอย่างในโฟลเดอร์ files" ไปวางทับในโฟลเดอร์เกม
  (ถ้ามี BepInEx 5 อยู่แล้ว คัดลอกเฉพาะ files\BepInEx\plugins\PKTrainer)

วิธีใช้
- กด Insert หรือ F9 หรือคลิกปุ่ม "PK Trainer [Ins]" มุมซ้ายล่างของจอ เพื่อเปิด/ปิดหน้าต่าง
- ลากหัวหน้าต่างเพื่อย้ายตำแหน่ง
- แท็บ "ตัวละคร": เลือกชื่อทางซ้าย แก้ตัวเลขทางขวา แล้วกด "ใช้ค่า"

วิธีถอนการติดตั้ง
- ดับเบิลคลิก Uninstall.bat (ลบเฉพาะเทรนเนอร์ ไม่ลบ BepInEx)

ข้อควรรู้
- ทดสอบกับเกมเวอร์ชัน 0.9.03 ถ้าเกมอัปเดต ตำแหน่งข้อมูลอาจเปลี่ยนและเทรนเนอร์อาจใช้ไม่ได้
- สำรองไฟล์ save ก่อนใช้ (โฟลเดอร์ save: %USERPROFILE%\AppData\LocalLow\S3Studio\House of Legacy)
- สำหรับเล่นคนเดียวเท่านั้น ใช้ด้วยความรับผิดชอบของตัวเอง
- ถ้าหน้าต่างไม่ขึ้น ดูไฟล์ BepInEx\LogOutput.log ว่ามีบรรทัด "PK Trainer - House of Legacy loaded"
- ถ้า Windows Defender/SmartScreen เตือน: ตัวติดตั้งเป็นสคริปต์ PowerShell ธรรมดา เปิดอ่านได้ (Install.ps1)

-----------------------------------------------------------------------------------

[English]
In-game trainer for House of Legacy (Steam, Windows 64-bit). Edits values in the running
game instantly - no need to reload your save.

Features
- Player: Coin, Gold, Slaves, Renown (set / add / subtract)
- Characters (Lineage, Spouse, Retainer): Age, Health, Mood, Renown, Charisma, Luck,
  Writing, Might, Business, Arts, Cunning
- Rename family, yourself and any other character
- People Finder: filter by gender/age/class, score by adjustable stat weights (hire / marriage ...)
- Thai / EN language switch inside the window

Install (easiest)
1. Extract the zip first (do not run from inside the zip).
2. Close the game completely, then double-click  Install.bat
3. The installer finds your Steam game folder (or asks you to pick it).
   - No BepInEx yet: installs BepInEx + the trainer.
   - BepInEx 5 already present: installs the trainer only.
4. Start the game and load a save.

Manual install (if Install.bat does not work)
- Steam > right-click the game > Manage > Browse local files
- Copy everything inside the "files" folder into the game folder
  (if BepInEx 5 is already installed, copy only files\BepInEx\plugins\PKTrainer)

Use
- Press Insert or F9, or click the "PK Trainer [Ins]" button at the bottom-left of the screen.
- Drag the title bar to move the window.
- Characters tab: pick a name on the left, edit numbers on the right, click "Apply".

Uninstall
- Double-click Uninstall.bat (removes the trainer only, keeps BepInEx).

Notes
- Tested on game version 0.9.03. A game update may move the data and break the trainer.
- Back up your save first (%USERPROFILE%\AppData\LocalLow\S3Studio\House of Legacy).
- Single-player use only, at your own risk.
- If the window does not appear, check BepInEx\LogOutput.log for "PK Trainer - House of Legacy loaded".
- If SmartScreen warns: the installer is a plain PowerShell script you can read (Install.ps1).
