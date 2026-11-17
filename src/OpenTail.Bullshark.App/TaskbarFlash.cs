using System.Runtime.InteropServices;

namespace OpenTail.Bullshark.App;

/// <summary>Flashes a window's taskbar button (FlashWindowEx) until the window comes to the front. Windows only; no state is kept.</summary>
internal static class TaskbarFlash
{
    /// <summary>The caption and the taskbar button.</summary>
    private const uint FlashCaptionAndTaskbar = 0x00000003;

    /// <summary>Keep flashing until the window is in the foreground.</summary>
    private const uint FlashUntilForeground = 0x0000000C;

    public static void UntilFocused(IntPtr handle)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = FlashCaptionAndTaskbar | FlashUntilForeground,
            Count = uint.MaxValue,
            TimeoutMilliseconds = 0,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint TimeoutMilliseconds;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
