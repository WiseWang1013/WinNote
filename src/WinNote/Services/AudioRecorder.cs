using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNote.Services;

public sealed class AudioRecorder : IDisposable
{
    private const string Alias = "winnote_recording";
    private bool _recording;

    public bool IsRecording => _recording;

    public void Start()
    {
        if (_recording)
        {
            return;
        }

        Close();
        Send($"open new type waveaudio alias {Alias}");
        try
        {
            Send($"set {Alias} time format ms bitspersample 16 channels 1 samplespersec 44100");
        }
        catch
        {
            // 某些声卡不支持指定格式，使用系统默认格式继续录音
        }

        Send($"record {Alias}");
        _recording = true;
    }

    public string Stop(string path)
    {
        if (!_recording)
        {
            throw new InvalidOperationException("当前没有正在进行的录音。");
        }

        try
        {
            Send($"stop {Alias}");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Send($"save {Alias} \"{path}\"");
        }
        finally
        {
            Close();
            _recording = false;
        }

        if (!File.Exists(path) || new FileInfo(path).Length < 44)
        {
            throw new InvalidOperationException("录音没有生成有效的音频文件。");
        }

        return path;
    }

    private void Close()
    {
        try
        {
            Send($"close {Alias}");
        }
        catch
        {
            // ignored
        }
    }

    private static void Send(string command)
    {
        var error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error == 0)
        {
            return;
        }

        var buffer = new StringBuilder(256);
        mciGetErrorString(error, buffer, buffer.Capacity);
        throw new InvalidOperationException("录音设备错误：" + buffer);
    }

    public void Dispose()
    {
        if (_recording)
        {
            try
            {
                Send($"stop {Alias}");
            }
            catch
            {
                // ignored
            }
        }

        Close();
        _recording = false;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnString, int returnLength, IntPtr hwndCallback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);
}
