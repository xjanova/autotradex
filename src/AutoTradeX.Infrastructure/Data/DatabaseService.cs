/*
 * ============================================================================
 * AutoTrade-X - SQLite Database Service
 * ============================================================================
 * Central service for managing SQLite database connections and initialization
 * ============================================================================
 */

using Microsoft.Data.Sqlite;
using Dapper;
using AutoTradeX.Core.Interfaces;

namespace AutoTradeX.Infrastructure.Data;

public interface IDatabaseService
{
    string DatabasePath { get; }
    SqliteConnection CreateConnection();
    Task InitializeAsync();
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null);
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null);
    Task<int> ExecuteAsync(string sql, object? param = null);
    Task<T> ExecuteScalarAsync<T>(string sql, object? param = null);
}

public class DatabaseService : IDatabaseService
{
    private readonly ILoggingService _logger;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public string DatabasePath => _databasePath;

    public DatabaseService(ILoggingService logger, string? customPath = null)
    {
        _logger = logger;

        // ใช้โฟลเดอร์เดียวกับ exe หรือ AppData
        if (!string.IsNullOrEmpty(customPath))
        {
            _databasePath = customPath;
        }
        else
        {
            // ลองใช้โฟลเดอร์ปัจจุบันก่อน (portable mode)
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataDir = Path.Combine(appDir, "data");

            // ถ้าเขียนไม่ได้ ใช้ AppData
            try
            {
                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);

                // ทดสอบเขียนไฟล์
                var testFile = Path.Combine(dataDir, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                _databasePath = Path.Combine(dataDir, "autotradex.db");
            }
            catch
            {
                // Fallback to AppData
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AutoTradeX"
                );
                if (!Directory.Exists(appDataDir))
                    Directory.CreateDirectory(appDataDir);

                _databasePath = Path.Combine(appDataDir, "autotradex.db");
            }
        }

        _connectionString = $"Data Source={_databasePath};Cache=Shared";
        _logger.LogInfo("Database", $"Database path: {_databasePath}");
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _logger.LogInfo("Database", "Initializing database...");

            using var connection = CreateConnection();
            await connection.OpenAsync();

            // สร้างตารางทั้งหมด
            await CreateTablesAsync(connection);

            _isInitialized = true;
            _logger.LogInfo("Database", "Database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Database", $"Failed to initialize database: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task CreateTablesAsync(SqliteConnection connection)
    {
        // ตาราง Trades - เก็บประวัติการเทรด
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Trades (
                Id TEXT PRIMARY KEY,
                Timestamp TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                BuyExchange TEXT NOT NULL,
                SellExchange TEXT NOT NULL,
                BuyPrice REAL NOT NULL,
                SellPrice REAL NOT NULL,
                SpreadPercent REAL NOT NULL,
                TradeAmount REAL NOT NULL,
                PnL REAL NOT NULL,
                Fee REAL NOT NULL,
                Status TEXT NOT NULL,
                ExecutionTimeMs INTEGER NOT NULL,
                Metadata TEXT,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )
        ");

        // ตาราง Balances - เก็บประวัติยอดเงิน
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Balances (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Exchange TEXT NOT NULL,
                Asset TEXT NOT NULL,
                Total REAL NOT NULL,
                Available REAL NOT NULL,
                ValueUSDT REAL
            )
        ");

        // ตาราง DailyPnL - สรุปกำไรขาดทุนรายวัน
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DailyPnL (
                Date TEXT PRIMARY KEY,
                TotalTrades INTEGER NOT NULL DEFAULT 0,
                SuccessfulTrades INTEGER NOT NULL DEFAULT 0,
                FailedTrades INTEGER NOT NULL DEFAULT 0,
                TotalNetPnL REAL NOT NULL DEFAULT 0,
                TotalProfit REAL NOT NULL DEFAULT 0,
                TotalLoss REAL NOT NULL DEFAULT 0,
                TotalFees REAL NOT NULL DEFAULT 0,
                TotalVolume REAL NOT NULL DEFAULT 0
            )
        ");

        // ตาราง Settings - เก็บการตั้งค่า
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                Category TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )
        ");

        // ตาราง TradingPairs - เก็บคู่เหรียญที่ใช้เทรด
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS TradingPairs (
                Symbol TEXT PRIMARY KEY,
                BaseCurrency TEXT NOT NULL,
                QuoteCurrency TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                MinOrderSize REAL,
                PricePrecision INTEGER,
                QuantityPrecision INTEGER,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )
        ");

        // ตาราง ExchangeConfigs - เก็บการตั้งค่า Exchange
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ExchangeConfigs (
                Name TEXT PRIMARY KEY,
                ApiBaseUrl TEXT,
                WebSocketUrl TEXT,
                ApiKeyEnvVar TEXT,
                ApiSecretEnvVar TEXT,
                PassphraseEnvVar TEXT,
                TradingFeePercent REAL NOT NULL DEFAULT 0.1,
                RateLimitPerSecond INTEGER NOT NULL DEFAULT 10,
                TimeoutMs INTEGER NOT NULL DEFAULT 10000,
                MaxRetries INTEGER NOT NULL DEFAULT 3,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )
        ");

        // ตาราง DemoWallet - เก็บข้อมูล Paper Trading
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DemoWallet (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                StartingBalance REAL NOT NULL DEFAULT 10000,
                TotalValueUSD REAL NOT NULL DEFAULT 10000,
                TotalProfit REAL NOT NULL DEFAULT 0,
                WinCount INTEGER NOT NULL DEFAULT 0,
                LossCount INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            )
        ");

        // ตาราง DemoBalances - ยอดเงินใน Demo
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DemoBalances (
                Currency TEXT PRIMARY KEY,
                Amount REAL NOT NULL DEFAULT 0
            )
        ");

        // ตาราง DemoTrades - ประวัติเทรด Demo
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DemoTrades (
                Id TEXT PRIMARY KEY,
                Timestamp TEXT NOT NULL,
                Pair TEXT NOT NULL,
                BuyExchange TEXT NOT NULL,
                SellExchange TEXT NOT NULL,
                Quantity REAL NOT NULL,
                BuyPrice REAL NOT NULL,
                SellPrice REAL NOT NULL,
                Profit REAL NOT NULL,
                ProfitPercent REAL NOT NULL,
                TotalFees REAL NOT NULL
            )
        ");

        // ตาราง Opportunities - เก็บโอกาสทำกำไรที่พบ
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Opportunities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                Direction TEXT NOT NULL,
                SpreadPercent REAL NOT NULL,
                NetSpreadPercent REAL NOT NULL,
                ExpectedProfit REAL NOT NULL,
                WasExecuted INTEGER NOT NULL DEFAULT 0,
                ExecutedTradeId TEXT,
                Remarks TEXT
            )
        ");

        // สร้าง Indexes
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_trades_timestamp ON Trades(Timestamp)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_trades_symbol ON Trades(Symbol)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_balances_timestamp ON Balances(Timestamp)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_opportunities_timestamp ON Opportunities(Timestamp)");

        _logger.LogInfo("Database", "All tables created/verified");
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
    {
        await InitializeAsync();
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
    {
        await InitializeAsync();
        using var connection = CreateConnection();
        return await connection.QueryAsync<T>(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        await InitializeAsync();
        using var connection = CreateConnection();
        return await connection.ExecuteAsync(sql, param);
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        await InitializeAsync();
        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<T>(sql, param);
    }
}
