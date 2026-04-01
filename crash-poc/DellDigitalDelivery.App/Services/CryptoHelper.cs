namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Provides cryptographic operations for license validation.
/// BUG: DecryptBuffer has an off-by-one error that causes a buffer overrun
/// when the input length is not a multiple of the block size.
/// </summary>
public static class CryptoHelper
{
    private const int BlockSize = 16;

    /// <summary>
    /// Decrypts an encrypted byte buffer using a simple XOR cipher.
    /// BUG: The loop bound uses &lt;= instead of &lt;, causing a write
    /// past the end of the output buffer when input.Length % BlockSize != 0.
    /// </summary>
    public static byte[] DecryptBuffer(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        int blockCount = input.Length / BlockSize;
        // BUG: Should be blockCount * BlockSize, but we add an extra block
        // causing buffer overrun on the last iteration
        byte[] output = new byte[blockCount * BlockSize];

        byte[] key = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
                       0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        // BUG: Using <= instead of < causes access beyond array bounds
        for (int i = 0; i <= blockCount; i++)
        {
            for (int j = 0; j < BlockSize; j++)
            {
                int srcIndex = i * BlockSize + j;
                int dstIndex = i * BlockSize + j;
                // This will throw IndexOutOfRangeException on the last iteration
                output[dstIndex] = (byte)(input[srcIndex] ^ key[j]);
            }
        }

        return output;
    }

    /// <summary>
    /// Encrypts a byte buffer using a simple XOR cipher.
    /// This method works correctly and is used to prepare test data.
    /// </summary>
    public static byte[] EncryptBuffer(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        byte[] key = { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
                       0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        byte[] output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = (byte)(input[i] ^ key[i % BlockSize]);
        }

        return output;
    }
}
