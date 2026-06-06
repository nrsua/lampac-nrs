using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EpWatch.Services;

public sealed class TelegramApi
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(70) };

    readonly string token;

    public TelegramApi(string token) { this.token = token; }

    public async Task<JObject> CallAsync(string method, object body, CancellationToken ct, TimeSpan? timeout = null)
    {
        var url = $"https://api.telegram.org/bot{token}/{method}";
        HttpResponseMessage resp;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) cts.CancelAfter(timeout.Value);

        if (body == null)
        {
            resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
        }
        else
        {
            var json = JsonConvert.SerializeObject(body, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            resp = await http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
        }

        var raw = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try { return JObject.Parse(raw); }
        catch { return null; }
    }

    public async Task<JObject> GetMeAsync(CancellationToken ct)
    {
        var r = await CallAsync("getMe", null, ct);
        return r?["result"] as JObject;
    }

    public Task DeleteWebhookAsync(CancellationToken ct)
        => CallAsync("deleteWebhook", new { drop_pending_updates = false }, ct);

    public async Task<JArray> GetUpdatesAsync(int? offset, int timeoutSeconds, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["timeout"] = timeoutSeconds,
            ["limit"] = 100
        };
        if (offset.HasValue) body["offset"] = offset.Value;

        var r = await CallAsync("getUpdates", body, ct, timeout: TimeSpan.FromSeconds(timeoutSeconds + 10));
        return r?["result"] as JArray;
    }

    public Task SendMessageAsync(long chatId, string text, object replyMarkup, string parseMode, CancellationToken ct)
        => CallAsync("sendMessage", new
        {
            chat_id = chatId,
            text,
            parse_mode = parseMode,
            reply_markup = replyMarkup
        }, ct);

    public Task SendPhotoAsync(long chatId, string photoUrl, string caption, string parseMode, CancellationToken ct)
        => CallAsync("sendPhoto", new
        {
            chat_id = chatId,
            photo = photoUrl,
            caption,
            parse_mode = parseMode
        }, ct);

    public Task SetMyCommandsAsync(object[] commands, object scope, string languageCode, CancellationToken ct)
        => CallAsync("setMyCommands", new
        {
            commands,
            scope,
            language_code = languageCode
        }, ct);

    public Task AnswerCallbackQueryAsync(string id, string text, CancellationToken ct)
        => CallAsync("answerCallbackQuery", new
        {
            callback_query_id = id,
            text
        }, ct);
}

public static class TgMarkup
{
    public static object ReplyKeyboard(string[][] rows, bool resize = true)
    {
        var keyboard = rows.Select(r => r.Select(text => new { text }).ToArray()).ToArray();
        return new { keyboard, resize_keyboard = resize };
    }

    public static object RemoveKeyboard() => new { remove_keyboard = true };

    public static object InlineKeyboard(params (string text, string data)[][] rows)
    {
        var inline_keyboard = rows
            .Select(r => r.Select(b => new { text = b.text, callback_data = b.data }).ToArray())
            .ToArray();
        return new { inline_keyboard };
    }

    public static object InlineKeyboardFromList(List<(string text, string data)[]> rows)
    {
        var inline_keyboard = rows
            .Select(r => r.Select(b => new { text = b.text, callback_data = b.data }).ToArray())
            .ToArray();
        return new { inline_keyboard };
    }
}
