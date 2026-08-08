package com.xdripwidget.android

import android.os.Bundle
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.SeekBar
import android.widget.Spinner
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

    private lateinit var cbAlarmEnabled: CheckBox
    private lateinit var etLowThreshold: EditText
    private lateinit var etHighThreshold: EditText
    private lateinit var etLowSnooze: EditText
    private lateinit var etHighSnooze: EditText
    private lateinit var spAlarmMelody: Spinner
    private lateinit var sbAlarmVolume: SeekBar
    private lateinit var tvVolumeVal: TextView
    private lateinit var btnTestSound: Button

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

        cbAlarmEnabled = findViewById(R.id.cb_alarm_enabled)
        etLowThreshold = findViewById(R.id.et_low_threshold)
        etHighThreshold = findViewById(R.id.et_high_threshold)
        etLowSnooze = findViewById(R.id.et_low_snooze)
        etHighSnooze = findViewById(R.id.et_high_snooze)
        spAlarmMelody = findViewById(R.id.sp_alarm_melody)
        sbAlarmVolume = findViewById(R.id.sb_alarm_volume)
        tvVolumeVal = findViewById(R.id.tv_volume_val)
        btnTestSound = findViewById(R.id.btn_test_sound)

        btnSave = findViewById(R.id.btn_save)
        btnAbout = findViewById(R.id.btn_about)

        // Load general preferences
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

        // Load Alarm preferences
        cbAlarmEnabled.isChecked = WidgetPreferences.isAlarmEnabled(this)
        etLowThreshold.setText(String.format("%.1f", WidgetPreferences.getLowThreshold(this)).replace(',', '.'))
        etHighThreshold.setText(String.format("%.1f", WidgetPreferences.getHighThreshold(this)).replace(',', '.'))
        etLowSnooze.setText(WidgetPreferences.getLowSnoozeMinutes(this).toString())
        etHighSnooze.setText(WidgetPreferences.getHighSnoozeMinutes(this).toString())

        val melodies = arrayOf(
            "Импульсный сигнал (Beeps)",
            "Сирена (Siren) — По умолчанию",
            "Тройной тон (Triple Tone)"
        )
        val spinnerAdapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, melodies)
        spAlarmMelody.adapter = spinnerAdapter
        spAlarmMelody.setSelection(WidgetPreferences.getAlarmMelody(this).coerceIn(0, 2))

        val currentVolume = WidgetPreferences.getAlarmVolume(this)
        sbAlarmVolume.progress = currentVolume
        tvVolumeVal.text = "$currentVolume%"

        sbAlarmVolume.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                tvVolumeVal.text = "$progress%"
            }
            override fun onStartTrackingTouch(seekBar: SeekBar?) {}
            override fun onStopTrackingTouch(seekBar: SeekBar?) {}
        })

        btnTestSound.setOnClickListener {
            val selectedMelody = spAlarmMelody.selectedItemPosition
            val volume = sbAlarmVolume.progress
            SoundGenerator.playMelodyAsync(selectedMelody, volume)
        }

        btnSave.setOnClickListener {
            val url = etServerUrl.text.toString().trim()
            val secret = etApiSecret.text.toString().trim()
            val interval = sbInterval.progress.coerceAtLeast(1)
            val transparency = sbTransparency.progress

            if (url.isEmpty()) {
                Toast.makeText(this, "Введите URL сервера", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            val lowThresh = etLowThreshold.text.toString().replace(',', '.').toFloatOrNull() ?: 4.0f
            val highThresh = etHighThreshold.text.toString().replace(',', '.').toFloatOrNull() ?: 10.0f
            val lowSnooze = etLowSnooze.text.toString().toIntOrNull() ?: 30
            val highSnooze = etHighSnooze.text.toString().toIntOrNull() ?: 60

            WidgetPreferences.setServerUrl(this, url)
            WidgetPreferences.setApiSecret(this, secret)
            WidgetPreferences.setRefreshInterval(this, interval)
            WidgetPreferences.setTransparency(this, transparency)

            WidgetPreferences.setAlarmEnabled(this, cbAlarmEnabled.isChecked)
            WidgetPreferences.setLowThreshold(this, lowThresh)
            WidgetPreferences.setHighThreshold(this, highThresh)
            WidgetPreferences.setLowSnoozeMinutes(this, lowSnooze.coerceAtLeast(1))
            WidgetPreferences.setHighSnoozeMinutes(this, highSnooze.coerceAtLeast(1))
            WidgetPreferences.setAlarmMelody(this, spAlarmMelody.selectedItemPosition)
            WidgetPreferences.setAlarmVolume(this, sbAlarmVolume.progress)

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
            xDrip Widget v1.7.0
            
            Мобильный виджет мониторинга уровня глюкозы крови.
            Совместим с xDrip+, AAPS (AndroidAPS) и Nightscout.
            
            🎨 Цветовая схема порогов глюкозы:
            🔴 <= 3.3 ммоль/л — Тяжелая гипогликемия (Ярко-красный)
            🟡 3.4 - 3.8 ммоль/л — Легкая гипогликемия (Жёлтый)
            🟢 3.9 - 7.8 ммоль/л — Целевая норма (Зелёный)
            🟡 7.9 - 9.9 ммоль/л — Легкая гипергликемия (Жёлтый)
            🔴 >= 10.0 ммоль/л — Гипергликемия (Мягкий красный)
            
            🔔 Звуковые тревоги:
            • Низкий сахар: по умолчанию < 4.0 ммоль/л (повтор через 30 мин)
            • Высокий сахар: по умолчанию > 10.0 ммоль/л (повтор через 60 мин)
            
            GitHub: EvgeniyKrasnyanskiy/xDripWidget
        """.trimIndent()

        AlertDialog.Builder(this)
            .setTitle("О программе")
            .setMessage(aboutText)
            .setPositiveButton("OK", null)
            .show()
    }
}
