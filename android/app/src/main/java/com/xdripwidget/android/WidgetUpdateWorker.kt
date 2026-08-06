package com.xdripwidget.android

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Color
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
                views.setTextViewText(R.id.tv_battery, "")
            } else {
                val mmol = json.optDouble("mmol", 0.0)
                val direction = json.optString("direction", "Unknown")
                val deltaStr = json.optString("delta", "?")
                val battery = json.optInt("battery", -1)
                val minutesAgo = json.optInt("minutes_ago", 0)

                val arrow = trendArrows[direction] ?: "?"
                val stale = minutesAgo > 15

                // Glucose text & color
                val colorHex = getGlucoseColorHex(mmol, stale)
                views.setTextViewText(R.id.tv_glucose, String.format(Locale.US, "%.1f %s", mmol, arrow))
                views.setTextColor(R.id.tv_glucose, Color.parseColor(colorHex))

                // Delta
                views.setTextViewText(R.id.tv_delta, "Δ $deltaStr")

                // Time ago
                val timeStr = if (minutesAgo < 60) "${minutesAgo}м назад" else ">1ч назад"
                views.setTextViewText(R.id.tv_time, timeStr)

                // Battery
                val batStr = if (battery >= 0) "🔋 $battery%" else ""
                views.setTextViewText(R.id.tv_battery, batStr)
            }

            // Click on widget triggers manual refresh
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

    private fun getGlucoseColorHex(mmol: Double, stale: Boolean): String {
        if (stale) return "#7f8c8d"
        if (mmol < 3.3 || mmol > 11.0) return "#e74c3c" // Red
        if (mmol < 3.9 || mmol > 9.0) return "#f39c12"  // Yellow
        return "#27ae60"                               // Green
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
