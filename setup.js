/**
 * سكريبت إعداد Supabase — يقوم بتطبيق SQL schema تلقائياً
 * 
 * الاستخدام:
 * 1. تأكد من وجود Node.js
 * 2. شغّل: npm install @supabase/supabase-js
 * 3. شغّل: node setup.js
 * 
 * ملاحظة: يتطلب Service Role Key (من Supabase Dashboard → Settings → API)
 * لأن Anon Key لا يملك صلاحية CREATE TABLE.
 */

const SUPABASE_URL = "https://accisrkoevfqqiwglswe.supabase.co";
const SUPABASE_SERVICE_KEY = "YOUR_SERVICE_ROLE_KEY"; // ← ضع الـ Service Role Key هنا

async function main() {
  if (SUPABASE_SERVICE_KEY === "YOUR_SERVICE_ROLE_KEY") {
    console.log("❌ الرجاء وضع Service Role Key في المتغير SUPABASE_SERVICE_KEY");
    console.log("   اذهب إلى: Supabase Dashboard → Settings → API → service_role key");
    process.exit(1);
  }

  const { createClient } = await import("@supabase/supabase-js");
  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);

  const fs = await import("fs");
  const schema = fs.readFileSync("supabase-schema.sql", "utf8");

  console.log("📦 جاري تطبيق SQL schema...");

  const { error } = await supabase.rpc("exec_sql", { query: schema });

  if (error) {
    // الطريقة المباشرة: تقسيم الاستعلامات
    console.log("⚠️  RPC غير متاح، جرب الطريقة المباشرة...");
    
    const statements = schema
      .split(";")
      .map(s => s.trim())
      .filter(s => s.length > 0 && !s.startsWith("--"));

    let success = 0;
    let failed = 0;

    for (const stmt of statements) {
      try {
        const { error: e } = await supabase.rpc("exec_sql", { query: stmt + ";" });
        if (e) {
          // تجاهل أخطاء "already exists"
          if (e.message?.includes("already exists")) {
            success++;
            continue;
          }
          failed++;
          console.log(`  ⚠️  فشل: ${e.message?.substring(0, 80)}`);
        } else {
          success++;
        }
      } catch {
        failed++;
      }
    }

    console.log(`\n✅ نجح: ${success} | ❌ فشل: ${failed}`);
    console.log("\n💡 نصيحة: إذا فشل معظمها، استخدم Supabase Dashboard:");
    console.log("   1. افتح https://supabase.com/dashboard/project/accisrkoevfqqiwglswe");
    console.log("   2. اذهب إلى SQL Editor");
    console.log("   3. الصق محتوى supabase-schema.sql بالكامل");
    console.log("   4. اضغط Run");
  } else {
    console.log("✅ تم تطبيق schema بنجاح!");
  }
}

main().catch(console.error);
