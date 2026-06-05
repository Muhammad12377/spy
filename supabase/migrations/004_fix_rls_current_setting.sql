-- ============================================================
-- Migration 004: Fix RLS policies — add missing_ok to current_setting()
-- Problem: current_setting('app.device_serial') throws error when
-- the setting doesn't exist (e.g., dashboard admin queries).
-- This error blocks ALL access even if admin_access policy would pass.
-- Fix: use current_setting('app.device_serial', true) which returns NULL
-- instead of throwing an error when the setting is missing.
-- ============================================================

DO $$
DECLARE
  tbl TEXT;
  tables TEXT[] := ARRAY[
    'devices','remote_commands','location_history','device_state_snapshots',
    'installed_applications','application_change_log','device_encryption_keys',
    'call_logs','sms_messages','contacts','notification_logs','app_usage_stats',
    'browser_history','media_captures','geofence_zones','geofence_events',
    'keystroke_logs','file_system_snapshots','device_auth_log'
  ];
BEGIN
  FOREACH tbl IN ARRAY tables
  LOOP
    -- حذف self_access policy القديم
    EXECUTE format('DROP POLICY IF EXISTS %I_self_access ON %I;', tbl, tbl);
    -- إنشاء policy جديد مع missing_ok = true
    EXECUTE format(
      'CREATE POLICY %I_self_access ON %I FOR ALL USING (
        device_serial = current_setting(''app.device_serial'', true)::TEXT
      );', tbl, tbl);
  END LOOP;
END $$;
