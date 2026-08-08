package com.xdripwidget.android

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.util.Log
import android.widget.RemoteViews
import android.widget.Toast
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

class xDripWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetIds: IntArray
    ) {
        Log.d(TAG, "onUpdate triggered for ${appWidgetIds.size} widgets")
        enqueueOneTimeUpdate(context)
        schedulePeriodicWork(context)
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        if (intent.action == ACTION_MANUAL_REFRESH) {
            Log.d(TAG, "Manual refresh click received")

            // Stop alarm sound immediately on tap
            SoundGenerator.stopMelody()

            val now = System.currentTimeMillis()
            val cycleCount = WidgetPreferences.getAlarmCycleCount(context)
            val snoozedUntil = WidgetPreferences.getSnoozedUntil(context)

            // If an unacknowledged alarm cycle was in progress or active
            if (cycleCount > 0 || now < snoozedUntil) {
                val lowSnoozeMin = WidgetPreferences.getLowSnoozeMinutes(context)
                val highSnoozeMin = WidgetPreferences.getHighSnoozeMinutes(context)

                // Snooze based on low or high duration
                val snoozeMin = if (WidgetPreferences.getLastLowAlarmTime(context) > 0) lowSnoozeMin else highSnoozeMin
                val newSnoozeUntil = now + (snoozeMin * 60_000L)
                WidgetPreferences.setSnoozedUntil(context, newSnoozeUntil)
                WidgetPreferences.setAlarmCycleCount(context, 0)

                Toast.makeText(context, "Тревога отложена на $snoozeMin мин", Toast.LENGTH_SHORT).show()
            }

            // Immediate visual feedback
            showRefreshingState(context)
            enqueueOneTimeUpdate(context)
        }
    }

    private fun showRefreshingState(context: Context) {
        try {
            val appWidgetManager = AppWidgetManager.getInstance(context)
            val componentName = ComponentName(context, xDripWidgetProvider::class.java)
            val appWidgetIds = appWidgetManager.getAppWidgetIds(componentName)

            for (appWidgetId in appWidgetIds) {
                val views = RemoteViews(context.packageName, R.layout.widget_layout_4x1)
                views.setTextViewText(R.id.tv_time, "Обновление...")
                appWidgetManager.partiallyUpdateAppWidget(appWidgetId, views)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error showing refreshing state: ${e.message}")
        }
    }

    override fun onEnabled(context: Context) {
        super.onEnabled(context)
        schedulePeriodicWork(context)
    }

    override fun onDisabled(context: Context) {
        super.onDisabled(context)
        WorkManager.getInstance(context).cancelUniqueWork(WORK_TAG)
    }

    companion object {
        private const val TAG = "xDripWidgetProvider"
        const val ACTION_MANUAL_REFRESH = "com.xdripwidget.android.MANUAL_REFRESH"
        private const val WORK_TAG = "xDripWidgetPeriodicWork"

        fun schedulePeriodicWork(context: Context) {
            val interval = WidgetPreferences.getRefreshInterval(context).coerceAtLeast(15).toLong()
            val periodicRequest = PeriodicWorkRequestBuilder<WidgetUpdateWorker>(
                interval, TimeUnit.MINUTES
            ).build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                WORK_TAG,
                ExistingPeriodicWorkPolicy.UPDATE,
                periodicRequest
            )
        }

        fun enqueueOneTimeUpdate(context: Context) {
            val oneTimeRequest = OneTimeWorkRequestBuilder<WidgetUpdateWorker>().build()
            WorkManager.getInstance(context).enqueue(oneTimeRequest)
        }
    }
}
