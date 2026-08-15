# План улучшений виджетов (Android & Desktop) v1.8.2

## 1. Android Widget (Опрос, Надежность, Рендеринг)
1. **Точный интервал обновления (1-5 мин) через AlarmManager**:
   - Реализовать планирование обновлений через `AlarmManager.setExactAndAllowWhileIdle()` / `setAndAllowWhileIdle()`.
   - Запускать `OneTimeWorkRequestBuilder<WidgetUpdateWorker>` при каждом срабатывании таймера или тапе.
   - Добавить обработку `ACTION_BOOT_COMPLETED` для перезапуска таймера после перезагрузки устройства.
2. **Сохранение скругления углов виджета**:
   - Использовать `RemoteViews.setInt(..., "setColorFilter", ...)` или переработать контейнер, чтобы при изменении прозрачности не терялись закругленные углы.
3. **Плотность экрана (DPI) для иконки батареи**:
   - Масштабировать `createBatteryBitmap` с учетом `context.resources.displayMetrics.density`.

## 2. Desktop Widget (`widget.py`)
1. **Корректный путь к `config.ini`**:
   - Использовать `QStandardPaths.writableLocation(AppConfigLocation)` с проверкой локального каталога для переносимости (portable mode).
2. **Адаптивный 4-часовой график (Sparkline)**:
   - Сделать ширину графика динамической (`self.width() - 20`).
   - Добавить подсветку целевого диапазона (коридор 3.9–7.8 ммоль/л) полупрозрачным зеленым цветом.
3. **Управление потоками `QThread`**:
   - Гарантировать корректную отмену и освобождение предыдущего рабочего потока `FetchWorker` перед запуском нового.

## 3. Сборка и проверка
1. Сборка Android APK (`./gradlew assembleDebug`).
2. Проверка запуска Desktop виджета.
3. Фиксация изменений в Git.
