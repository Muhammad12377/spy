using DeviceTracker.Models.Command;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace DeviceTracker.Services.Command;

/// <summary>
/// يسحب الأوامر الجديدة من Supabase (polling)
/// وينفذها عبر CommandExecutor
/// </summary>
public sealed class CommandReceiverService
{
    private readonly HttpClient _http;
    private readonly CommandExecutor _executor;
    private readonly SupabaseService _supabase;
    private CancellationTokenSource? _cts;
    private const int PollingIntervalSeconds = 30;

    public CommandReceiverService(CommandExecutor executor, SupabaseService supabase)
    {
        _executor = executor;
        _supabase = supabase;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        var url = Preferences.Get("supabase_url", "");
        var key = Preferences.Get("supabase_anon_key", "");
        _http.BaseAddress = new Uri(url.TrimEnd('/') + "/rest/v1/");
        _http.DefaultRequestHeaders.Add("apikey", key);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", key);
        _http.DefaultRequestHeaders.Add("Prefer", "return=minimal");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var serial = Preferences.Get("device_serial", "");
                if (!string.IsNullOrEmpty(serial))
                {
                    // سحب الأوامر المعلقة من Supabase
                    var response = await _http.GetAsync(
                        $"remote_commands?device_serial=eq.{serial}&status=eq.pending&order=sent_at.asc&limit=10",
                        ct);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(ct);
                        var commands = JsonConvert.DeserializeObject<List<RemoteCommand>>(json);

                        if (commands != null)
                        {
                            foreach (var cmd in commands)
                            {
                                // تنفيذ الأمر
                                await _executor.ExecuteAsync(cmd);

                                // تحديث الحالة عبر Edge Function
                                await _supabase.UpdateCommandStatusAsync(cmd.Id, "executed");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CmdReceiver] Poll error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), ct);
        }
    }
}
