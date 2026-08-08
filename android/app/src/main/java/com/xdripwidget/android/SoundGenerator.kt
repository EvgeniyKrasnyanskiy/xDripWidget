package com.xdripwidget.android

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import kotlin.concurrent.thread
import kotlin.math.sin

object SoundGenerator {
    private const val SAMPLE_RATE = 44100

    @Volatile
    private var currentTrack: AudioTrack? = null

    @Volatile
    private var isPlaying = false

    fun stopMelody() {
        isPlaying = false
        try {
            currentTrack?.let {
                if (it.playState == AudioTrack.PLAYSTATE_PLAYING) {
                    it.stop()
                }
                it.release()
            }
        } catch (e: Exception) {
            e.printStackTrace()
        } finally {
            currentTrack = null
        }
    }

    fun playMelodyAsync(melodyIndex: Int, volumePercent: Int) {
        stopMelody()
        thread {
            playMelodyOnce(melodyIndex, volumePercent)
        }
    }

    fun playAlarmCycleAsync(melodyIndex: Int, volumePercent: Int, durationMs: Long = 60_000L) {
        stopMelody()
        thread {
            isPlaying = true
            val pcmData = generatePcm(melodyIndex)
            if (pcmData.isEmpty()) return@thread

            val vol = (volumePercent.coerceIn(0, 100) / 100f)

            try {
                val audioAttributes = AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_ALARM)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .build()

                val audioFormat = AudioFormat.Builder()
                    .setSampleRate(SAMPLE_RATE)
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                    .build()

                val track = AudioTrack.Builder()
                    .setAudioAttributes(audioAttributes)
                    .setAudioFormat(audioFormat)
                    .setBufferSizeInBytes(pcmData.size)
                    .setTransferMode(AudioTrack.MODE_STATIC)
                    .build()

                currentTrack = track
                track.setVolume(vol)
                track.write(pcmData, 0, pcmData.size)

                val singleDurationMs = (pcmData.size.toDouble() / (SAMPLE_RATE * 2) * 1000).toLong()
                val startTime = System.currentTimeMillis()

                while (isPlaying && (System.currentTimeMillis() - startTime) < durationMs) {
                    track.stop()
                    track.setPlaybackHeadPosition(0)
                    track.play()
                    
                    val sleepStep = 100L
                    var elapsed = 0L
                    val totalSleep = singleDurationMs + 200L
                    while (isPlaying && elapsed < totalSleep) {
                        Thread.sleep(sleepStep)
                        elapsed += sleepStep
                    }
                }

                track.stop()
                track.release()
            } catch (e: Exception) {
                e.printStackTrace()
            } finally {
                if (currentTrack === currentTrack) {
                    currentTrack = null
                }
                isPlaying = false
            }
        }
    }

    private fun playMelodyOnce(melodyIndex: Int, volumePercent: Int) {
        val pcmData = generatePcm(melodyIndex)
        if (pcmData.isEmpty()) return

        val vol = (volumePercent.coerceIn(0, 100) / 100f)

        try {
            val audioAttributes = AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_ALARM)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()

            val audioFormat = AudioFormat.Builder()
                .setSampleRate(SAMPLE_RATE)
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                .build()

            val track = AudioTrack.Builder()
                .setAudioAttributes(audioAttributes)
                .setAudioFormat(audioFormat)
                .setBufferSizeInBytes(pcmData.size)
                .setTransferMode(AudioTrack.MODE_STATIC)
                .build()

            currentTrack = track
            isPlaying = true
            track.setVolume(vol)
            track.write(pcmData, 0, pcmData.size)
            track.play()

            val durationMs = (pcmData.size.toDouble() / (SAMPLE_RATE * 2) * 1000).toLong()
            var elapsed = 0L
            while (isPlaying && elapsed < durationMs + 100L) {
                Thread.sleep(50L)
                elapsed += 50L
            }
            track.stop()
            track.release()
        } catch (e: Exception) {
            e.printStackTrace()
        } finally {
            currentTrack = null
            isPlaying = false
        }
    }

    private fun generatePcm(melodyIndex: Int): ByteArray {
        return when (melodyIndex) {
            0 -> generateBeeps()
            1 -> generateSiren()
            2 -> generateTripleTone()
            else -> generateSiren()
        }
    }

    // 0: Beeps (3 fast 1000Hz beeps)
    private fun generateBeeps(): ByteArray {
        val samples = ArrayList<Short>()
        val beepDuration = (SAMPLE_RATE * 0.12).toInt()
        val pauseDuration = (SAMPLE_RATE * 0.08).toInt()

        repeat(3) {
            for (i in 0 until beepDuration) {
                val angle = 2.0 * Math.PI * i * 1000 / SAMPLE_RATE
                val sample = (sin(angle) * 28000).toInt().toShort()
                samples.add(sample)
            }
            for (i in 0 until pauseDuration) {
                samples.add(0.toShort())
            }
        }
        return samplesToByteArray(samples)
    }

    // 1: Siren (880Hz / 440Hz alternating) - DEFAULT
    private fun generateSiren(): ByteArray {
        val samples = ArrayList<Short>()
        val toneDuration = (SAMPLE_RATE * 0.22).toInt()

        repeat(2) {
            // High tone 880Hz
            for (i in 0 until toneDuration) {
                val angle = 2.0 * Math.PI * i * 880 / SAMPLE_RATE
                val sample = (sin(angle) * 28000).toInt().toShort()
                samples.add(sample)
            }
            // Low tone 440Hz
            for (i in 0 until toneDuration) {
                val angle = 2.0 * Math.PI * i * 440 / SAMPLE_RATE
                val sample = (sin(angle) * 28000).toInt().toShort()
                samples.add(sample)
            }
        }
        return samplesToByteArray(samples)
    }

    // 2: Triple Tone (523Hz -> 659Hz -> 784Hz)
    private fun generateTripleTone(): ByteArray {
        val samples = ArrayList<Short>()
        val freqs = doubleArrayOf(523.25, 659.25, 784.00)
        val toneDuration = (SAMPLE_RATE * 0.18).toInt()

        for (f in freqs) {
            for (i in 0 until toneDuration) {
                val angle = 2.0 * Math.PI * i * f / SAMPLE_RATE
                val sample = (sin(angle) * 26000).toInt().toShort()
                samples.add(sample)
            }
        }
        return samplesToByteArray(samples)
    }

    private fun samplesToByteArray(samples: List<Short>): ByteArray {
        val bytes = ByteArray(samples.size * 2)
        for (i in samples.indices) {
            val sample = samples[i].toInt()
            bytes[i * 2] = (sample and 0x00FF).toByte()
            bytes[i * 2 + 1] = ((sample shr 8) and 0x00FF).toByte()
        }
        return bytes
    }
}
