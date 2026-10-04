package com.xdripwidget.android

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.DashPathEffect
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.util.Log
import android.util.TypedValue
import android.view.View
import android.widget.RemoteViews
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONArray
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
        val success = executeFetchAndUpdate(context)
        return if (success) Result.success() else Result.failure()
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

        fun executeFetchAndUpdate(context: Context): Boolean {
            val serverUrl = WidgetPreferences.getServerUrl(context)
            val apiSecret = WidgetPreferences.getApiSecret(context)

            var currentJson: JSONObject? = null
            val historyList = mutableListOf<JSONObject>()

            try {
                // 1. Fetch /api/v1/current
                var currEndpoint = "$serverUrl/api/v1/current"
                if (apiSecret.isNotEmpty()) {
                    currEndpoint += "?token=$apiSecret"
                }

                val currUrl = URL(currEndpoint)
                val currConn = currUrl.openConnection() as HttpURLConnection
                currConn.requestMethod = "GET"
                currConn.connectTimeout = 8000
                currConn.readTimeout = 8000
                currConn.setRequestProperty("Accept", "application/json")
                if (apiSecret.isNotEmpty()) {
                    currConn.setRequestProperty("api-secret", apiSecret)
                }

                val responseCode = currConn.responseCode
                if (responseCode == 200) {
                    val stream = currConn.inputStream
                    val jsonText = stream.bufferedReader().use { it.readText() }
                    currConn.disconnect()
                    currentJson = JSONObject(jsonText)
                } else if (responseCode == 204) {
                    currConn.disconnect()
                    updateWidgetViews(context, null, emptyList(), "Нет данных")
                    return true
                } else {
                    currConn.disconnect()
                    showErrorOnWidget(context, "HTTP $responseCode")
                    return false
                }

                // 2. Fetch /api/v1/history?hours=4
                try {
                    var histEndpoint = "$serverUrl/api/v1/history?hours=4"
                    if (apiSecret.isNotEmpty()) {
                        histEndpoint += "&token=$apiSecret"
                    }
                    val histUrl = URL(histEndpoint)
                    val histConn = histUrl.openConnection() as HttpURLConnection
                    histConn.requestMethod = "GET"
                    histConn.connectTimeout = 6000
                    histConn.readTimeout = 6000
                    histConn.setRequestProperty("Accept", "application/json")
                    if (apiSecret.isNotEmpty()) {
                        histConn.setRequestProperty("api-secret", apiSecret)
                    }

                    if (histConn.responseCode == 200) {
                        val histText = histConn.inputStream.bufferedReader().use { it.readText() }
                        val array = JSONArray(histText)
                        for (i in 0 until array.length()) {
                            historyList.add(array.getJSONObject(i))
                        }
                    }
                    histConn.disconnect()
                } catch (eHist: Exception) {
                    Log.w(TAG, "History fetch failed: ${eHist.message}")
                }

                updateWidgetViews(context, currentJson, historyList, null)
                return true

            } catch (e: Exception) {
                Log.e(TAG, "Fetch error: ${e.message}", e)
                showErrorOnWidget(context, "Ошибка сети")
                return false
            }
        }

        fun showErrorOnWidget(context: Context, errorMsg: String) {
            try {
                val appWidgetManager = AppWidgetManager.getInstance(context)
                val componentName = ComponentName(context, xDripWidgetProvider::class.java)
                val appWidgetIds = appWidgetManager.getAppWidgetIds(componentName)

                val staleColor = Color.parseColor("#94A3B8")
                for (appWidgetId in appWidgetIds) {
                    val views = RemoteViews(context.packageName, R.layout.widget_layout_4x1)
                    views.setTextViewText(R.id.tv_time, "⚠ $errorMsg")
                    views.setTextColor(R.id.tv_glucose, staleColor)
                    views.setTextColor(R.id.tv_delta, staleColor)

                    val refreshIntent = Intent(context, xDripWidgetProvider::class.java).apply {
                        action = xDripWidgetProvider.ACTION_MANUAL_REFRESH
                    }
                    val pendingIntent = PendingIntent.getBroadcast(
                        context, 0, refreshIntent,
                        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                    )
                    views.setOnClickPendingIntent(R.id.widget_container, pendingIntent)
                    views.setOnClickPendingIntent(R.id.top_block, pendingIntent)

                    appWidgetManager.partiallyUpdateAppWidget(appWidgetId, views)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error showing error on widget: ${e.message}")
            }
        }

        fun updateWidgetViews(
            context: Context,
            json: JSONObject?,
            historyList: List<JSONObject>,
            errorMsg: String?
        ) {
            val appWidgetManager = AppWidgetManager.getInstance(context)
            val componentName = ComponentName(context, xDripWidgetProvider::class.java)
            val appWidgetIds = appWidgetManager.getAppWidgetIds(componentName)

            for (appWidgetId in appWidgetIds) {
                val views = RemoteViews(context.packageName, R.layout.widget_layout_4x1)
                val options = appWidgetManager.getAppWidgetOptions(appWidgetId)
                val minWidth = options.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 150)
                val minHeight = options.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 50)

                val showGraph = minHeight >= 90 && historyList.size >= 2

                if (errorMsg != null || json == null) {
                    views.setTextViewText(R.id.tv_glucose, "--.-")
                    views.setTextViewText(R.id.tv_delta, errorMsg ?: "Ошибка")
                    views.setTextViewText(R.id.tv_time, "")
                    val emptyBat = createBatteryBitmap(context, -1, true, 1.0f)
                    views.setImageViewBitmap(R.id.iv_battery, emptyBat)
                    views.setViewVisibility(R.id.iv_sparkline, View.GONE)
                } else {
                    val mmol = json.optDouble("mmol", 0.0)
                    val direction = json.optString("direction", "Unknown")
                    val deltaStr = json.optString("delta", "?")
                    val battery = json.optInt("battery", -1)
                    val minutesAgo = json.optInt("minutes_ago", 0)

                    val arrow = trendArrows[direction] ?: "?"
                    val stale = minutesAgo > 5

                    // Responsive typography & scaling
                    val glucoseSizeSp: Float
                    val deltaSizeSp: Float
                    val timeSizeSp: Float
                    val batteryScale: Float

                    if (showGraph) {
                        glucoseSizeSp = 28f
                        deltaSizeSp = 13f
                        timeSizeSp = 11f
                        batteryScale = 1.2f

                        // Draw 4-Hour Sparkline Graph
                        val graphHeightDp = (minHeight - 48).coerceIn(45, 400)
                        val graphWidthDp = minWidth.coerceAtLeast(140)
                        val sparklineBitmap = createSparklineBitmap(context, historyList, graphWidthDp, graphHeightDp)
                        views.setImageViewBitmap(R.id.iv_sparkline, sparklineBitmap)
                        views.setViewVisibility(R.id.iv_sparkline, View.VISIBLE)
                    } else {
                        views.setViewVisibility(R.id.iv_sparkline, View.GONE)
                        if (minWidth >= 220) {
                            glucoseSizeSp = 26f
                            deltaSizeSp = 13f
                            timeSizeSp = 11f
                            batteryScale = 1.15f
                        } else {
                            glucoseSizeSp = 20f
                            deltaSizeSp = 12f
                            timeSizeSp = 10f
                            batteryScale = 1.0f
                        }
                    }

                    views.setTextViewTextSize(R.id.tv_glucose, TypedValue.COMPLEX_UNIT_SP, glucoseSizeSp)
                    views.setTextViewTextSize(R.id.tv_delta, TypedValue.COMPLEX_UNIT_SP, deltaSizeSp)
                    views.setTextViewTextSize(R.id.tv_time, TypedValue.COMPLEX_UNIT_SP, timeSizeSp)

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

                    // Battery Bar Bitmap
                    val batBitmap = createBatteryBitmap(context, battery, stale, batteryScale)
                    views.setImageViewBitmap(R.id.iv_battery, batBitmap)

                    // Check 3-cycle alarms
                    checkAlarms(context, mmol, minutesAgo)
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
                views.setOnClickPendingIntent(R.id.top_block, pendingIntent)

                // Dynamic transparency tint
                val transparency = WidgetPreferences.getTransparency(context)
                val alpha = (255 * (100 - transparency) / 100).coerceIn(40, 255)
                val bgColor = Color.argb(alpha, 20, 20, 30)
                views.setInt(R.id.widget_container, "setBackgroundColor", bgColor)

                appWidgetManager.updateAppWidget(appWidgetId, views)
            }
        }

        private fun checkAlarms(context: Context, mmol: Double, minutesAgo: Int) {
            if (!WidgetPreferences.isAlarmEnabled(context)) return
            if (minutesAgo > 5 || mmol <= 0.0) return

            val lowThreshold = WidgetPreferences.getLowThreshold(context)
            val highThreshold = WidgetPreferences.getHighThreshold(context)
            val now = System.currentTimeMillis()

            val isLow = mmol < lowThreshold
            val isHigh = mmol > highThreshold

            if (!isLow && !isHigh) {
                WidgetPreferences.resetAlarmState(context)
                SoundGenerator.stopMelody()
                return
            }

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
                    Log.d(TAG, "Completed 3 alarm cycles without user tap. Auto-snoozing for $snoozeMs ms.")
                    SoundGenerator.stopMelody()
                    WidgetPreferences.setSnoozedUntil(context, now + snoozeMs)
                    WidgetPreferences.setAlarmCycleCount(context, 0)
                }
            }
        }

        fun getGlucoseColorHex(mmol: Double, stale: Boolean): String {
            if (stale || mmol <= 0.0) return "#94A3B8"
            if (mmol < 3.0) return "#EF4444"
            if (mmol < 3.9) return "#F59E0B"
            if (mmol <= 7.8) return "#4ADE80"
            if (mmol <= 10.0) return "#10B981"
            if (mmol <= 13.9) return "#F59E0B"
            return "#EF4444"
        }

        private fun formatTimeAgo(minutesAgo: Int): String {
            return when {
                minutesAgo < 60 -> "$minutesAgo мин"
                minutesAgo < 1440 -> "${minutesAgo / 60} ч"
                else -> "${minutesAgo / 1440} д"
            }
        }

        private fun createBatteryBitmap(context: Context, pct: Int, stale: Boolean, scale: Float = 1.0f): Bitmap {
            val density = context.resources.displayMetrics.density.coerceAtLeast(1.0f)
            val targetWidthDp = 50f * scale
            val targetHeightDp = 14f * scale
            val width = (targetWidthDp * density).toInt().coerceAtLeast(40)
            val height = (targetHeightDp * density).toInt().coerceAtLeast(12)
            val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)

            if (pct < 0) return bitmap

            canvas.scale(width / 90f, height / 26f)

            val paint = Paint(Paint.ANTI_ALIAS_FLAG)

            val bColor = when {
                stale -> Color.parseColor("#94A3B8")
                pct <= 20 -> Color.parseColor("#EF4444")
                pct <= 50 -> Color.parseColor("#F59E0B")
                else -> Color.parseColor("#10B981")
            }

            // Frame
            val frameRect = RectF(1.5f, 3f, 44f, 23f)
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 2.2f
            paint.color = Color.parseColor("#334155")
            canvas.drawRoundRect(frameRect, 3.5f, 3.5f, paint)

            // Tip
            val tipRect = RectF(44f, 8f, 47.5f, 18f)
            paint.style = Paint.Style.FILL
            canvas.drawRoundRect(tipRect, 1.5f, 1.5f, paint)

            // Fill
            if (pct > 0) {
                val fillWidth = 37f * (pct.coerceIn(0, 100) / 100f)
                val fillRect = RectF(3.5f, 5f, 3.5f + fillWidth, 21f)
                paint.color = bColor
                canvas.drawRect(fillRect, paint)
            }

            // Percentage text
            paint.style = Paint.Style.FILL
            paint.color = Color.parseColor("#94A3B8")
            paint.textSize = 17f
            paint.typeface = Typeface.DEFAULT_BOLD
            canvas.drawText("$pct%", 52f, 18.5f, paint)

            return bitmap
        }

        private fun createSparklineBitmap(
            context: Context,
            history: List<JSONObject>,
            widthDp: Int,
            heightDp: Int
        ): Bitmap {
            val density = context.resources.displayMetrics.density.coerceAtLeast(1.0f)
            val w = (widthDp * density).toInt().coerceAtLeast(140)
            val h = (heightDp * density).toInt().coerceAtLeast(45)

            val bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)

            if (history.size < 2) return bitmap

            val gx = 6f * density
            val gy = 4f * density
            val gw = w - 12f * density
            val gh = h - 20f * density

            var minVal = 2.5
            var maxVal = 14.0
            for (item in history) {
                val v = item.optDouble("mmol", 5.5)
                if (v < minVal) minVal = (v - 0.5).coerceAtLeast(1.5)
                if (v > maxVal) maxVal = (v + 1.0).coerceAtMost(22.0)
            }
            val valRange = (maxVal - minVal).coerceAtLeast(0.1)

            fun valToY(v: Double): Float {
                val ratio = (v - minVal) / valRange
                return (gy + gh - (ratio * gh)).toFloat()
            }

            val yLo = valToY(3.9)
            val yHi = valToY(7.8)

            // Target corridor (TIR 3.9 - 7.8)
            val topCorridor = yHi.coerceIn(gy, gy + gh)
            val botCorridor = yLo.coerceIn(gy, gy + gh)
            if (botCorridor > topCorridor) {
                val corridorPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    style = Paint.Style.FILL
                    color = Color.argb(35, 74, 222, 128)
                }
                canvas.drawRect(gx, topCorridor, gx + gw, botCorridor, corridorPaint)
            }

            // Dashed lines for 3.9 and 7.8
            val dashPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = 1f * density
                color = Color.argb(90, 74, 222, 128)
                pathEffect = DashPathEffect(floatArrayOf(5f * density, 5f * density), 0f)
            }
            if (yLo in gy..(gy + gh)) {
                canvas.drawLine(gx, yLo, gx + gw, yLo, dashPaint)
            }
            if (yHi in gy..(gy + gh)) {
                canvas.drawLine(gx, yHi, gx + gw, yHi, dashPaint)
            }

            // Timeline points
            val tStart = history.first().optLong("timestamp", 0L)
            val tEnd = history.last().optLong("timestamp", tStart)
            val tSpan = (tEnd - tStart).coerceAtLeast(1L).toDouble()

            val points = mutableListOf<Triple<Float, Float, Int>>()
            for (item in history) {
                val ts = item.optLong("timestamp", tStart)
                val v = item.optDouble("mmol", 5.5)
                val px = (gx + ((ts - tStart) / tSpan * gw)).toFloat()
                val py = valToY(v)
                val dotColor = Color.parseColor(getGlucoseColorHex(v, false))
                points.add(Triple(px, py, dotColor))
            }

            // Line connecting points
            val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = 1.5f * density
                color = Color.argb(80, 148, 163, 184)
            }
            for (i in 0 until points.size - 1) {
                val p1 = points[i]
                val p2 = points[i + 1]
                canvas.drawLine(p1.first, p1.second, p2.first, p2.second, linePaint)
            }

            // Point dots
            val dotPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.FILL
            }
            for (p in points) {
                dotPaint.color = p.third
                canvas.drawCircle(p.first, p.second, 2.5f * density, dotPaint)
            }

            // Axis line
            val axisY = gy + gh + 3f * density
            val axisPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = 1f * density
                color = Color.argb(60, 51, 65, 85)
            }
            canvas.drawLine(gx, axisY, gx + gw, axisY, axisPaint)

            // Axis labels
            val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = Color.argb(180, 148, 163, 184)
                textSize = 9f * density
            }
            canvas.drawText("-4ч", gx, axisY + 11f * density, textPaint)
            val midX = gx + gw / 2f
            val midText = "-2ч"
            val midW = textPaint.measureText(midText)
            canvas.drawText(midText, midX - (midW / 2f), axisY + 11f * density, textPaint)
            val nowText = "сейчас"
            val nowW = textPaint.measureText(nowText)
            canvas.drawText(nowText, gx + gw - nowW, axisY + 11f * density, textPaint)

            return bitmap
        }
    }
}
