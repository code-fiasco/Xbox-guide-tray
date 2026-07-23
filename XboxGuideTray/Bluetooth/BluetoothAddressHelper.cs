using System.Globalization;

namespace XboxGuideTray.Bluetooth;

internal static class BluetoothAddressHelper
{
    public const string XboxControllerName = "Xbox Wireless Controller";

    public static ulong Parse(string text)
    {
        string normalized = text.Replace(":", string.Empty).Replace("-", string.Empty);
        if (normalized.Length != 12)
        {
            throw new ArgumentException($"Invalid Bluetooth address: {text}", nameof(text));
        }

        Span<byte> mac = stackalloc byte[6];
        for (int i = 0; i < 6; i++)
        {
            if (!byte.TryParse(
                    normalized.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out mac[i]))
            {
                throw new ArgumentException($"Invalid Bluetooth address: {text}", nameof(text));
            }
        }

        ulong address = 0;
        for (int i = 0; i < 6; i++)
        {
            address |= (ulong)mac[i] << (8 * (5 - i));
        }

        return address;
    }

    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            address = Parse(text);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string Format(ulong address)
    {
        Span<char> buffer = stackalloc char[17];
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            if (i > 0)
            {
                buffer[(i * 3) - 1] = ':';
            }

            value.TryFormat(buffer.Slice(i * 3, 2), out _, "X2");
        }

        return new string(buffer);
    }

    public static string FormatPnpMacToken(ulong address)
    {
        // PnP instance IDs embed the MAC without separators, in reverse byte order.
        Span<char> buffer = stackalloc char[12];
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            value.TryFormat(buffer.Slice((5 - i) * 2, 2), out _, "X2");
        }

        return new string(buffer);
    }

    // Windows APIs may return the same controller address in different byte orders.
    public static bool Matches(ulong left, ulong right) =>
        left == right || ReverseAddress(left) == right || left == ReverseAddress(right);

    private static ulong ReverseAddress(ulong address)
    {
        ulong reversed = 0;
        for (int i = 0; i < 6; i++)
        {
            byte value = (byte)((address >> (8 * i)) & 0xFF);
            reversed |= (ulong)value << (8 * (5 - i));
        }

        return reversed;
    }
}
