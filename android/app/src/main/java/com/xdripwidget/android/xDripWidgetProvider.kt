package com.xdripwidget.android

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.content.Intent
import android.util.Log
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
            enqueueOneTimeUpdate(context)
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
