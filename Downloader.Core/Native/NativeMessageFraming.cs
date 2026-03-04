using System.Buffers.Binary;
using System.Text;

namespace Downloader.Core.Native;

public static class NativeMessageFraming
{
    public static async Task<string?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var headerRead = await ReadExactAsync(input, header, cancellationToken);
        if (!headerRead)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException($"Invalid native message length: {length}");
        }

        var payload = new byte[length];
        var payloadRead = await ReadExactAsync(input, payload, cancellationToken);
        if (!payloadRead)
        {
            return null;
        }

        return Encoding.UTF8.GetString(payload);
    }

    public static async Task WriteMessageAsync(Stream output, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(payload, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
