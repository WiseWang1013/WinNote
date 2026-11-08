using System.Speech.Recognition;

namespace WinNote.Services;

public sealed class VoiceService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _stopping;

    public event Action<string>? FinalText;
    public event Action<string>? Hypothesis;
    public event Action<bool>? ListeningChanged;
    public event Action<string>? Failed;

    public bool IsListening => _engine is not null;

    public bool Start(out string? error)
    {
        error = null;
        if (_engine is not null)
        {
            return true;
        }

        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            var recognizer = recognizers.FirstOrDefault(r =>
                                 r.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                             ?? recognizers.FirstOrDefault();

            if (recognizer is null)
            {
                error = "系统里没有安装可用的语音识别引擎。";
                return false;
            }

            var engine = new SpeechRecognitionEngine(recognizer);
            engine.SetInputToDefaultAudioDevice();
            engine.MaxAlternates = 5;
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(10);
            engine.BabbleTimeout = TimeSpan.FromSeconds(5);
            engine.EndSilenceTimeout = TimeSpan.FromSeconds(1.1);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.4);
            engine.LoadGrammar(new DictationGrammar());
            engine.SpeechHypothesized += (_, e) =>
            {
                var text = e.Result?.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    Hypothesis?.Invoke(text);
                }
            };
            engine.SpeechRecognized += (_, e) =>
            {
                var best = e.Result?.Alternates
                    .Cast<RecognizedPhrase>()
                    .OrderByDescending(phrase => phrase.Confidence)
                    .FirstOrDefault() ?? e.Result;
                var text = best?.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    FinalText?.Invoke(text);
                }
            };
            engine.RecognizeCompleted += (_, e) =>
            {
                var wasStopping = _stopping;
                Cleanup();
                ListeningChanged?.Invoke(false);

                if (!wasStopping && e.Error is not null)
                {
                    Failed?.Invoke("语音识别中断：" + e.Error.Message);
                }
            };

            engine.RecognizeAsync(RecognizeMode.Multiple);
            _engine = engine;
            ListeningChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            error = "无法启动语音识别：" + ex.Message +
                    "。请检查麦克风是否可用，以及「Windows 设置 → 隐私和安全性 → 麦克风」是否允许桌面应用访问。";
            Cleanup();
            return false;
        }
    }

    public void Stop()
    {
        if (_engine is null)
        {
            return;
        }

        _stopping = true;
        try
        {
            // Stop 会等当前这一句识别完成，避免点“完成”时把最后一句话丢掉。
            _engine.RecognizeAsyncStop();
        }
        catch
        {
            Cleanup();
            ListeningChanged?.Invoke(false);
        }
    }

    private void Cleanup()
    {
        var engine = _engine;
        _engine = null;
        _stopping = false;
        if (engine is null)
        {
            return;
        }

        try
        {
            engine.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
