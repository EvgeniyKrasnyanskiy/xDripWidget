package com.xdripwidget.android

import android.app.AlarmManager
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Build
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
        scheduleExactAlarm(context)
        schedulePeriodicWorkBackup(context)
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        val action = intent.action
        Log.d(TAG, "onReceive: action=$action")

        when (action) {
            ACTION_MANUAL_REFRESH -> {
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
                scheduleExactAlarm(context)
            }

            ACTION_ALARM_TICK, Intent.ACTION_BOOT_COMPLETED -> {
                Log.d(TAG, "Alarm tick / Boot completed -> updating widget")
                enqueueOneTimeUpdate(context)
                scheduleExactAlarm(context)
            }
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
        scheduleExactAlarm(context)
        schedulePeriodicWorkBackup(context)
    }

    override fun onDisabled(context: Context) {
        super.onDisabled(context)
        cancelAlarm(context)
        WorkManager.getInstance(context).cancelUniqueWork(WORK_TAG)
    }

    companion object {
        private const val TAG = "xDripWidgetProvider"
        const val ACTION_MANUAL_REFRESH = "com.xdripwidget.android.MANUAL_REFRESH"
        const val ACTION_ALARM_TICK = "com.xdripwidget.android.ALARM_TICK"
        private const val WORK_TAG = "xDripWidgetPeriodicWork"
        private const val ALARM_REQ_CODE = 1001

        fun scheduleExactAlarm(context: Context) {
            try {
                val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as? AlarmManager ?: return
                val intervalMinutes = WidgetPreferences.getRefreshInterval(context).coerceIn(1, 60)
                val triggerAt = System.currentTimeMillis() + (intervalMinutes * 60_000L)

                val intent = Intent(context, xDripWidgetProvider::class.java).apply {
                    action = ACTION_ALARM_TICK
                }
                val pendingIntent = PendingIntent.getBroadcast(
                    context,
                    ALARM_REQ_CODE,
                    intent,
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                )

                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                    alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAt, pendingIntent)
                } else {
                    alarmManager.set(AlarmManager.RTC_WAKEUP, triggerAt, pendingIntent)
                }
                Log.d(TAG, "Scheduled exact alarm for +$intervalMinutes min ($triggerAt)")
            } catch (e: Exception) {
                Log.e(TAG, "Error scheduling alarm: ${e.message}", e)
            }
        }

        fun cancelAlarm(context: Context) {
            try {
                val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as? AlarmManager ?: return
                val intent = Intent(context, xDripWidgetProvider::class.java).apply {
                    action = ACTION_ALARM_TICK
                }
                val pendingIntent = PendingIntent.getBroadcast(
                    context,
                    ALARM_REQ_CODE,
                    intent,
                    PendingIntent.FLAG_NO_CREATE or PendingIntent.FLAG_IMMUTABLE
                )
                if (pendingIntent != null) {
                    alarmManager.cancel(pendingIntent)
                    pendingIntent.cancel()
                    Log.d(TAG, "Alarm cancelled")
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error cancelling alarm: ${e.message}", e)
            }
        }

        fun schedulePeriodicWorkBackup(context: Context) {
            val periodicRequest = PeriodicWorkRequestBuilder<WidgetUpdateWorker>(
                15, TimeUnit.MINUTES
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
