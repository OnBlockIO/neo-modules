using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Neo.Consensus
{
    public partial class ConsensusContext : IDisposable, ISerializable
    {
        /// <summary>
        /// Key for saving consensus state.
        /// </summary>
        private static readonly byte[] ConsensusStateKey = { 0xf4 };

        public Block[] Block = new Block[2];
        public byte ViewNumber;
        public ECPoint[] Validators;
        public int MyIndex;
        public UInt256[][] TransactionHashes = new UInt256[2][];
        public Dictionary<UInt256, Transaction>[] Transactions = new Dictionary<UInt256, Transaction>[2];
        public ExtensiblePayload[][] PreparationPayloads = new ExtensiblePayload[2][];
        public ExtensiblePayload[][] PreCommitPayloads = new ExtensiblePayload[2][];
        public ExtensiblePayload[][] CommitPayloads = new ExtensiblePayload[2][];
        public ExtensiblePayload[] ChangeViewPayloads;
        public ExtensiblePayload[] LastChangeViewPayloads;
        // LastSeenMessage array stores the height of the last seen message, for each validator.
        // if this node never heard from validator i, LastSeenMessage[i] will be -1.
        public Dictionary<ECPoint, uint> LastSeenMessage { get; private set; }

        /// <summary>
        /// Store all verified unsorted transactions' senders' fee currently in the consensus context.
        /// </summary>
        public TransactionVerificationContext[] VerificationContext = new TransactionVerificationContext[2];

        public SnapshotCache Snapshot { get; private set; }
        private KeyPair keyPair;
        private int _witnessSize;
        private readonly NeoSystem neoSystem;
        private readonly Settings dbftSettings;
        private readonly Wallet wallet;
        private readonly IStore store;
        private Dictionary<UInt256, ConsensusMessage> cachedMessages;

        public int F => (Validators.Length - 1) / 3;
        public int M => Validators.Length - F;

        public bool IsPriorityPrimary => MyIndex == GetPriorityPrimaryIndex(ViewNumber);
        public bool IsFallbackPrimary => MyIndex == GetFallbackPrimaryIndex(ViewNumber);

        public bool IsAPrimary => IsPriorityPrimary || IsFallbackPrimary;

        //Modify to be 1 or 4/3
        public float PrimaryTimerMultiplier => 1;
        public bool IsBackup => MyIndex >= 0 && !IsPriorityPrimary && IsFallbackPrimary;
        public bool WatchOnly => MyIndex < 0;
        public Header PrevHeader => NativeContract.Ledger.GetHeader(Snapshot, Block[0].PrevHash);
        public int CountCommitted => CommitPayloads.Count(p => p != null);
        public int CountFailed
        {
            get
            {
                if (LastSeenMessage == null) return 0;
                return Validators.Count(p => !LastSeenMessage.TryGetValue(p, out var value) || value < (Block[0].Index - 1));
            }
        }
        public bool ValidatorsChanged
        {
            get
            {
                if (NativeContract.Ledger.CurrentIndex(Snapshot) == 0) return false;
                UInt256 hash = NativeContract.Ledger.CurrentHash(Snapshot);
                TrimmedBlock currentBlock = NativeContract.Ledger.GetTrimmedBlock(Snapshot, hash);
                TrimmedBlock previousBlock = NativeContract.Ledger.GetTrimmedBlock(Snapshot, currentBlock.Header.PrevHash);
                return currentBlock.Header.NextConsensus != previousBlock.Header.NextConsensus;
            }
        }

        #region Consensus States
        public bool RequestSentOrReceived => (PreparationPayloads[0][Block[0].PrimaryIndex] != null || PreparationPayloads[1][Block[0].PrimaryIndex] != null);
        public bool ResponseSent => !WatchOnly && (PreparationPayloads[0][MyIndex] != null || PreparationPayloads[1][MyIndex] != null);
        public bool CommitSent => !WatchOnly && (CommitPayloads[0][MyIndex] != null || CommitPayloads[1][MyIndex] != null);
        public bool BlockSent => (Block[0].Transactions != null || Block[1].Transactions != null);
        public bool ViewChanging => !WatchOnly && GetMessage<ChangeView>(ChangeViewPayloads[MyIndex])?.NewViewNumber > ViewNumber;
        public bool NotAcceptingPayloadsDueToViewChanging => ViewChanging && !MoreThanFNodesCommittedOrLost;
        // A possible attack can happen if the last node to commit is malicious and either sends change view after his
        // commit to stall nodes in a higher view, or if he refuses to send recovery messages. In addition, if a node
        // asking change views loses network or crashes and comes back when nodes are committed in more than one higher
        // numbered view, it is possible for the node accepting recovery to commit in any of the higher views, thus
        // potentially splitting nodes among views and stalling the network.
        public bool MoreThanFNodesCommittedOrLost => (CountCommitted + CountFailed) > F;
        #endregion

        public int Size => throw new NotImplementedException();

        public ConsensusContext(NeoSystem neoSystem, Settings settings, Wallet wallet)
        {
            this.wallet = wallet;
            this.neoSystem = neoSystem;
            this.dbftSettings = settings;
            this.store = neoSystem.LoadStore(settings.RecoveryLogs);
        }

        public Block CreateBlock(uint ii)
        {
            EnsureHeader(ii);
            Contract contract = Contract.CreateMultiSigContract(M, Validators);
            ContractParametersContext sc = new ContractParametersContext(neoSystem.StoreView, Block[ii].Header, dbftSettings.Network);
            for (int i = 0, j = 0; i < Validators.Length && j < M; i++)
            {
                if (GetMessage(CommitPayloads[ii][i])?.ViewNumber != ViewNumber) continue;
                sc.AddSignature(contract, Validators[i], GetMessage<Commit>(CommitPayloads[ii][i]).Signature);
                j++;
            }
            Block[ii].Header.Witness = sc.GetWitnesses()[0];
            Block[ii].Transactions = TransactionHashes[ii].Select(p => Transactions[ii][p]).ToArray();
            return Block[ii];
        }

        public ExtensiblePayload CreatePayload(ConsensusMessage message, byte[] invocationScript = null)
        {
            ExtensiblePayload payload = new ExtensiblePayload
            {
                Category = "dBFT",
                ValidBlockStart = 0,
                ValidBlockEnd = message.BlockIndex,
                Sender = GetSender(message.ValidatorIndex),
                Data = message.ToArray(),
                Witness = invocationScript is null ? null : new Witness
                {
                    InvocationScript = invocationScript,
                    VerificationScript = Contract.CreateSignatureRedeemScript(Validators[message.ValidatorIndex])
                }
            };
            cachedMessages.TryAdd(payload.Hash, message);
            return payload;
        }

        public void Dispose()
        {
            Snapshot?.Dispose();
        }

        public Block EnsureHeader(uint i)
        {
            if (TransactionHashes[i] == null) return null;
            Block[i].Header.MerkleRoot ??= MerkleTree.ComputeRoot(TransactionHashes[i]);
            return Block[i];
        }

        public bool Load()
        {
            byte[] data = store.TryGet(ConsensusStateKey);
            if (data is null || data.Length == 0) return false;
            using (MemoryStream ms = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                try
                {
                    Deserialize(reader);
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        public void Reset(byte viewNumber)
        {
            if (viewNumber == 0)
            {
                Snapshot?.Dispose();
                Snapshot = neoSystem.GetSnapshot();
                uint height = NativeContract.Ledger.CurrentIndex(Snapshot);
                for (uint i = 0; i <= 1; i++)
                {
                    Block[i] = new Block
                    {
                        Header = new Header
                        {
                            PrevHash = NativeContract.Ledger.CurrentHash(Snapshot),
                            Index = height + 1,
                            NextConsensus = Contract.GetBFTAddress(
                                NeoToken.ShouldRefreshCommittee(height + 1, neoSystem.Settings.CommitteeMembersCount) ?
                                NativeContract.NEO.ComputeNextBlockValidators(Snapshot, neoSystem.Settings) :
                                NativeContract.NEO.GetNextBlockValidators(Snapshot, neoSystem.Settings.ValidatorsCount))
                        }
                    };

                    CommitPayloads[i] = new ExtensiblePayload[Validators.Length];
                }
                var pv = Validators;
                Validators = NativeContract.NEO.GetNextBlockValidators(Snapshot, neoSystem.Settings.ValidatorsCount);
                if (_witnessSize == 0 || (pv != null && pv.Length != Validators.Length))
                {
                    // Compute the expected size of the witness
                    using (ScriptBuilder sb = new())
                    {
                        for (int x = 0; x < M; x++)
                        {
                            sb.EmitPush(new byte[64]);
                        }
                        _witnessSize = new Witness
                        {
                            InvocationScript = sb.ToArray(),
                            VerificationScript = Contract.CreateMultiSigRedeemScript(M, Validators)
                        }.Size;
                    }
                }
                MyIndex = -1;
                ChangeViewPayloads = new ExtensiblePayload[Validators.Length];
                LastChangeViewPayloads = new ExtensiblePayload[Validators.Length];
                if (ValidatorsChanged || LastSeenMessage is null)
                {
                    var previous_last_seen_message = LastSeenMessage;
                    LastSeenMessage = new Dictionary<ECPoint, uint>();
                    foreach (var validator in Validators)
                    {
                        if (previous_last_seen_message != null && previous_last_seen_message.TryGetValue(validator, out var value))
                            LastSeenMessage[validator] = value;
                        else
                            LastSeenMessage[validator] = height;
                    }
                }
                keyPair = null;
                for (int i = 0; i < Validators.Length; i++)
                {
                    WalletAccount account = wallet?.GetAccount(Validators[i]);
                    if (account?.HasKey != true) continue;
                    MyIndex = i;
                    keyPair = account.GetKey();
                    break;
                }
                cachedMessages = new Dictionary<UInt256, ConsensusMessage>();
            }
            else
            {
                for (int i = 0; i < LastChangeViewPayloads.Length; i++)
                    if (GetMessage<ChangeView>(ChangeViewPayloads[i])?.NewViewNumber >= viewNumber)
                        LastChangeViewPayloads[i] = ChangeViewPayloads[i];
                    else
                        LastChangeViewPayloads[i] = null;
            }
            ViewNumber = viewNumber;
            for (uint i = 0; i <= 1; i++)
            {
                Block[i].Header.PrimaryIndex = GetPriorityPrimaryIndex(viewNumber);
                Block[i].Header.MerkleRoot = null;
                Block[i].Header.Timestamp = 0;
                Block[i].Header.Nonce = 0;
                Block[i].Transactions = null;
                TransactionHashes[i] = null;
                PreparationPayloads[i] = new ExtensiblePayload[Validators.Length];
                if (MyIndex >= 0) LastSeenMessage[Validators[MyIndex]] = Block[i].Index;
            }

            // Disable Fallback if viewnumber > 1
            if (viewNumber > 0)
            {
                Block[1] = null;
                TransactionHashes[1] = null;
                Transactions[1] = null;
                VerificationContext[1] = null;
                PreparationPayloads[1] = null;
                PreCommitPayloads[1] = null;
                CommitPayloads[1] = null;
            }
        }

        public void Save()
        {
            store.PutSync(ConsensusStateKey, this.ToArray());
        }

        public void Deserialize(BinaryReader reader)
        {
            Reset(0);
            for (uint i = 0; i <= 1; i++)
            {
                if (reader.ReadUInt32() != Block[i].Version) throw new FormatException();
                if (reader.ReadUInt32() != Block[i].Index) throw new InvalidOperationException();
                Block[i].Header.Timestamp = reader.ReadUInt64();
                Block[i].Header.Nonce = reader.ReadUInt64();
                Block[i].Header.PrimaryIndex = reader.ReadByte();
                Block[i].Header.NextConsensus = reader.ReadSerializable<UInt160>();
                if (Block[i].NextConsensus.Equals(UInt160.Zero))
                    Block[i].Header.NextConsensus = null;

                TransactionHashes[i] = reader.ReadSerializableArray<UInt256>(ushort.MaxValue);
                Transaction[] transactions = reader.ReadSerializableArray<Transaction>(ushort.MaxValue);
                PreparationPayloads[i] = reader.ReadNullableArray<ExtensiblePayload>(neoSystem.Settings.ValidatorsCount);
                PreCommitPayloads[i] = reader.ReadNullableArray<ExtensiblePayload>(neoSystem.Settings.ValidatorsCount);
                CommitPayloads[i] = reader.ReadNullableArray<ExtensiblePayload>(neoSystem.Settings.ValidatorsCount);

                if (TransactionHashes[i].Length == 0 && !RequestSentOrReceived)
                    TransactionHashes[i] = null;
                Transactions[i] = transactions.Length == 0 && !RequestSentOrReceived ? null : transactions.ToDictionary(p => p.Hash);
                VerificationContext[i] = new TransactionVerificationContext();
                if (Transactions[i] != null)
                {
                    foreach (Transaction tx in Transactions[i].Values)
                        VerificationContext[i].AddTransaction(tx);
                }
            }

            ViewNumber = reader.ReadByte();
            ChangeViewPayloads = reader.ReadNullableArray<ExtensiblePayload>(neoSystem.Settings.ValidatorsCount);
            LastChangeViewPayloads = reader.ReadNullableArray<ExtensiblePayload>(neoSystem.Settings.ValidatorsCount);

        }

        public void Serialize(BinaryWriter writer)
        {
            for (uint i = 0; i <= 1; i++)
            {
                writer.Write(Block[i].Version);
                writer.Write(Block[i].Index);
                writer.Write(Block[i].Timestamp);
                writer.Write(Block[i].Nonce);
                writer.Write(Block[i].PrimaryIndex);
                writer.Write(Block[i].NextConsensus ?? UInt160.Zero);
                writer.Write(TransactionHashes[i] ?? Array.Empty<UInt256>());
                writer.Write(Transactions[i]?.Values.ToArray() ?? Array.Empty<Transaction>());
                writer.WriteNullableArray(PreparationPayloads[i]);
                writer.WriteNullableArray(PreCommitPayloads[i]);
                writer.WriteNullableArray(CommitPayloads[i]);
            }
            writer.Write(ViewNumber);
            writer.WriteNullableArray(ChangeViewPayloads);
            writer.WriteNullableArray(LastChangeViewPayloads);
        }
    }
}
