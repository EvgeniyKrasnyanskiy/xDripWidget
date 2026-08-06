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
}
