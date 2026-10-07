using System;
using System.IO;
using System.Media;
using System.Windows.Media;

namespace XDripWidget
{
    public static class SoundHelper
    {
        private static MediaPlayer _mediaPlayer;

        public const string KeyHand = "SystemHand";
        public const string KeyExclamation = "SystemExclamation";
        public const string KeyAsterisk = "SystemAsterisk";
        public const string KeyBeep = "SystemBeep";

        public static void Play(string soundKeyOrPath)
        {
            if (string.IsNullOrWhiteSpace(soundKeyOrPath)) return;

            try
            {
                string key = soundKeyOrPath.Trim();

                if (key.Equals(KeyHand, StringComparison.OrdinalIgnoreCase))
                {
                    SystemSounds.Hand.Play();
                    return;
                }
                if (key.Equals(KeyExclamation, StringComparison.OrdinalIgnoreCase))
                {
                    SystemSounds.Exclamation.Play();
                    return;
                }
                if (key.Equals(KeyAsterisk, StringComparison.OrdinalIgnoreCase))
                {
                    SystemSounds.Asterisk.Play();
                    return;
                }
                if (key.Equals(KeyBeep, StringComparison.OrdinalIgnoreCase))
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                if (File.Exists(key))
                {
                    if (_mediaPlayer == null)
                    {
                        _mediaPlayer = new MediaPlayer();
                    }
                    _mediaPlayer.Stop();
                    _mediaPlayer.Open(new Uri(Path.GetFullPath(key), UriKind.Absolute));
                    _mediaPlayer.Play();
                    return;
                }

                // Fallback if file doesn't exist
                SystemSounds.Exclamation.Play();
            }
            catch
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
        }

        public static string GetDisplayName(string soundKeyOrPath)
        {
            if (string.IsNullOrWhiteSpace(soundKeyOrPath)) return "По умолчанию";

            string key = soundKeyOrPath.Trim();
            if (key.Equals(KeyHand, StringComparison.OrdinalIgnoreCase)) return "Windows: Ошибка (Hand)";
            if (key.Equals(KeyExclamation, StringComparison.OrdinalIgnoreCase)) return "Windows: Предупреждение (Exclamation)";
            if (key.Equals(KeyAsterisk, StringComparison.OrdinalIgnoreCase)) return "Windows: Уведомление (Asterisk)";
            if (key.Equals(KeyBeep, StringComparison.OrdinalIgnoreCase)) return "Windows: Сигнал (Beep)";

            if (File.Exists(key))
            {
                return string.Format("📁 {0}", Path.GetFileName(key));
            }

            return key;
        }
    }
}
