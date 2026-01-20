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
/// IConfigService - Interface สำหรับจัดการ Configuration
///
/// หน้าที่หลัก:
/// 1. โหลด config จากไฟล์ appsettings.json
/// 2. บันทึก config กลับไฟล์
/// 3. ดึงค่า API Key จาก Environment Variables
/// 4. Validate config
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Event เมื่อ config เปลี่ยน
    /// </summary>
    event EventHandler<AppConfig>? ConfigChanged;

    /// <summary>
    /// ดึง config ปัจจุบัน
    /// </summary>
    AppConfig GetConfig();

    /// <summary>
    /// โหลด config จากไฟล์
    /// </summary>
    Task<AppConfig> LoadConfigAsync();

    /// <summary>
    /// บันทึก config ลงไฟล์
    /// </summary>
    Task SaveConfigAsync(AppConfig config);

    /// <summary>
    /// อัพเดท config บางส่วน
    /// </summary>
    Task UpdateConfigAsync(Action<AppConfig> updateAction);

    /// <summary>
    /// ตรวจสอบ config ว่าถูกต้องหรือไม่
    /// </summary>
    /// <returns>รายการ error (ถ้ามี)</returns>
    List<string> ValidateConfig(AppConfig config);

    /// <summary>
    /// ดึง API Key จาก Environment Variable
    /// </summary>
    /// <param name="envVarName">ชื่อ Environment Variable</param>
    /// <returns>ค่า API Key หรือ null ถ้าไม่พบ</returns>
    string? GetApiKey(string envVarName);

    /// <summary>
    /// ดึง API Secret จาก Environment Variable
    /// </summary>
    string? GetApiSecret(string envVarName);

    /// <summary>
    /// ตรวจสอบว่า API Key/Secret ถูกตั้งค่าหรือยัง
    /// </summary>
    bool HasValidCredentials(ExchangeConfig exchangeConfig);

    /// <summary>
    /// รีโหลด config จากไฟล์ (ใช้เมื่อไฟล์ถูกแก้ไขจากภายนอก)
    /// </summary>
    Task ReloadConfigAsync();

    /// <summary>
    /// Export config เป็น JSON string
    /// </summary>
    string ExportConfigJson();

    /// <summary>
    /// Import config จาก JSON string
    /// </summary>
    AppConfig ImportConfigJson(string json);
}
