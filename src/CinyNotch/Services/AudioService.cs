using Avalonia.Threading;
using CinyNotch.Diagnostics;
using CinyNotch.Native;

namespace CinyNotch.Services;

/// <summary>Speaker volume and mute, and one switch that mutes every microphone.</summary>
internal sealed class AudioService
{
    private IAudioEndpointVolume? _speaker;
    private VolumeCallback? _speakerCallback;
    private IAudioEndpointVolume? _mic;
    private VolumeCallback? _micCallback;

    public float Volume { get; private set; }
    public bool SpeakerMuted { get; private set; }
    public bool MicMuted { get; private set; }

    /// <summary>Volume changed from outside this app (keys, mixer): level 0..1, muted.</summary>
    public event Action<float, bool>? VolumeChangedOutside;
    public event Action? Changed;

    public void Start()
    {
        try
        {
            _speaker = CoreAudio.DefaultSpeaker();
            if (_speaker is not null)
            {
                _speaker.GetMasterVolumeLevelScalar(out var v);
                _speaker.GetMute(out var m);
                Volume = v;
                SpeakerMuted = m;
                _speakerCallback = new VolumeCallback((level, muted, ctx) => Dispatcher.UIThread.Post(() =>
                {
                    Volume = level;
                    SpeakerMuted = muted;
                    if (ctx != CoreAudio.OwnContext) VolumeChangedOutside?.Invoke(level, muted);
                    Changed?.Invoke();
                }));
                _speaker.RegisterControlChangeNotify(_speakerCallback);
            }

            _mic = CoreAudio.DefaultMicrophone();
            if (_mic is not null)
            {
                _mic.GetMute(out var mm);
                MicMuted = mm;
                _micCallback = new VolumeCallback((_, muted, _) => Dispatcher.UIThread.Post(() =>
                {
                    MicMuted = muted;
                    Changed?.Invoke();
                }));
                _mic.RegisterControlChangeNotify(_micCallback);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"audio unavailable: {ex.Message}");
        }
    }

    public void SetVolume(float level)
    {
        if (_speaker is null) return;
        var ctx = CoreAudio.OwnContext;
        _speaker.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx);
        if (SpeakerMuted && level > 0) _speaker.SetMute(false, ref ctx);
    }

    public void ToggleSpeakerMute()
    {
        if (_speaker is null) return;
        var ctx = CoreAudio.OwnContext;
        _speaker.SetMute(!SpeakerMuted, ref ctx);
    }

    /// <summary>Mutes every microphone at once, or unmutes them all. Returns the new state.</summary>
    public bool ToggleMicMute()
    {
        var target = !MicMuted;
        var ctx = CoreAudio.OwnContext;
        foreach (var mic in CoreAudio.AllMicrophones())
        {
            try { mic.SetMute(target, ref ctx); } catch { /* a device that refuses stays as it is */ }
        }
        MicMuted = target;
        Changed?.Invoke();
        return target;
    }
}
