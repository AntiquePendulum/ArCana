﻿using System.Linq;

namespace ArCana.Extensions
{
    public static class ByteArrayExtensions
    {
        public static HexString ToHexString(this byte[] data)
            => new HexString(data);

        public static string ToHex(this byte[] data)
            => data is null ? "Null" :string.Join("", data.Select(x => $"{x:X2}"));
    }
}
