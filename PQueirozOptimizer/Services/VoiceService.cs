using System.Speech.Synthesis;

namespace PQueirozOptimizer.Services;

public static class VoiceService
{
    private static readonly object _lock = new();
    private static SpeechSynthesizer? _synth;

    public static void Speak(string text, bool isEnglish)
    {
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    _synth ??= new SpeechSynthesizer();
                    _synth.SpeakAsyncCancelAll();

                    var langPrefix = isEnglish ? "en" : "pt";
                    var voice = _synth.GetInstalledVoices()
                        .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase));

                    if (voice != null)
                    {
                        _synth.SelectVoice(voice.VoiceInfo.Name);
                    }

                    _synth.SpeakAsync(text);
                }
            }
            catch
            {
                // Silently handle if audio subsystem or TTS is unavailable
            }
        });
    }

    public static void Stop()
    {
        try
        {
            lock (_lock)
            {
                _synth?.SpeakAsyncCancelAll();
            }
        }
        catch
        {
        }
    }
}
