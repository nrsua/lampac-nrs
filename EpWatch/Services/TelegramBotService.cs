using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace EpWatch.Services;

public sealed class TelegramBotService : BackgroundService
{
    const int LongPollTimeout = 50;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!ModInit.conf.enable || string.IsNullOrWhiteSpace(ModInit.conf.bot_token))
            return;

        var bot = new TelegramApi(ModInit.conf.bot_token.Trim());

        try
        {
            var me = await bot.GetMeAsync(ct);
            if (me == null)
            {
                Console.WriteLine("[EpWatch] getMe returned empty response");
                return;
            }
            Notifier.Bot = bot;
            Notifier.BotUsername = me.Value<string>("username");
            Console.WriteLine($"[EpWatch] @{Notifier.BotUsername} ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] bot init failed: {ex.Message}");
            return;
        }

        try { await bot.DeleteWebhookAsync(ct); } catch { }

        await PublishCommands(bot, Strings.DefaultLang, null, ct);

        int? offset = null;
        var errDelay = TimeSpan.FromSeconds(5);

        while (!ct.IsCancellationRequested)
        {
            JArray updates;
            try
            {
                updates = await bot.GetUpdatesAsync(offset, LongPollTimeout, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[EpWatch] getUpdates: {ex.Message}");
                try { await Task.Delay(errDelay, ct); } catch { break; }
                continue;
            }

            if (updates == null) continue;

            foreach (var u in updates)
            {
                var id = u.Value<int?>("update_id") ?? 0;
                if (id > 0) offset = id + 1;

                try
                {
                    var msg = u["message"] as JObject;
                    var cb = u["callback_query"] as JObject;

                    if (msg != null && msg["text"] != null)
                        await HandleMessage(bot, msg, ct);
                    else if (cb != null)
                        await HandleCallback(bot, cb, ct);
                }
                catch (Exception ex) { Console.WriteLine($"[EpWatch] update: {ex.Message}"); }
            }
        }

        Notifier.Bot = null;
    }

    static object MainKb(string lang) => TgMarkup.ReplyKeyboard(new[]
    {
        new[] { Strings.T(lang, "btn_subs"), Strings.T(lang, "btn_check") },
        new[] { Strings.T(lang, "btn_help"), Strings.T(lang, "btn_unlink") }
    });

    static async Task PublishCommands(TelegramApi bot, string lang, long? chatId, CancellationToken ct)
    {
        var cmds = new object[]
        {
            new { command = "list",   description = Strings.T(lang, "cmd_list")   },
            new { command = "check",  description = Strings.T(lang, "cmd_check")  },
            new { command = "lang",   description = Strings.T(lang, "cmd_lang")   },
            new { command = "unlink", description = Strings.T(lang, "cmd_unlink") },
            new { command = "help",   description = Strings.T(lang, "cmd_help")   }
        };

        try
        {
            object scope = chatId.HasValue ? new { type = "chat", chat_id = chatId.Value } : null;
            await bot.SetMyCommandsAsync(cmds, scope, chatId.HasValue ? lang : null, ct);
        }
        catch { }
    }

    static async Task<string> GetLang(long chatId, CancellationToken ct)
    {
        using var db = SqlContext.Create();
        var u = await db.users.AsNoTracking().FirstOrDefaultAsync(x => x.chat_id == chatId, ct);
        return Strings.Normalize(u?.lang);
    }

    static async Task HandleMessage(TelegramApi bot, JObject msg, CancellationToken ct)
    {
        var chat = msg["chat"] as JObject;
        var from = msg["from"] as JObject;
        var chatId = chat?.Value<long?>("id") ?? 0;
        if (chatId == 0) return;
        var text = (msg.Value<string>("text") ?? "").Trim();

        if (text.StartsWith("/start"))
        {
            var parts = text.Split(' ', 2);
            var lang = Strings.Normalize(from?.Value<string>("language_code"));

            if (parts.Length == 2 && parts[1].StartsWith("link_"))
            {
                var uid = parts[1].Substring(5);
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    await LinkUser(uid, chatId, from?.Value<string>("username"), lang, ct);
                    await PublishCommands(bot, lang, chatId, ct);
                    await bot.SendMessageAsync(chatId, Strings.T(lang, "linked"), MainKb(lang), Notifier.PARSE_MODE, ct);
                    return;
                }
            }

            await bot.SendMessageAsync(chatId, Strings.T(lang, "welcome"), MainKb(lang), Notifier.PARSE_MODE, ct);
            return;
        }

        var L = await GetLang(chatId, ct);

        if (text == "/list" || text == Strings.T(L, "btn_subs"))
        {
            await ShowList(bot, chatId, L, ct);
        }
        else if (text == "/check" || text == Strings.T(L, "btn_check"))
        {
            await bot.SendMessageAsync(chatId, Strings.T(L, "checking"), null, null, ct);
            _ = Task.Run(async () =>
            {
                try { await EpisodeChecker.CheckOnceAsync(chatId, CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"[EpWatch] /check: {ex.Message}"); }
            });
        }
        else if (text == "/help" || text == Strings.T(L, "btn_help"))
        {
            await bot.SendMessageAsync(chatId, Strings.T(L, "help"), MainKb(L), Notifier.PARSE_MODE, ct);
        }
        else if (text == "/unlink" || text == Strings.T(L, "btn_unlink"))
        {
            await Unlink(chatId, ct);
            await bot.SendMessageAsync(chatId, Strings.T(L, "unlinked"), TgMarkup.RemoveKeyboard(), null, ct);
        }
        else if (text == "/lang")
        {
            var kb = TgMarkup.InlineKeyboard(
                new (string, string)[] { ("🇺🇦 Українська", "lang_uk") },
                new (string, string)[] { ("🇬🇧 English",    "lang_en") },
                new (string, string)[] { ("🇷🇺 Русский",    "lang_ru") }
            );
            await bot.SendMessageAsync(chatId, Strings.T(L, "lang_pick"), kb, null, ct);
        }
    }

    static async Task HandleCallback(TelegramApi bot, JObject cb, CancellationToken ct)
    {
        var data = cb.Value<string>("data") ?? "";
        var id = cb.Value<string>("id") ?? "";
        var message = cb["message"] as JObject;
        var chat = message?["chat"] as JObject;
        var chatId = chat?.Value<long?>("id") ?? 0;
        if (chatId == 0) return;

        var L = await GetLang(chatId, ct);

        if (data.StartsWith("unsub_"))
        {
            long subId;
            long.TryParse(data.Substring(6), out subId);
            if (subId > 0)
            {
                using var db = SqlContext.Create();
                var sub = await db.subs.FirstOrDefaultAsync(s => s.Id == subId, ct);
                if (sub != null && sub.chat_id == chatId)
                {
                    var rmTitle = sub.title;
                    var rmVoice = sub.voice;
                    db.subs.Remove(sub);
                    await db.SaveChangesAsync(ct);
                    await bot.AnswerCallbackQueryAsync(id, Strings.T(L, "cb_removed"), ct);

                    var vTxt = string.IsNullOrEmpty(rmVoice) ? Strings.T(L, "voice_any") : rmVoice;
                    var text = Strings.T(L, "sub_removed", Notifier.Esc(rmTitle), Notifier.Esc(vTxt));
                    await bot.SendMessageAsync(chatId, text, null, Notifier.PARSE_MODE, ct);

                    await ShowList(bot, chatId, L, ct);
                    return;
                }
            }
            await bot.AnswerCallbackQueryAsync(id, Strings.T(L, "cb_notfound"), ct);
        }
        else if (data.StartsWith("lang_"))
        {
            var newLang = Strings.Normalize(data.Substring(5));
            using (var db = SqlContext.Create())
            {
                var u = await db.users.FirstOrDefaultAsync(x => x.chat_id == chatId, ct);
                if (u != null) { u.lang = newLang; db.users.Update(u); await db.SaveChangesAsync(ct); }
            }
            await bot.AnswerCallbackQueryAsync(id, null, ct);
            await PublishCommands(bot, newLang, chatId, ct);
            await bot.SendMessageAsync(chatId, Strings.T(newLang, "lang_set"), MainKb(newLang), Notifier.PARSE_MODE, ct);
        }
        else if (data.StartsWith("auto_"))
        {
            long subId;
            long.TryParse(data.Substring(5), out subId);
            if (subId > 0)
            {
                using var db = SqlContext.Create();
                var sub = await db.subs.FirstOrDefaultAsync(s => s.Id == subId, ct);
                if (sub != null && sub.chat_id == chatId)
                {
                    sub.target_season = 0;
                    sub.next_check_at = DateTime.UtcNow.AddMinutes(1);
                    db.subs.Update(sub);
                    await db.SaveChangesAsync(ct);
                    await bot.AnswerCallbackQueryAsync(id, Strings.T(L, "cb_removed"), ct);
                    var msg = Strings.T(L, "switched_to_auto", Notifier.Esc(sub.title));
                    await bot.SendMessageAsync(chatId, msg, null, Notifier.PARSE_MODE, ct);
                    return;
                }
            }
            await bot.AnswerCallbackQueryAsync(id, Strings.T(L, "cb_notfound"), ct);
        }
        else if (data == "noop")
        {
            await bot.AnswerCallbackQueryAsync(id, null, ct);
        }
    }

    static async Task ShowList(TelegramApi bot, long chatId, string L, CancellationToken ct)
    {
        using var db = SqlContext.Create();
        var list = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == chatId)
            .OrderByDescending(s => s.subscribed_at)
            .ToListAsync(ct);

        if (list.Count == 0)
        {
            await bot.SendMessageAsync(chatId, Strings.T(L, "list_empty"), null, Notifier.PARSE_MODE, ct);
            return;
        }

        var sb = new StringBuilder(Strings.T(L, "list_header"));
        var rows = new List<(string text, string data)[]>();
        var voiceAny = Strings.T(L, "list_voice_any");

        foreach (var s in list)
        {
            sb.Append(FormatSubscriptionBlock(s, L)).Append('\n');
            var voice = string.IsNullOrEmpty(s.voice) ? voiceAny : s.voice;
            rows.Add(new (string, string)[] { ($"❌ {s.title} · {voice}", $"unsub_{s.Id}") });
        }

        var kb = TgMarkup.InlineKeyboardFromList(rows);
        await bot.SendMessageAsync(chatId, sb.ToString(), kb, Notifier.PARSE_MODE, ct);
    }

    public static string FormatSubscriptionBlock(SubscriptionRow s, string L)
    {
        var voiceAny = Strings.T(L, "list_voice_any");
        var voice = string.IsNullOrEmpty(s.voice) ? voiceAny : s.voice;
        var seasonNum = s.target_season > 0 ? s.target_season : s.last_season;

        var sb = new StringBuilder();
        sb.Append("<blockquote>");
        sb.Append("🎬 <b>").Append(Notifier.Esc(s.title)).Append("</b>");
        sb.Append(" · 🎙 ").Append(Notifier.Esc(voice));
        sb.Append('\n');

        sb.Append("📺 <b>S").Append(seasonNum.ToString("D2")).Append("</b>");

        if (s.season_total > 0)
        {
            sb.Append(" · ").Append(s.season_aired).Append('/').Append(s.season_total);
            sb.Append("  <code>").Append(Notifier.ProgressBar(s.season_aired, s.season_total)).Append("</code>");
        }
        else
        {
            sb.Append(" · E").Append(s.last_episode.ToString("D2"));
        }
        sb.Append('\n');

        sb.Append(StatusEmoji(s)).Append(' ');
        bool ended = string.Equals(s.show_status, "Ended", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(s.show_status, "Canceled", StringComparison.OrdinalIgnoreCase);
        bool allOut = s.season_total > 0 && s.season_aired >= s.season_total;

        if (allOut && ended)
            sb.Append("<i>").Append(Notifier.Esc(Strings.T(L, "all_aired"))).Append("</i>");
        else if (allOut)
        {
            sb.Append("<i>").Append(Notifier.Esc(Strings.T(L, "season_complete_waiting"))).Append("</i>");
            if (s.next_air_date.HasValue && s.next_air_date.Value.Date >= DateTime.UtcNow.Date)
                sb.Append(" · ").Append(Strings.T(L, "next_air")).Append(' ')
                  .Append(s.next_air_date.Value.ToString("yyyy-MM-dd"));
        }
        else if (s.season_total > 0)
        {
            sb.Append("<i>").Append(Notifier.Esc(Strings.ShowStatusLabel(L, s.show_status))).Append("</i>");
            if (s.next_air_date.HasValue && s.next_air_date.Value.Date >= DateTime.UtcNow.Date)
                sb.Append(" · ").Append(Strings.T(L, "next_air")).Append(' ')
                  .Append(s.next_air_date.Value.ToString("yyyy-MM-dd"));
        }
        else if (s.target_season > 0)
        {
            sb.Append("<i>").Append(Notifier.Esc(Strings.T(L, "awaiting_release"))).Append("</i>");
            if (s.next_air_date.HasValue)
                sb.Append(' ').Append(s.next_air_date.Value.ToString("yyyy-MM-dd"));
        }

        sb.Append("</blockquote>");
        return sb.ToString();
    }

    static string StatusEmoji(SubscriptionRow s)
    {
        bool ended = string.Equals(s.show_status, "Ended", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(s.show_status, "Canceled", StringComparison.OrdinalIgnoreCase);
        bool allOut = s.season_total > 0 && s.season_aired >= s.season_total;

        if (s.season_total == 0 && s.target_season > 0) return "🟡";
        if (allOut && ended) return "✅";
        if (ended) return "🔴";
        if (allOut) return "🔵";
        return "🟢";
    }

    static async Task LinkUser(string uid, long chatId, string username, string lang, CancellationToken ct)
    {
        using var db = SqlContext.Create();
        var exists = await db.users.FirstOrDefaultAsync(u => u.chat_id == chatId, ct);
        if (exists != null)
        {
            exists.lampac_uid = uid;
            exists.username = username ?? "";
            exists.lang = lang;
            exists.linked_at = DateTime.UtcNow;
            db.users.Update(exists);
        }
        else
        {
            var byUid = await db.users.FirstOrDefaultAsync(u => u.lampac_uid == uid, ct);
            if (byUid != null) db.users.Remove(byUid);

            db.users.Add(new TgUserRow
            {
                chat_id = chatId,
                lampac_uid = uid,
                username = username ?? "",
                lang = lang,
                linked_at = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(ct);
    }

    static async Task Unlink(long chatId, CancellationToken ct)
    {
        using var db = SqlContext.Create();
        var user = await db.users.FirstOrDefaultAsync(u => u.chat_id == chatId, ct);
        if (user != null) db.users.Remove(user);
        var subs = await db.subs.Where(s => s.chat_id == chatId).ToListAsync(ct);
        db.subs.RemoveRange(subs);
        await db.SaveChangesAsync(ct);
    }
}
