# План реализации v1.7.3: Увеличение иконки батарейки на Android в 2 раза

## 1. Увеличение иконки батарейки на Android
- **Файл**: `WidgetUpdateWorker.kt`
- В функции `createBatteryBitmap`:
  - Увеличить размер `Bitmap` в **2 раза** по сравнению с предыдущим маленьким размером (93x22 px -> **186x44 px**).
  - Настроить масштабирование холста `canvas.scale(186f / 140f, 44f / 34f)` для сохранения высокой четкости всех линий, текста и градиента заряда.

## 2. Сборка и Git
- Выполнить сборку Android APK (`./gradlew assembleDebug`).
- Выполнить сборку Windows EXE (`python -m PyInstaller xDripWidget.spec`).
- Создать commit и сделать `git push`.
