package com.xdripwidget.android

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    private lateinit var etServerUrl: EditText
    private lateinit var etApiSecret: EditText
    private lateinit var sbInterval: SeekBar
    private lateinit var tvIntervalVal: TextView
    private lateinit var sbTransparency: SeekBar
    private lateinit var tvTransparencyVal: TextView
    private lateinit var btnSave: Button
    private lateinit var btnAbout: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        etServerUrl = findViewById(R.id.et_server_url)
        etApiSecret = findViewById(R.id.et_api_secret)
        sbInterval = findViewById(R.id.sb_interval)
        tvIntervalVal = findViewById(R.id.tv_interval_val)
        sbTransparency = findViewById(R.id.sb_transparency)
        tvTransparencyVal = findViewById(R.id.tv_transparency_val)
        btnSave = findViewById(R.id.btn_save)
        btnAbout = findViewById(R.id.btn_about)

        // Load preferences
        etServerUrl.setText(WidgetPreferences.getServerUrl(this))
        etApiSecret.setText(WidgetPreferences.getApiSecret(this))

        val currentInterval = WidgetPreferences.getRefreshInterval(this).coerceIn(1, 60)
        sbInterval.progress = currentInterval
        tvIntervalVal.text = "$currentInterval мин"

        sbInterval.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                val valMin = progress.coerceAtLeast(1)
                tvIntervalVal.text = "$valMin мин"
            }
            override fun onStartTrackingTouch(seekBar: SeekBar?) {}
            override fun onStopTrackingTouch(seekBar: SeekBar?) {}
        })

        val currentTrans = WidgetPreferences.getTransparency(this)
        sbTransparency.progress = currentTrans
        tvTransparencyVal.text = "$currentTrans%"

        sbTransparency.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                tvTransparencyVal.text = "$progress%"
            }
            override fun onStartTrackingTouch(seekBar: SeekBar?) {}
            override fun onStopTrackingTouch(seekBar: SeekBar?) {}
        })

        btnSave.setOnClickListener {
            val url = etServerUrl.text.toString().trim()
            val secret = etApiSecret.text.toString().trim()
            val interval = sbInterval.progress.coerceAtLeast(1)
            val transparency = sbTransparency.progress

            if (url.isEmpty()) {
                Toast.makeText(this, "Введите URL сервера", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            WidgetPreferences.setServerUrl(this, url)
            WidgetPreferences.setApiSecret(this, secret)
            WidgetPreferences.setRefreshInterval(this, interval)
            WidgetPreferences.setTransparency(this, transparency)

            // Trigger immediate widget refresh
            xDripWidgetProvider.enqueueOneTimeUpdate(this)
            xDripWidgetProvider.schedulePeriodicWork(this)

            Toast.makeText(this, "Настройки сохранены! Виджет обновляется...", Toast.LENGTH_SHORT).show()
            finish()
        }

        btnAbout.setOnClickListener {
            showAboutDialog()
        }
    }

    private fun showAboutDialog() {
        val aboutText = """
            xDrip Widget v1.6.0
            
            Мобильный виджет мониторинга уровня глюкозы крови.
            Совместим с xDrip+, AAPS (AndroidAPS) и Nightscout.
            
            🎨 Цветовая схема порогов глюкозы:
            🔴 <= 3.3 ммоль/л — Тяжелая гипогликемия (Ярко-красный)
            🟡 3.4 - 3.8 ммоль/л — Легкая гипогликемия (Жёлтый)
            🟢 3.9 - 7.8 ммоль/л — Целевая норма (Зелёный)
            🟡 7.9 - 9.9 ммоль/л — Легкая гипергликемия (Жёлтый)
            🔴 >= 10.0 ммоль/л — Гипергликемия (Мягкий красный)
            
            GitHub: EvgeniyKrasnyanskiy/xDripWidget
        """.trimIndent()

        AlertDialog.Builder(this)
            .setTitle("О программе")
            .setMessage(aboutText)
            .setPositiveButton("OK", null)
            .show()
    }
}
