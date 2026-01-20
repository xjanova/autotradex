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
}

/// <summary>
/// IExchangeClientFactory - Factory สำหรับสร้าง Exchange Client
/// </summary>
public interface IExchangeClientFactory
{
    /// <summary>
    /// สร้าง Exchange Client ตามชื่อ
    /// </summary>
    /// <param name="exchangeName">ชื่อ Exchange</param>
    /// <returns>Exchange Client</returns>
    IExchangeClient CreateClient(string exchangeName);

    /// <summary>
    /// ดึงรายชื่อ Exchange ที่รองรับ
    /// </summary>
    IEnumerable<string> GetSupportedExchanges();
}
