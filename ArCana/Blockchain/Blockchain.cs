﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ArCana.Cryptography;
using Utf8Json;

namespace ArCana.Blockchain
{
    public class Blockchain
    {
        public List<Block> Chain { get; } = new List<Block>();

        public bool CheckInput(Input input, byte[] hash, out Output prevOutTx)
        {
            var transactions = Chain.SelectMany(x => x.Transactions).ToArray();
            prevOutTx = transactions
                .First(x => x.Id.Bytes == input.TransactionId.Bytes)?
                .Outputs[input.OutputIndex];
            var verified = prevOutTx != null && Signature.Verify(hash, input.Signature, input.PublicKey, prevOutTx.PublicKeyHash);

            //utxo check ブロックの長さに比例してコストが上がってしまう問題アリ
            var utxoUsed = transactions.SelectMany(x => x.Inputs).Any(ipt => ipt.TransactionId.Bytes != input.TransactionId.Bytes);

            var redeemable = prevOutTx != null && prevOutTx.PublicKeyHash.SequenceEqual(HashUtil.Hash160(input.PublicKey));


            return verified && !utxoUsed && redeemable;
        }

        public bool VerifyTransaction(Transaction tx, DateTime timestamp, bool isCoinbase, ulong coinbase = 0)
        {
            if (tx.TimeStamp > timestamp ||
                !(isCoinbase ^ tx.Inputs.Count == 0))
                return false;

            var hash = tx.GetSignHash();
            //Input check
            var inSum = coinbase;
            foreach (var input in tx.Inputs)
            {
                if (CheckInput(input, hash, out var prevOutTx)) return false;
                inSum = checked(inSum + prevOutTx.Amount);
            }

            ulong outSum = 0;
            foreach (var output in tx.Outputs)
            {
                if (output.PublicKeyHash is null || output.Amount <= 0)
                    return false;

                outSum = checked(outSum + output.Amount);
            }

            if (outSum > inSum) return false;

            return true;
        }

        public ulong CalculateFee(Transaction tx, ulong coinbase)
        {
            var chainTxs = Chain.SelectMany(x => x.Transactions);
            //var outputs = tx.Inputs.Select(x => x.TransactionId).Select(x => chainTxs.First(cTx => cTx.Id.Equals(x)).Outputs);
            var inSum = tx.Inputs
                .Select(x => chainTxs.First(cTx => cTx.Id.Equals(x.TransactionId)).Outputs[x.OutputIndex].Amount).Aggregate((a,b) => a + b);
            inSum += coinbase;

            var outSum = tx.Outputs.Select(x => x.Amount).Aggregate((a, b) => a + b);
            if(outSum > inSum) throw new ArgumentException();
            return inSum - outSum;
        }
    }
}
