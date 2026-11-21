using System.Runtime.InteropServices;

namespace CinyNotch.Native;

// Minimal Windows Core Audio interop: the default speaker's volume and mute, and the mute
// switch of every microphone. Only the methods used here are declared, in vtable order.

internal enum EDataFlow { Render = 0, Capture = 1 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(nint notifyData);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

internal static class CoreAudio
{
    private const int DEVICE_STATE_ACTIVE = 1;
    private const int CLSCTX_ALL = 23;
    private static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>Identifies changes this app made, so they don't trigger its own pop-ups.</summary>
    public static Guid OwnContext = Guid.NewGuid();

    public static IAudioEndpointVolume? DefaultSpeaker() => Default(EDataFlow.Render);
    public static IAudioEndpointVolume? DefaultMicrophone() => Default(EDataFlow.Capture);

    private static IAudioEndpointVolume? Default(EDataFlow flow)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device) != 0) return null;
            return Activate(device);
        }
        catch
        {
            return null;
        }
    }

    public static List<IAudioEndpointVolume> AllMicrophones()
    {
        var list = new List<IAudioEndpointVolume>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.EnumAudioEndpoints(EDataFlow.Capture, DEVICE_STATE_ACTIVE, out var devices) != 0) return list;
            devices.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) == 0 && Activate(device) is { } volume) list.Add(volume);
            }
        }
        catch
        {
            // No audio service, no microphones: an empty list is the honest answer.
        }
        return list;
    }

    private static IAudioEndpointVolume? Activate(IMMDevice device)
    {
        var iid = IidEndpointVolume;
        return device.Activate(ref iid, CLSCTX_ALL, 0, out var o) == 0 ? o as IAudioEndpointVolume : null;
    }
}

/// <summary>Receives volume changes. Windows calls it on a background thread.</summary>
internal sealed class VolumeCallback(Action<float, bool, Guid> onChange) : IAudioEndpointVolumeCallback
{
    public int OnNotify(nint data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: Guid context, BOOL muted, float master volume, ...
        var context = Marshal.PtrToStructure<Guid>(data);
        var muted = Marshal.ReadInt32(data, 16) != 0;
        var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        onChange(level, muted, context);
        return 0;
    }
}
