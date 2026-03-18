# AutoTrade-X Connection Status Debug - Handoff Document

## สรุปปัญหา (Problem Summary)

**ปัญหาหลัก**: เมื่อผู้ใช้กด Save API credentials และ Test Connection สำเร็จในหน้า Settings แต่หน้าอื่นๆ (Dashboard, Trading, Scanner) ไม่รับรู้ว่า exchange เชื่อมต่อแล้ว - ทำให้ทุกหน้าแสดงว่า "ไม่ได้เชื่อมต่อ"

**อาการที่เห็น**:
- Settings page: กด Test Connection → แสดง "Connected ✓"
- Dashboard/Trading page: แสดง "No API Key" หรือ "Not Connected"
- Status bar: ไม่แสดง logo ของ exchange ที่เชื่อมต่อแล้ว

---

## การวิเคราะห์ที่ทำไปแล้ว

### 1. Flow การ Save/Load Credentials
- **Save**: `SettingsPage.SaveCredentialsToFile()` → บันทึกไฟล์ `credentials.encrypted.json`
- **Load**: `App.LoadSavedCredentialsToEnvironment()` → อ่านไฟล์และ set เป็น Environment Variables
- **Status**: ✅ ถูกต้อง - credentials ถูก save และ load เป็น env vars ก่อน services ถูกสร้าง

### 2. Flow การ Test Connection (Settings Page)
```
TestExchangeConnection()
  → Set env vars สำหรับ credentials ที่กรอก
  → CreateRealClient(exchangeName)
  → client.TestConnectionAsync()
  → ถ้าสำเร็จ: MarkExchangeAsVerified(exchangeName)
```
- **Status**: ✅ ถูกต้อง - เรียก MarkExchangeAsVerified เมื่อ test สำเร็จ

### 3. Flow การตรวจสอบ Connection ของหน้าอื่นๆ
```
Dashboard/MainWindow เรียก CheckAllConnectionsAsync()
  → วน loop SupportedExchanges
  → CheckExchangeConnectionAsync(exchangeName)
    → ตรวจสอบ env vars มี credentials หรือไม่
    → ถ้าไม่มี: return "API key not configured" (ก่อนเช็ค cache!)
    → ถ้ามี: เช็ค GetVerifiedStatus(exchangeName)
      → ถ้ามี cache: return cached status
      → ถ้าไม่มี: ทดสอบ GetBalanceAsync()
```

### 4. ConnectionStatusService เป็น Singleton
- **Status**: ✅ ถูกต้อง - ใช้ instance เดียวกันทั้ง app

### 5. Verified Cache
- **_verifiedExchanges**: Dictionary เก็บ exchange ที่ test ผ่านแล้ว
- **Key**: exchangeName.ToLower() (เช่น "bitkub")
- **Status**: Logic ถูกต้อง แต่ต้องยืนยันด้วย debug log

---

## Debug Logging ที่เพิ่มไปแล้ว

### ไฟล์: `ConnectionStatusService.cs`

1. **CheckExchangeConnectionAsync()** - เพิ่ม log:
   - `CheckExchangeConnectionAsync: Checking '{exchangeName}'`
   - `EnvVars: {apiKeyEnv}=True/False, {apiSecretEnv}=True/False`
   - `No credentials, returning early` (ถ้าไม่มี credentials)

2. **MarkExchangeAsVerified()** - เพิ่ม log:
   - `Adding '{key}' to verified cache`
   - `'{key}' added. Cache now has X entries: [list of keys]`

3. **GetVerifiedStatus()** - เพิ่ม log:
   - `Looking for '{key}', cache has X entries: [list of keys]`
   - `FOUND '{key}' in cache!` หรือ `'{key}' NOT found in cache`

---

## สิ่งที่ต้องทดสอบ (Next Steps)

### 1. รัน App และดู Log
```
1. รัน app ใหม่
2. ดู log ว่า exchanges มี credentials หรือไม่ตั้งแต่เริ่มต้น
3. ไป Settings → ใส่ API key → กด Save → กด Test Connection
4. กลับไป Dashboard
5. ดู log อีกครั้ง
```

### 2. สิ่งที่ต้องดูใน Log
- `CheckExchangeConnectionAsync: 'Bitkub' - EnvVars: AUTOTRADEX_BITKUB_API_KEY=True, ...`
- `MarkExchangeAsVerified: 'bitkub' added. Cache now has 1 entries: [bitkub]`
- `GetVerifiedStatus: Looking for 'bitkub', cache has 1 entries: [bitkub]`
- `GetVerifiedStatus: FOUND 'bitkub' in cache!`

### 3. ปัญหาที่อาจพบ
- **ถ้า EnvVars=False**: Credentials ไม่ถูก load - ตรวจสอบไฟล์ credentials.encrypted.json
- **ถ้า Cache has 0 entries หลัง MarkExchangeAsVerified**: มีปัญหากับ singleton หรือ threading
- **ถ้า NOT found in cache แม้เพิ่งเพิ่ม**: Key case mismatch

---

## ไฟล์สำคัญที่เกี่ยวข้อง

| ไฟล์ | หน้าที่ |
|------|---------|
| `src/AutoTradeX.UI/App.xaml.cs` | Load credentials เมื่อ app เริ่ม |
| `src/AutoTradeX.UI/Views/SettingsPage.xaml.cs` | Save credentials, Test connection |
| `src/AutoTradeX.Infrastructure/Services/ConnectionStatusService.cs` | Verified cache, Check connections |
| `src/AutoTradeX.Infrastructure/ExchangeClients/ExchangeClientFactory.cs` | สร้าง exchange clients |
| `src/AutoTradeX.Infrastructure/ExchangeClients/BitkubClient.cs` | Bitkub API implementation |
| `src/AutoTradeX.UI/Views/DashboardPage.xaml.cs` | แสดง exchange status |
| `src/AutoTradeX.UI/Views/MainWindow.xaml.cs` | Status bar, Connection monitoring |

---

## Potential Fixes ที่อาจต้องทำ

### Fix 1: ตรวจสอบ Verified Cache ก่อน Credentials Check
ปัจจุบัน code return ถ้าไม่มี credentials **ก่อน** เช็ค verified cache:
```csharp
if (!hasCredentials)
{
    status.IsConnected = false;
    status.ErrorMessage = "API key not configured";
    return status;  // <-- Return ก่อนเช็ค cache!
}

var verifiedStatus = GetVerifiedStatus(exchangeName);
```

อาจต้องสลับลำดับ - เช็ค cache ก่อน แล้วค่อยเช็ค credentials

### Fix 2: Force Update UI หลัง MarkExchangeAsVerified
ตรวจสอบว่า `ConnectionStatusChanged` event ถูก fire และ UI ได้รับ event จริงหรือไม่

### Fix 3: Ensure Singleton Instance ถูก Share
ตรวจสอบว่าทุก page ใช้ `App.Services.GetService<IConnectionStatusService>()` ไม่ใช่ `new ConnectionStatusService()`

---

## Log File Location

Log ถูกเขียนโดย `FileLoggingService` ที่:
```
%APPDATA%\AutoTradeX\logs\
```

---

## วันที่สร้างเอกสาร
2026-01-26

## Status
**IN PROGRESS** - รอทดสอบและดู debug log เพื่อระบุปัญหาที่แท้จริง
