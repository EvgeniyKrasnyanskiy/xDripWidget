package com.xdripwidget.android

import android.content.Context
import android.content.SharedPreferences

object WidgetPreferences {
    private const val PREF_NAME = "xDripWidgetPrefs"
    private const val KEY_SERVER_URL = "server_url"
    private const val KEY_API_SECRET = "api_secret"
    private const val KEY_INTERVAL = "refresh_interval"
    private const val KEY_TRANSPARENCY = "transparency"

    private const val DEFAULT_URL = "http://10.0.2.2:8080"

    private fun getPrefs(context: Context): SharedPreferences {
        return context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE)
    }

    fun getServerUrl(context: Context): String {
        val url = getPrefs(context).getString(KEY_SERVER_URL, DEFAULT_URL) ?: DEFAULT_URL
        return url.trimEnd('/')
    }

    fun setServerUrl(context: Context, url: String) {
        getPrefs(context).edit().putString(KEY_SERVER_URL, url.trimEnd('/')).apply()
    }

    fun getApiSecret(context: Context): String {
        return getPrefs(context).getString(KEY_API_SECRET, "") ?: ""
    }

    fun setApiSecret(context: Context, secret: String) {
        getPrefs(context).edit().putString(KEY_API_SECRET, secret.trim()).apply()
    }

    fun getRefreshInterval(context: Context): Int {
        return getPrefs(context).getInt(KEY_INTERVAL, 5) // default 5 minutes
    }

    fun setRefreshInterval(context: Context, minutes: Int) {
        getPrefs(context).edit().putInt(KEY_INTERVAL, minutes).apply()
    }

    fun getTransparency(context: Context): Int {
        return getPrefs(context).getInt(KEY_TRANSPARENCY, 15) // default 15% transparent
    }

    fun setTransparency(context: Context, percent: Int) {
        getPrefs(context).edit().putInt(KEY_TRANSPARENCY, percent).apply()
    }

    fun isAlarmEnabled(context: Context): Boolean {
        return getPrefs(context).getBoolean(KEY_ALARM_ENABLED, true)
    }

    fun setAlarmEnabled(context: Context, enabled: Boolean) {
        getPrefs(context).edit().putBoolean(KEY_ALARM_ENABLED, enabled).apply()
    }

    fun getLowThreshold(context: Context): Float {
        return getPrefs(context).getFloat(KEY_LOW_THRESHOLD, 4.0f)
    }

    fun setLowThreshold(context: Context, value: Float) {
        getPrefs(context).edit().putFloat(KEY_LOW_THRESHOLD, value).apply()
    }

    fun getHighThreshold(context: Context): Float {
        return getPrefs(context).getFloat(KEY_HIGH_THRESHOLD, 10.0f)
    }

    fun setHighThreshold(context: Context, value: Float) {
        getPrefs(context).edit().putFloat(KEY_HIGH_THRESHOLD, value).apply()
    }

    fun getLowSnoozeMinutes(context: Context): Int {
        return getPrefs(context).getInt(KEY_LOW_SNOOZE_MIN, 30) // default 30 min
    }

    fun setLowSnoozeMinutes(context: Context, minutes: Int) {
        getPrefs(context).edit().putInt(KEY_LOW_SNOOZE_MIN, minutes).apply()
    }

    fun getHighSnoozeMinutes(context: Context): Int {
        return getPrefs(context).getInt(KEY_HIGH_SNOOZE_MIN, 60) // default 60 min (1 hour)
    }

    fun setHighSnoozeMinutes(context: Context, minutes: Int) {
        getPrefs(context).edit().putInt(KEY_HIGH_SNOOZE_MIN, minutes).apply()
    }

    fun getAlarmVolume(context: Context): Int {
        return getPrefs(context).getInt(KEY_ALARM_VOLUME, 80) // default 80%
    }

    fun setAlarmVolume(context: Context, volume: Int) {
        getPrefs(context).edit().putInt(KEY_ALARM_VOLUME, volume).apply()
    }

    fun getAlarmMelody(context: Context): Int {
        return getPrefs(context).getInt(KEY_ALARM_MELODY, 1) // default 1: Siren
    }

    fun setAlarmMelody(context: Context, melodyIndex: Int) {
        getPrefs(context).edit().putInt(KEY_ALARM_MELODY, melodyIndex).apply()
    }

    fun getLastLowAlarmTime(context: Context): Long {
        return getPrefs(context).getLong(KEY_LAST_LOW_ALARM_TIME, 0L)
    }

    fun setLastLowAlarmTime(context: Context, timeMs: Long) {
        getPrefs(context).edit().putLong(KEY_LAST_LOW_ALARM_TIME, timeMs).apply()
    }

    fun getLastHighAlarmTime(context: Context): Long {
        return getPrefs(context).getLong(KEY_LAST_HIGH_ALARM_TIME, 0L)
    }

    fun setLastHighAlarmTime(context: Context, timeMs: Long) {
        getPrefs(context).edit().putLong(KEY_LAST_HIGH_ALARM_TIME, timeMs).apply()
    }

    private const val KEY_ALARM_ENABLED = "alarm_enabled"
    private const val KEY_LOW_THRESHOLD = "low_threshold"
    private const val KEY_HIGH_THRESHOLD = "high_threshold"
    private const val KEY_LOW_SNOOZE_MIN = "low_snooze_min"
    private const val KEY_HIGH_SNOOZE_MIN = "high_snooze_min"
    private const val KEY_ALARM_VOLUME = "alarm_volume"
    private const val KEY_ALARM_MELODY = "alarm_melody"
    private const val KEY_LAST_LOW_ALARM_TIME = "last_low_alarm_time"
    private const val KEY_LAST_HIGH_ALARM_TIME = "last_high_alarm_time"
}
