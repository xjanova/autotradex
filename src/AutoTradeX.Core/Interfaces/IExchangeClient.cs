/*
 * ============================================================================
 * AutoTrade-X - Cross-Exchange Arbitrage Trading Bot
 * ============================================================================
 * ⚠️ Educational/Experimental Only - No profit guarantee
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Interfaces;

/// <summary>
/// IExchangeClient - Interface สำหรับเชื่อมต่อกับ Exchange
/// แต่ละ Exchange ต้อง implement interface นี้
///
/// หน้าที่หลัก:
/// 1. ดึงข้อมูลราคา (Ticker, Order Book)
/// 2. ดึงข้อมูล Balance
/// 3. สร้างและจัดการ Order
/// </summary>
public interface IExchangeClient : IDisposable
{
    /// <summary>
    /// ชื่อ Exchange
    /// </summary>
    string ExchangeName { get; }

    /// <summary>
    /// สถานะการเชื่อมต่อ
    /// </summary>
    bool IsConnected { get; }

    // ========== Market Data ==========

    /// <summary>
    /// ดึงข้อมูล Ticker ของคู่เทรดที่ระบุ
    /// </summary>
    /// <param name="symbol">ชื่อคู่เทรด เช่น "BTCUSDT"</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ข้อมูล Ticker</returns>
    Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงข้อมูล Order Book
    /// </summary>
    /// <param name="symbol">ชื่อคู่เทรด</param>
    /// <param name="depth">จำนวนระดับราคาที่ต้องการ</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ข้อมูล Order Book</returns>
    Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึง Ticker หลายคู่พร้อมกัน (Batch)
    /// </summary>
    /// <param name="symbols">รายการคู่เทรด</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>Dictionary ของ Ticker โดย key คือ symbol</returns>
    Task<Dictionary<string, Ticker>> GetTickersAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึง Ticker ทั้งหมดจาก Exchange (สำหรับ scan pairs)
    /// </summary>
    /// <param name="quoteAsset">Quote asset ที่ต้องการกรอง เช่น "USDT", "THB" (null = ทั้งหมด)</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>Dictionary ของ Ticker ทั้งหมด</returns>
    Task<Dictionary<string, Ticker>> GetAllTickersAsync(string? quoteAsset = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงข้อมูล Klines (แท่งเทียน) สำหรับการวิเคราะห์ทางเทคนิค
    /// </summary>
    /// <param name="symbol">ชื่อคู่เทรด เช่น "BTCUSDT"</param>
    /// <param name="interval">ช่วงเวลา เช่น "1m", "5m", "15m", "1h", "4h", "1d"</param>
    /// <param name="limit">จำนวนแท่งเทียนที่ต้องการ (สูงสุด 500)</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>รายการแท่งเทียน</returns>
    Task<List<PriceCandle>> GetKlinesAsync(string symbol, string interval = "1m", int limit = 100, CancellationToken cancellationToken = default);

    // ========== Account Data ==========

    /// <summary>
    /// ดึงข้อมูล Balance ทั้งหมดในบัญชี
    /// </summary>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ข้อมูล Balance</returns>
    Task<AccountBalance> GetBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงข้อมูล Balance ของสินทรัพย์ที่ระบุ
    /// </summary>
    /// <param name="asset">ชื่อสินทรัพย์ เช่น "BTC", "USDT"</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ข้อมูล Balance ของสินทรัพย์นั้น</returns>
    Task<AssetBalance> GetAssetBalanceAsync(string asset, CancellationToken cancellationToken = default);

    // ========== Order Management ==========

    /// <summary>
    /// สร้าง Order ใหม่
    /// </summary>
    /// <param name="request">รายละเอียด Order ที่ต้องการสร้าง</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>Order ที่สร้างแล้ว</returns>
    Task<Order> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ยกเลิก Order
    /// </summary>
    /// <param name="symbol">คู่เทรด</param>
    /// <param name="orderId">Order ID ที่ต้องการยกเลิก</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>Order ที่ถูกยกเลิก</returns>
    Task<Order> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงสถานะ Order
    /// </summary>
    /// <param name="symbol">คู่เทรด</param>
    /// <param name="orderId">Order ID</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>Order ปัจจุบัน</returns>
    Task<Order> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึง Open Orders ทั้งหมด
    /// </summary>
    /// <param name="symbol">คู่เทรด (ถ้าไม่ระบุจะดึงทั้งหมด)</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>รายการ Open Orders</returns>
    Task<List<Order>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default);

    // ========== Transfer Operations / การโอนเหรียญ ==========

    /// <summary>
    /// ดึงที่อยู่ฝากเหรียญ (Deposit Address)
    /// </summary>
    /// <param name="asset">ชื่อเหรียญ เช่น "BTC", "ETH"</param>
    /// <param name="network">เครือข่าย เช่น "BTC", "ERC20", "TRC20" (null = default)</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ที่อยู่ฝากและ memo (ถ้ามี)</returns>
    Task<DepositAddressInfo> GetDepositAddressAsync(string asset, string? network = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงค่าธรรมเนียมถอนเหรียญ
    /// </summary>
    /// <param name="asset">ชื่อเหรียญ</param>
    /// <param name="network">เครือข่าย (null = default)</param>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ค่าธรรมเนียมถอน</returns>
    Task<WithdrawalFeeInfo> GetWithdrawalFeeAsync(string asset, string? network = null, CancellationToken cancellationToken = default);

    // ========== Connection Management ==========

    /// <summary>
    /// เริ่มการเชื่อมต่อ
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ปิดการเชื่อมต่อ
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// ทดสอบการเชื่อมต่อและ API Key
    /// </summary>
    /// <returns>true ถ้าเชื่อมต่อได้และ API Key ถูกต้อง</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ดึงข้อมูล Permissions ของ API Key
    /// </summary>
    /// <param name="cancellationToken">Token สำหรับยกเลิก</param>
    /// <returns>ข้อมูล API Permissions</returns>
    Task<ApiPermissionInfo> GetApiPermissionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ข้อมูลที่อยู่ฝากเหรียญ
/// Deposit address information
/// </summary>
public class DepositAddressInfo
{
    /// <summary>ที่อยู่ฝาก / Deposit address</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Memo/Tag (สำหรับเหรียญเช่น XRP, EOS, ATOM)</summary>
    public string? Memo { get; set; }

    /// <summary>เครือข่ายที่ใช้ / Network used</summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>เหรียญ / Asset</summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>จำนวนฝากขั้นต่ำ / Minimum deposit amount</summary>
    public decimal MinDepositAmount { get; set; }

    /// <summary>จำนวน confirmations ที่ต้องการ / Required confirmations</summary>
    public int RequiredConfirmations { get; set; }
}

/// <summary>
/// ข้อมูลค่าธรรมเนียมถอนเหรียญ
/// Withdrawal fee information
/// </summary>
public class WithdrawalFeeInfo
{
    /// <summary>เหรียญ / Asset</summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>เครือข่าย / Network</summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>ค่าธรรมเนียมถอน / Withdrawal fee</summary>
    public decimal Fee { get; set; }

    /// <summary>จำนวนถอนขั้นต่ำ / Minimum withdrawal amount</summary>
    public decimal MinWithdrawalAmount { get; set; }

    /// <summary>สามารถถอนได้หรือไม่ / Is withdrawal enabled</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// ข้อมูล API Permissions
/// </summary>
public class ApiPermissionInfo
{
    /// <summary>
    /// สิทธิ์อ่านข้อมูล (Balances, Orders, etc.)
    /// </summary>
    public bool CanRead { get; set; }

    /// <summary>
    /// สิทธิ์เทรด (Place/Cancel Orders)
    /// </summary>
    public bool CanTrade { get; set; }

    /// <summary>
    /// สิทธิ์ถอนเงิน
    /// </summary>
    public bool CanWithdraw { get; set; }

    /// <summary>
    /// สิทธิ์ฝากเงิน
    /// </summary>
    public bool CanDeposit { get; set; }

    /// <summary>
    /// IP ที่ถูกจำกัด (ถ้ามี)
    /// </summary>
    public string? IpRestriction { get; set; }

    /// <summary>
    /// ข้อความเพิ่มเติม
    /// </summary>
    public string? AdditionalInfo { get; set; }
}

/// <summary>
/// IExchangeClientFactory - Factory สำหรับสร้าง Exchange Client
/// </summary>
public interface IExchangeClientFactory
{
    /// <summary>
    /// สร้าง Exchange Client ตามชื่อ (respects LiveTrading flag - may return simulation client)
    /// </summary>
    /// <param name="exchangeName">ชื่อ Exchange</param>
    /// <returns>Exchange Client</returns>
    IExchangeClient CreateClient(string exchangeName);

    /// <summary>
    /// สร้าง Real Exchange Client สำหรับทดสอบการเชื่อมต่อ (ไม่ขึ้นกับ LiveTrading flag)
    /// Create real exchange client for connection testing (ignores LiveTrading flag)
    /// </summary>
    /// <param name="exchangeName">ชื่อ Exchange</param>
    /// <returns>Real Exchange Client</returns>
    IExchangeClient CreateRealClient(string exchangeName);

    /// <summary>
    /// ดึงรายชื่อ Exchange ที่รองรับ
    /// </summary>
    IEnumerable<string> GetSupportedExchanges();
}
