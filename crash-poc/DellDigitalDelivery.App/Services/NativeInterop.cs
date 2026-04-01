namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Handles native interop for system notifications.
/// BUG: MarshalCallbackData attempts to access freed memory,
/// causing heap corruption (0xC0000374).
/// </summary>
public class NativeInterop
{
    private byte[]? _callbackBuffer;

    /// <summary>
    /// Marshals callback data from a native notification.
    /// BUG: Sets _callbackBuffer to null after reading the first byte,
    /// then tries to access it again, simulating use-after-free.
    /// </summary>
    public byte[] MarshalCallbackData(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        _callbackBuffer = new byte[size];

        // Fill with some data
        for (int i = 0; i < size; i++)
            _callbackBuffer[i] = (byte)(i & 0xFF);

        // BUG: "Release" the buffer prematurely
        byte firstByte = _callbackBuffer[0];
        _callbackBuffer = null;  // Simulate free

        // BUG: Try to use the buffer after releasing it
        // This causes NullReferenceException simulating heap corruption
        byte[] result = new byte[size];
        Array.Copy(_callbackBuffer!, 0, result, 0, size);  // NullRef here

        return result;
    }

    /// <summary>
    /// Processes a native notification code.
    /// </summary>
    public void ProcessNotification(int code)
    {
        Console.WriteLine($"[NativeInterop] Processing notification code: {code}");
        var data = MarshalCallbackData(256);
        Console.WriteLine($"[NativeInterop] Marshalled {data.Length} bytes");
    }

    /// <summary>
    /// Dispatches queued notifications.
    /// </summary>
    public void DispatchNotifications()
    {
        Console.WriteLine("[NativeInterop] Dispatching notifications...");
        int[] pendingCodes = { 1001, 1002, 1003 };
        foreach (var code in pendingCodes)
        {
            ProcessNotification(code);
        }
    }
}
