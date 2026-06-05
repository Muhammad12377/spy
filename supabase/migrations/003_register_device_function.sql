-- ============================================================
-- Migration 003: Register device via SECURITY DEFINER function
-- Allows anon key to register devices (bypasses RLS)
-- Call via: POST /rest/v1/rpc/register_device
-- ============================================================

CREATE OR REPLACE FUNCTION register_device(
  p_device_serial TEXT,
  p_device_name TEXT DEFAULT NULL,
  p_manufacturer TEXT DEFAULT NULL,
  p_model TEXT DEFAULT NULL,
  p_os_version TEXT DEFAULT NULL,
  p_public_key TEXT DEFAULT NULL
)
RETURNS TABLE (device_token TEXT)
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  new_token TEXT;
BEGIN
  -- إنشاء token فريد
  new_token := encode(gen_random_bytes(48), 'base64');
  new_token := regexp_replace(new_token, '[^a-zA-Z0-9]', '', 'g');
  new_token := substring(new_token, 1, 64);

  -- إدراج أو تحديث الجهاز
  INSERT INTO devices (device_serial, device_token, device_name, manufacturer, model, os_version)
  VALUES (p_device_serial, new_token, p_device_name, p_manufacturer, p_model, p_os_version)
  ON CONFLICT (device_serial)
  DO UPDATE SET
    device_token = new_token,
    device_name = COALESCE(p_device_name, devices.device_name),
    manufacturer = COALESCE(p_manufacturer, devices.manufacturer),
    model = COALESCE(p_model, devices.model),
    os_version = COALESCE(p_os_version, devices.os_version),
    is_active = true;

  -- تسجيل في سجل المصادقة
  INSERT INTO device_auth_log (device_serial, token_used, action)
  VALUES (p_device_serial, new_token, 'token_rotated');

  -- تسجيل مفتاح التشفير
  IF p_public_key IS NOT NULL AND p_public_key != '' THEN
    INSERT INTO device_encryption_keys (device_serial, public_key, key_algorithm)
    VALUES (p_device_serial, p_public_key, 'AES-256-GCM')
    ON CONFLICT (device_serial)
    DO UPDATE SET public_key = p_public_key, rotated_at = now();
  END IF;

  RETURN QUERY SELECT new_token;
END;
$$;

-- السماح للـ anon key باستخدام هذه الدالة
GRANT EXECUTE ON FUNCTION register_device TO anon;
