package com.xdripwidget.android

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.util.Log
import android.widget.RemoteViews
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale

class WidgetUpdateWorker(
    private val context: Context,
    params: WorkerParameters
) : Worker(context, params) {

    override fun doWork(): Result {
        Log.d(TAG, "doWork() triggered")
        val serverUrl = WidgetPreferences.getServerUrl(context)
        val apiSecret = WidgetPreferences.getApiSecret(context)

        try {
            var endpoint = "$serverUrl/api/v1/current"
            if (apiSecret.isNotEmpty()) {
                endpoint += "?token=$apiSecret"
            }

            val url = URL(endpoint)
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "GET"
            connection.connectTimeout = 8000
            connection.readTimeout = 8000
            connection.setRequestProperty("Accept", "application/json")
            if (apiSecret.isNotEmpty()) {
                connection.setRequestProperty("api-secret", apiSecret)
            }

            val responseCode = connection.responseCode
            if (responseCode == 200) {
                val stream = connection.inputStream
                val jsonText = stream.bufferedReader().use { it.readText() }
                connection.disconnect()

                val json = JSONObject(jsonText)
                updateWidgetViews(json, null)
                return Result.success()
            } else if (responseCode == 204) {
                updateWidgetViews(null, "Нет данных")
                return Result.success()
            } else {
                updateWidgetViews(null, "HTTP $responseCode")
                return Result.retry()
            }
        } catch (e: Exception) {
            Log.e(TAG, "Fetch error: ${e.message}", e)
            updateWidgetViews(null, "Ошибка сети")
            return Result.retry()
        }
    }

    private fun updateWidgetViews(json: JSONObject?, errorMsg: String?) {
        val appWidgetManager = AppWidgetManager.getInstance(context)
        val componentName = ComponentName(context, xDripWidgetProvider::class.java)
        val appWidgetIds = appWidgetManager.getAppWidgetIds(componentName)

        for (appWidgetId in appWidgetIds) {
            val views = RemoteViews(context.packageName, R.layout.widget_layout_4x1)

            if (errorMsg != null || json == null) {
                views.setTextViewText(R.id.tv_glucose, "--.-")
                views.setTextViewText(R.id.tv_delta, errorMsg ?: "Ошибка")
                views.setTextViewText(R.id.tv_time, "")
                val emptyBat = createBatteryBitmap(-1, true)
                views.setImageViewBitmap(R.id.iv_battery, emptyBat)
            } else {
                val mmol = json.optDouble("mmol", 0.0)
                val direction = json.optString("direction", "Unknown")
                val deltaStr = json.optString("delta", "?")
                val battery = json.optInt("battery", -1)
                val minutesAgo = json.optInt("minutes_ago", 0)

                val arrow = trendArrows[direction] ?: "?"
                val stale = minutesAgo > 5

                // Glucose text & color
                val colorHex = getGlucoseColorHex(mmol, stale)
                views.setTextViewText(R.id.tv_glucose, String.format(Locale.US, "%.1f %s", mmol, arrow))
                views.setTextColor(R.id.tv_glucose, Color.parseColor(colorHex))

                // Delta & Stale Icon
                val iconSymbol = if (minutesAgo > 5) "🔄" else "Δ"
                views.setTextViewText(R.id.tv_delta, "$iconSymbol $deltaStr")

                // Time ago
                val timeStr = formatTimeAgo(minutesAgo)
                views.setTextViewText(R.id.tv_time, timeStr)

                // Battery Bar Bitmap (scaled 1.5x smaller on both sides)
                val batBitmap = createBatteryBitmap(battery, stale)
                views.setImageViewBitmap(R.id.iv_battery, batBitmap)

                // Check 3-cycle alarms
                checkAlarms(mmol, minutesAgo)
            }

            // Click on widget triggers manual refresh / snooze
            val refreshIntent = Intent(context, xDripWidgetProvider::class.java).apply {
                action = xDripWidgetProvider.ACTION_MANUAL_REFRESH
            }
            val pendingIntent = PendingIntent.getBroadcast(
                context, 0, refreshIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.widget_container, pendingIntent)

            // Dynamic transparency tint
            val transparency = WidgetPreferences.getTransparency(context)
            val alpha = (255 * (100 - transparency) / 100).coerceIn(40, 255)
            val bgColor = Color.argb(alpha, 20, 20, 30)
            views.setInt(R.id.widget_container, "setBackgroundColor", bgColor)

            appWidgetManager.updateAppWidget(appWidgetId, views)
        }
    }

    private fun checkAlarms(mmol: Double, minutesAgo: Int) {
        if (!WidgetPreferences.isAlarmEnabled(context)) return
        if (minutesAgo > 5 || mmol <= 0.0) return

        val lowThreshold = WidgetPreferences.getLowThreshold(context)
        val highThreshold = WidgetPreferences.getHighThreshold(context)
        val now = System.currentTimeMillis()

        val isLow = mmol < lowThreshold
        val isHigh = mmol > highThreshold

        if (!isLow && !isHigh) {
            // Normal range: reset alarm cycle counter and snooze state
            WidgetPreferences.resetAlarmState(context)
            SoundGenerator.stopMelody()
            return
        }

        // Check if currently snoozed by user tap or auto-snooze
        val snoozedUntil = WidgetPreferences.getSnoozedUntil(context)
        if (now < snoozedUntil) {
            Log.d(TAG, "Alarm is snoozed until $snoozedUntil")
            return
        }

        val lowSnoozeMs = WidgetPreferences.getLowSnoozeMinutes(context) * 60_000L
        val highSnoozeMs = WidgetPreferences.getHighSnoozeMinutes(context) * 60_000L
        val snoozeMs = if (isLow) lowSnoozeMs else highSnoozeMs

        val cycleCount = WidgetPreferences.getAlarmCycleCount(context)
        val lastCycleTime = WidgetPreferences.getLastCycleTime(context)

        // 1-minute interval between 1-minute alarm cycles
        if (now - lastCycleTime >= 60_000L) {
            if (cycleCount < 3) {
                val newCount = cycleCount + 1
                WidgetPreferences.setAlarmCycleCount(context, newCount)
                WidgetPreferences.setLastCycleTime(context, now)
                if (isLow) {
                    WidgetPreferences.setLastLowAlarmTime(context, now)
                } else {
                    WidgetPreferences.setLastHighAlarmTime(context, now)
                }

                val melody = WidgetPreferences.getAlarmMelody(context)
                val volume = WidgetPreferences.getAlarmVolume(context)

                Log.d(TAG, "Triggering alarm cycle $newCount/3 for mmol=$mmol")
                SoundGenerator.playAlarmCycleAsync(melody, volume, 60_000L)
            } else {
                // 3 cycles finished without user tap -> auto-snooze for 30m / 60m!
                Log.d(TAG, "Completed 3 alarm cycles without user tap. Auto-snoozing for $snoozeMs ms.")
                SoundGenerator.stopMelody()
                WidgetPreferences.setSnoozedUntil(context, now + snoozeMs)
                WidgetPreferences.setAlarmCycleCount(context, 0)
            }
        }
    }

    private fun getGlucoseColorHex(mmol: Double, stale: Boolean): String {
        if (stale) return "#7f8c8d"
        if (mmol <= 3.3) return "#e74c3c" // Heavy Hypo (Red)
        if (mmol < 3.9) return "#f39c12"  // Mild Hypo (Yellow)
        if (mmol <= 7.8) return "#27ae60" // Target Normal (Green)
        if (mmol < 10.0) return "#f39c12" // Mild Hyper (Yellow)
        return "#e57373"                  // Soft Red (Hyper >= 10.0)
    }

    private fun formatTimeAgo(minutesAgo: Int): String {
        return when {
            minutesAgo < 60 -> "$minutesAgo м назад"
            minutesAgo < 1440 -> "${minutesAgo / 60} ч назад"
            else -> "${minutesAgo / 1440} д назад"
        }
    }

    private fun createBatteryBitmap(pct: Int, stale: Boolean): Bitmap {
        val width = 186
        val height = 44
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)

        if (pct < 0) return bitmap

        canvas.scale(width / 140f, height / 34f)

        val paint = Paint(Paint.ANTI_ALIAS_FLAG)

        val bColor = when {
            stale -> Color.parseColor("#7f8c8d")
            pct <= 20 -> Color.parseColor("#e74c3c")
            pct <= 50 -> Color.parseColor("#f39c12")
            else -> Color.parseColor("#27ae60")
        }

        // Frame
        val frameRect = RectF(2f, 4f, 64f, 30f)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2.5f
        paint.color = Color.parseColor("#bdc3c7")
        canvas.drawRoundRect(frameRect, 4.5f, 4.5f, paint)

        // Tip
        val tipRect = RectF(64f, 10f, 68f, 24f)
        paint.style = Paint.Style.FILL
        canvas.drawRoundRect(tipRect, 2f, 2f, paint)

        // Fill
        if (pct > 0) {
            val fillWidth = (56f * (pct.coerceIn(0, 100) / 100f))
            val fillRect = RectF(4.5f, 6.5f, 4.5f + fillWidth, 27.5f)
            paint.color = bColor
            canvas.drawRect(fillRect, paint)
        }

        // Percentage text
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#bdc3c7")
        paint.textSize = 17f
        paint.typeface = Typeface.DEFAULT_BOLD
        canvas.drawText("$pct%", 75f, 23.5f, paint)

        return bitmap
    }

    companion object {
        private const val TAG = "WidgetUpdateWorker"

        private val trendArrows = mapOf(
            "DoubleUp" to "⇈",
            "SingleUp" to "↑",
            "FortyFiveUp" to "↗",
            "Flat" to "→",
            "FortyFiveDown" to "↘",
            "SingleDown" to "↓",
            "DoubleDown" to "⇊",
            "Unknown" to "?"
        )
    }
}
