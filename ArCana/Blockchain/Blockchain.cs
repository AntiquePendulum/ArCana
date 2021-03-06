﻿using ArCana.Blockchain.Util;
using ArCana.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArCana.Blockchain
{
    public class Blockchain
    {
        public List<Block> Chain { get; } = new List<Block>();
        public List<TransactionOutput> Utxos { get; } = new List<TransactionOutput>();
        public TransactionPool TransactionPool { get; set; }
        public event Action Applied;

        public Blockchain() : this(new TransactionPool()){}

        public Blockchain(TransactionPool transactionPool)
        {
            TransactionPool = transactionPool;
            BlockVerify(BlockchainUtil.CreateGenesisBlock());
        }

        public void BlockVerify(Block block)
        {
            lock (Chain)
            {
                Chain.Add(block);
            }

            lock (TransactionPool)
            {
                var ids = block.Transactions.Select(x => x.Id).ToArray();
                TransactionPool.RemoveTxs(ids);
            }

            lock (Utxos)
            {
                var inEntries = block.Transactions.SelectMany(x => x.Inputs);
                Utxos.RemoveAll(x => 
                        inEntries.Any(inEntry =>
                            inEntry.OutputIndex == x.OutIndex &&
                            inEntry.TransactionId.Equals(x.TransactionId)));
                var utxos = 
                    block.Transactions
                    .Select(x => (x.Outputs, x.Id))
                    .SelectMany(x => ToTxO(x.Outputs, x.Id));

                Utxos.AddRange(utxos);
            }

            //if(Chain.Count % 100 == 0) Difficulty.CalculateNextDifficulty()

            Applied?.Invoke();
        }

        public void ChainApply(List<Block> chain)
        {
            lock (this) LockedChainApply(chain);
        }

        void LockedChainApply(List<Block> newChain)
        {
            var localTxs = Chain.SelectMany(x => x.Transactions);
            var remoteTxs = newChain.SelectMany(x => x.Transactions);
            var txNotIncludeRemote =
                localTxs.Where(tx => !remoteTxs.Any(x => x.Id.Equals(tx.Id))).ToArray();

            lock (TransactionPool)
            {
                var txIds = TransactionPool.GetPool().Select(x => x.Id)
                    .Where(x => remoteTxs.Any(tx => x.Equals(tx.Id))).ToArray();
                TransactionPool.RemoveTxs(txIds);
                TransactionPool.AddTxs(txNotIncludeRemote);
            }

            lock (Chain)
            {
                Chain.Clear();
                Chain.AddRange(newChain);
            }

            UpdateUtxos();
            Applied?.Invoke();
        }

        static IEnumerable<TransactionOutput> ToTxO(List<Output> outputs, HexString id)
        {
            return outputs.Select((t, i) => new TransactionOutput()
            {
                TransactionId = id,
                OutIndex = i,
                Output = t,
            });
        }

        public void UpdateUtxos()
        {
            lock (Utxos)
            {
                var inEntries = Chain.SelectMany(x => x.Transactions.SelectMany(tx => tx.Inputs));
                var txO = 
                    Chain.SelectMany(x => x.Transactions)
                        .Select(x => (x.Outputs, x.Id))
                        .SelectMany((x) => ToTxO(x.Outputs, x.Id));

                var newUtxos = txO
                    .Where(x => 
                        !inEntries.Any(input => 
                            input.TransactionId.Equals(x.TransactionId) && 
                            input.OutputIndex == x.OutIndex
                            )
                        );
                Utxos.Clear();
                Utxos.AddRange(newUtxos);
            }
        }

        public bool VerifyTransaction(Transaction tx, DateTime timestamp, bool isCoinbase, out ulong fee, ulong coinbase = 0) 
            => VerifyTransaction(tx, timestamp, Chain, isCoinbase, out fee, coinbase);

        public static bool CheckInput(Input input, byte[] hash, IReadOnlyList<Block> chain, out Output prevOutTx)
        {
            var transactions = chain.SelectMany(x => x.Transactions).ToArray();
            prevOutTx = transactions
                .First(x => x.Id.Equals(input.TransactionId))?
                .Outputs[input.OutputIndex];
            var verified = prevOutTx != null && Signature.Verify(hash, input.Signature, input.PublicKey, prevOutTx.PublicKeyHash);

            //utxo check ブロックの長さに比例してコストが上がってしまう問題アリ
            var utxoUsed = transactions.SelectMany(x => x.Inputs).Any(ipt => ipt.TransactionId.Equals(input.TransactionId));

            var redeemable = prevOutTx != null && prevOutTx.PublicKeyHash.SequenceEqual(HashUtil.Hash160(input.PublicKey));

            return verified && !utxoUsed && redeemable;
        }

        public static bool VerifyTransaction(Transaction tx, DateTime timestamp, IReadOnlyList<Block> chain, bool isCoinbase, out ulong fee, ulong coinbase = 0)
        {
            fee = 0;
            if (tx.TimeStamp > timestamp ||
                (!isCoinbase & tx.Inputs.Count == 0))
                return false;

            var hash = tx.GetSignHash();
            //Input check
            var inSum = coinbase;
            foreach (var input in tx.Inputs)
            {
                if (!CheckInput(input, hash, chain, out var prevOutTx)) return false;
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

            fee = inSum - outSum;
            return true;
        }

        public bool VerifyBlockchain()
        {
            /*var i = 0;
            while (i < Blockchain.Count)
            {
                var rearData = JsonSerializer.Serialize(Blockchain[i]);
                var prevHash = Blockchain[i + 1].PreviousBlockHash.Bytes;
                if (prevHash != HashUtil.DoubleSHA256(rearData)) return false;
                i++;
            }*/

            var isRight = Chain.Take(Chain.Count - 1).SkipWhile((block, i) =>
            {
                var hash = block.ComputeId();
                var prevHash = Chain[i + 1].PreviousBlockHash.Bytes;
                return prevHash.SequenceEqual(hash);
            }).Any();

            return !isRight;
        }

        public static bool VerifyBlockchain(IList<Block> blockchain)
        {
            var isRight = blockchain.Take(blockchain.Count - 1).SkipWhile((block, i) =>
            {
                try
                {
                    return !blockchain[i + 1].PreviousBlockHash.Bytes.SequenceEqual(block.ComputeId());
                }
                catch
                {
                    return false;
                }
            }).Any();
            return !isRight;
        }

        public uint GetDifficulty()
        {
            var last = Chain.Last();
            return Chain.Count < Difficulty.DifInterval
                ? last.Bits
                : Difficulty.CalculateNextDifficulty(last, Chain[^Difficulty.DifInterval].Timestamp).Bits;
        }
    }
}
