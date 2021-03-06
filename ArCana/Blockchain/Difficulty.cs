﻿using System;

namespace ArCana.Blockchain
{
    public class Difficulty
    {
        public const uint MaxDifficultBits = 64;
        public const uint MinDifficultBits = 17;

        public const int DifInterval = 100;
        private static readonly TimeSpan TargetTime = TimeSpan.FromMinutes(10);

        private uint _bits = MinDifficultBits;

        public uint Bits
        {
            get => _bits;
            set
            {
                _bits = value switch
                {
                    {} n when n < MinDifficultBits => MinDifficultBits,
                    {} n when n > MaxDifficultBits => MaxDifficultBits,
                    _ => value
                };
            }
        }

        public Difficulty(uint bits)
        {
            Bits = bits;
        }

        public byte[] ToTargetBytes() => ToTargetBytes(Bits);

        public static byte[] ToTargetBytes(uint bits)
        {
            var byteLength = bits / 8;
            var bitLength = bits % 8;
            var bytes = new byte[32];
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i < byteLength) bytes[i] = 0x00;
                else if (i == byteLength) bytes[i] = (byte)(Math.Pow(2, 8 - bitLength) - 1);
                else bytes[i] = 0xFF;
            }

            return bytes;
        }

        public static Difficulty CalculateNextDifficulty(Block lastBlock, DateTime firstDate)
        {
            var actualTime = (lastBlock.Timestamp - firstDate).Seconds;
            var bits = lastBlock.Bits;
            if (actualTime < TargetTime.Seconds) bits++;
            if (actualTime < TargetTime.Seconds/2) bits++;
            if (actualTime > TargetTime.Seconds) bits--;
            if (actualTime > TargetTime.Seconds*2) bits--;
            return new Difficulty(bits);
        }
    }
}
