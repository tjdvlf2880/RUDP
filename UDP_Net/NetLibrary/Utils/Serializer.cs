using System;

namespace NetLibrary.Utils
{
    public class Serializer
    {
        public static byte[] ToByte(int val, int length)
        {
            byte[] buffer = new byte[length];

            for (int i = 0; i < length; i++)
            {
                buffer[i] = (byte)(val >> i * 8 & 0xFF);
            }
            return buffer;
        }
        public static int ToValue(Memory<byte> buffer, int offset, int length)
        {
            int value = 0;
            for (int i = 0; i < length; i++)
            {
                value |= (buffer.Span[offset + i] & 0xFF) << i * 8;
            }
            return value;
        }

        public static int ToValue(byte[] buffer, int offset, int length)
        {
            int value = 0;
            for (int i = 0; i < length; i++)
            {
                value |= (buffer[offset + i] & 0xFF) << i * 8;
            }
            return value;
        }
    }
}
