/**
 * سكريبت إعداد Supabase
 * يقوم بتطبيق SQL schema تلقائياً
 * 
 * المتطلبات: Node.js + npm
 * 
 * خطوات التشغيل:
 *   cd scripts
 *   npm install @supabase/supabase-js node-fetch
 *   node setup-supabase.js
 */

const SUPABASE_URL = "https://accisrkoevfqqiwglswe.supabase.co";
const ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImFjY2lzcmtvZXZmcXFpd2dsc3dlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzkzMjAzNzQsImV4cCI6MjA5NDg5NjM3NH0.xJWT0Ft5PE3b7F5UZ4DorYLTr3ykM5wU1LVuvt_RuXQ";

async function applyViaManagementAPI() {
  // المحاولة عبر Management API (تحتاج PAT)
  const PAT = process.env.SUPABASE_PAT || "";
  if (!PAT) {
    console.log("⚠️  لم يتم تعيين SUPABASE_PAT");
    console.log("   الرجاء إنشاء Personal Access Token من:");
    console.log("   https://supabase.com/dashboard/account/tokens");
    console.log("   ثم شغّل: $env:SUPABASE_PAT='your-token'");
    return false;
  }

  const { default: fetch } = await import("node-fetch");
  const fs = await import("fs");
  const schema = fs.readFileSync("../supabase-schema.sql", "utf8");

  console.log("📡 جاري الاتصال بـ Supabase Management API...");

  const response = await fetch(
    `https://api.supabase.com/v1/projects/accisrkoevfqqiwglswe/database/query`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${PAT}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ query: schema }),
    }
  );

  if (response.ok) {
    console.log("✅ تم تطبيق SQL schema بنجاح!");
    return true;
  } else {
    const err = await response.text();
    console.log(`❌ فشل: ${err.substring(0, 200)}`);
    return false;
  }
}

async function main() {
  console.log("=== Supabase Setup Script ===");
  console.log(`Project: ${SUPABASE_URL}`);
  console.log("");

  // محاولة 1: Management API
  console.log("1️⃣  محاولة عبر Management API...");
  const done = await applyViaManagementAPI();
  if (done) return;

  // إذا فشلت، إظهار التعليمات اليدوية
  console.log("");
  console.log("2️⃣  التعليمات اليدوية:");
  console.log("   =====================");
  console.log("   1. افتح المتصفح على الرابط:");
  console.log("   https://supabase.com/dashboard/project/accisrkoevfqqiwglswe/sql/new");
  console.log("");
  console.log("   2. الصق محتوى الملف التالي بالكامل:");
  console.log(`   ${__dirname}\\..\\supabase-schema.sql`);
  console.log("");
  console.log("   3. اضغط على زر 'Run' أو Ctrl+Enter");
  console.log("");
  console.log("   4. انتظر حتى تظهر 'Success. No rows returned'");
  console.log("");
  console.log("3️⃣  بعد تطبيق schema، اختبر الاتصال:");
  console.log("   node -e \"require('@supabase/supabase-js'); ...\"");
}

main().catch(console.error);
