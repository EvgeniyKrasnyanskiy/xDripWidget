package com.xdripwidget.android

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    private lateinit var etServerUrl: EditText
    private lateinit var etApiSecret: EditText
    private lateinit var sbTransparency: SeekBar
    private lateinit var tvTransparencyVal: TextView
    private lateinit var btnSave: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        etServerUrl = findViewById(R.id.et_server_url)
        etApiSecret = findViewById(R.id.et_api_secret)
        sbTransparency = findViewById(R.id.sb_transparency)
        tvTransparencyVal = findViewById(R.id.tv_transparency_val)
        btnSave = findViewById(R.id.btn_save)

        // Load preferences
        etServerUrl.setText(WidgetPreferences.getServerUrl(this))
        etApiSecret.setText(WidgetPreferences.getApiSecret(this))

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
            val transparency = sbTransparency.progress

            if (url.isEmpty()) {
                Toast.makeText(this, "Введите URL сервера", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            WidgetPreferences.setServerUrl(this, url)
            WidgetPreferences.setApiSecret(this, secret)
            WidgetPreferences.setTransparency(this, transparency)

            // Trigger immediate widget refresh
            xDripWidgetProvider.enqueueOneTimeUpdate(this)
            xDripWidgetProvider.schedulePeriodicWork(this)

            Toast.makeText(this, "Настройки сохранены! Виджет обновляется...", Toast.LENGTH_SHORT).show()
            finish()
        }
    }
}
