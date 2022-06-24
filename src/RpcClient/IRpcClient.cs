using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Network.RPC.Models;
using Neo.SmartContract;

namespace Neo.Network.RPC;

public interface IRpcClient
{
    ProtocolSettings ProtocolSettings { get; init; }
    RpcResponse Send(RpcRequest request);
    Task<RpcResponse> SendAsync(RpcRequest request);
    JObject RpcSend(string method, params JObject[] paraArgs);
    Task<JObject> RpcSendAsync(string method, params JObject[] paraArgs);

    /// <summary>
    /// Returns the hash of the tallest block in the main chain.
    /// </summary>
    Task<string> GetBestBlockHashAsync();

    /// <summary>
    /// Returns the hash of the tallest block in the main chain.
    /// The serialized information of the block is returned, represented by a hexadecimal string.
    /// </summary>
    Task<string> GetBlockHexAsync(string hashOrIndex);

    /// <summary>
    /// Returns the hash of the tallest block in the main chain.
    /// </summary>
    Task<RpcBlock> GetBlockAsync(string hashOrIndex);

    /// <summary>
    /// Gets the number of block header in the main chain.
    /// </summary>
    Task<uint> GetBlockHeaderCountAsync();

    /// <summary>
    /// Gets the number of blocks in the main chain.
    /// </summary>
    Task<uint> GetBlockCountAsync();

    /// <summary>
    /// Returns the hash value of the corresponding block, based on the specified index.
    /// </summary>
    Task<string> GetBlockHashAsync(int index);

    /// <summary>
    /// Returns the corresponding block header information according to the specified script hash.
    /// </summary>
    Task<string> GetBlockHeaderHexAsync(string hashOrIndex);

    /// <summary>
    /// Returns the corresponding block header information according to the specified script hash.
    /// </summary>
    Task<RpcBlockHeader> GetBlockHeaderAsync(string hashOrIndex);

    /// <summary>
    /// Queries contract information, according to the contract script hash.
    /// </summary>
    Task<ContractState> GetContractStateAsync(string hash);

    /// <summary>
    /// Get all native contracts.
    /// </summary>
    Task<RpcNativeContract[]> GetNativeContractsAsync();

    /// <summary>
    /// Obtains the list of unconfirmed transactions in memory.
    /// </summary>
    Task<string[]> GetRawMempoolAsync();

    /// <summary>
    /// Obtains the list of unconfirmed transactions in memory.
    /// shouldGetUnverified = true
    /// </summary>
    Task<RpcRawMemPool> GetRawMempoolBothAsync();

    /// <summary>
    /// Returns the corresponding transaction information, based on the specified hash value.
    /// </summary>
    Task<string> GetRawTransactionHexAsync(string txHash);

    /// <summary>
    /// Returns the corresponding transaction information, based on the specified hash value.
    /// verbose = true
    /// </summary>
    Task<RpcTransaction> GetRawTransactionAsync(string txHash);

    /// <summary>
    /// Calculate network fee
    /// </summary>
    /// <param name="tx">Transaction</param>
    /// <returns>NetworkFee</returns>
    Task<long> CalculateNetworkFeeAsync(Transaction tx);

    /// <summary>
    /// Returns the stored value, according to the contract script hash (or Id) and the stored key.
    /// </summary>
    Task<string> GetStorageAsync(string scriptHashOrId, string key);

    /// <summary>
    /// Returns the block index in which the transaction is found.
    /// </summary>
    Task<uint> GetTransactionHeightAsync(string txHash);

    /// <summary>
    /// Returns the next NEO consensus nodes information and voting status.
    /// </summary>
    Task<RpcValidator[]> GetNextBlockValidatorsAsync();

    /// <summary>
    /// Returns the current NEO committee members.
    /// </summary>
    Task<string[]> GetCommitteeAsync();

    /// <summary>
    /// Gets the current number of connections for the node.
    /// </summary>
    Task<int> GetConnectionCountAsync();

    /// <summary>
    /// Gets the list of nodes that the node is currently connected/disconnected from.
    /// </summary>
    Task<RpcPeers> GetPeersAsync();

    /// <summary>
    /// Returns the version information about the queried node.
    /// </summary>
    Task<RpcVersion> GetVersionAsync();

    /// <summary>
    /// Broadcasts a serialized transaction over the NEO network.
    /// </summary>
    Task<UInt256> SendRawTransactionAsync(byte[] rawTransaction);

    /// <summary>
    /// Broadcasts a transaction over the NEO network.
    /// </summary>
    Task<UInt256> SendRawTransactionAsync(Transaction transaction);

    /// <summary>
    /// Broadcasts a serialized block over the NEO network.
    /// </summary>
    Task<UInt256> SubmitBlockAsync(byte[] block);

    /// <summary>
    /// Returns the result after calling a smart contract at scripthash with the given operation and parameters.
    /// This RPC call does not affect the blockchain in any way.
    /// </summary>
    Task<RpcInvokeResult> InvokeFunctionAsync(string scriptHash, string operation, RpcStack[] stacks, params Signer[] signer);

    /// <summary>
    /// Returns the result after passing a script through the VM.
    /// This RPC call does not affect the blockchain in any way.
    /// </summary>
    Task<RpcInvokeResult> InvokeScriptAsync(ReadOnlyMemory<byte> script, params Signer[] signers);

    Task<RpcUnclaimedGas> GetUnclaimedGasAsync(string address);

    /// <summary>
    /// Returns a list of plugins loaded by the node.
    /// </summary>
    Task<RpcPlugin[]> ListPluginsAsync();

    /// <summary>
    /// Verifies that the address is a correct NEO address.
    /// </summary>
    Task<RpcValidateAddressResult> ValidateAddressAsync(string address);

    /// <summary>
    /// Close the wallet opened by RPC.
    /// </summary>
    Task<bool> CloseWalletAsync();

    /// <summary>
    /// Exports the private key of the specified address.
    /// </summary>
    Task<string> DumpPrivKeyAsync(string address);

    /// <summary>
    /// Creates a new account in the wallet opened by RPC.
    /// </summary>
    Task<string> GetNewAddressAsync();

    /// <summary>
    /// Returns the balance of the corresponding asset in the wallet, based on the specified asset Id.
    /// This method applies to assets that conform to NEP-17 standards.
    /// </summary>
    /// <returns>new address as string</returns>
    Task<BigDecimal> GetWalletBalanceAsync(string assetId);

    /// <summary>
    /// Gets the amount of unclaimed GAS in the wallet.
    /// </summary>
    Task<BigDecimal> GetWalletUnclaimedGasAsync();

    /// <summary>
    /// Imports the private key to the wallet.
    /// </summary>
    Task<RpcAccount> ImportPrivKeyAsync(string wif);

    /// <summary>
    /// Lists all the accounts in the current wallet.
    /// </summary>
    Task<List<RpcAccount>> ListAddressAsync();

    /// <summary>
    /// Open wallet file in the provider's machine.
    /// By default, this method is disabled by RpcServer config.json.
    /// </summary>
    Task<bool> OpenWalletAsync(string path, string password);

    /// <summary>
    /// Transfer from the specified address to the destination address.
    /// </summary>
    /// <returns>This function returns Signed Transaction JSON if successful, ContractParametersContext JSON if signing failed.</returns>
    Task<JObject> SendFromAsync(string assetId, string fromAddress, string toAddress, string amount);

    /// <summary>
    /// Bulk transfer order, and you can specify a sender address.
    /// </summary>
    /// <returns>This function returns Signed Transaction JSON if successful, ContractParametersContext JSON if signing failed.</returns>
    Task<JObject> SendManyAsync(string fromAddress, IEnumerable<RpcTransferOut> outputs);

    /// <summary>
    /// Transfer asset from the wallet to the destination address.
    /// </summary>
    /// <returns>This function returns Signed Transaction JSON if successful, ContractParametersContext JSON if signing failed.</returns>
    Task<JObject> SendToAddressAsync(string assetId, string address, string amount);

    /// <summary>
    /// Returns the contract log based on the specified txHash. The complete contract logs are stored under the ApplicationLogs directory.
    /// This method is provided by the plugin ApplicationLogs.
    /// </summary>
    Task<RpcApplicationLog> GetApplicationLogAsync(string txHash);

    /// <summary>
    /// Returns the contract log based on the specified txHash. The complete contract logs are stored under the ApplicationLogs directory.
    /// This method is provided by the plugin ApplicationLogs.
    /// </summary>
    Task<RpcApplicationLog> GetApplicationLogAsync(string txHash, TriggerType triggerType);

    /// <summary>
    /// Returns all the NEP-17 transaction information occurred in the specified address.
    /// This method is provided by the plugin RpcNep17Tracker.
    /// </summary>
    /// <param name="address">The address to query the transaction information.</param>
    /// <param name="startTimestamp">The start block Timestamp, default to seven days before UtcNow</param>
    /// <param name="endTimestamp">The end block Timestamp, default to UtcNow</param>
    Task<RpcNep17Transfers> GetNep17TransfersAsync(string address, ulong? startTimestamp = default, ulong? endTimestamp = default);

    /// <summary>
    /// Returns the balance of all NEP-17 assets in the specified address.
    /// This method is provided by the plugin RpcNep17Tracker.
    /// </summary>
    Task<RpcNep17Balances> GetNep17BalancesAsync(string address);
}
