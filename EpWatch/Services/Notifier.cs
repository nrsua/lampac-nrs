using System;
using System.Threading;
using System.Threading.Tasks;
using EpWatch.Models;

namespace EpWatch.Services;

public static class Notifier
{
    public static volatile TelegramApi Bot;
    public static volatile string BotUsername;

    public static bool Ready => Bot != null;

    public const string PARSE_MODE = "HTML";

    public static async Task<bool> SendEpisodeAsync(SubscriptionRow sub, TmdbEpisode ep, CancellationToken ct)
    {
        if (Bot == null) return false;

        var sb = new System.Text.StringBuilder();
        sb.Append("🎬 <b>").Append(Esc(sub.title)).Append("</b>\n");
        sb.Append("🆕 <b>").Append(FormatSE(ep.season, ep.episode)).Append("</b>");
        if (!string.IsNullOrEmpty(ep.name))
            sb.Append(" - <i>").Append(Esc(ep.name)).Append("</i>");
        sb.Append('\n');

        if (!string.IsNullOrEmpty(sub.voice))
            sb.Append("🎙 ").Append(Esc(sub.voice)).Append('\n');
        if (ep.air_date.HasValue)
            sb.Append("📅 ").Append(ep.air_date.Value.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

        if (!string.IsNullOrEmpty(ep.overview))
        {
            var d = ep.overview.Length > 600 ? ep.overview.Substring(0, 600) + "…" : ep.overview;
            sb.Append("\n<blockquote expandable><i>").Append(Esc(d)).Append("</i></blockquote>");
        }

        try
        {
            var msg = sb.ToString();
            if (!string.IsNullOrEmpty(ep.still_url))
                await Bot.SendPhotoAsync(sub.chat_id, ep.still_url, msg, PARSE_MODE, ct);
            else
                await Bot.SendMessageAsync(sub.chat_id, msg, null, PARSE_MODE, ct);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] notify failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> SendTextAsync(long chatId, string text, CancellationToken ct)
    {
        if (Bot == null) return false;
        try
        {
            await Bot.SendMessageAsync(chatId, text, null, PARSE_MODE, ct);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] send text failed: {ex.Message}");
            return false;
        }
    }

    public static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    public static string FormatSE(int s, int e) => $"S{s:D2}E{e:D2}";

    public static string ProgressBar(int current, int total, int width = 10)
    {
        if (total <= 0) return new string('░', width);
        var filled = (int)Math.Round((double)width * current / total);
        if (filled < 0) filled = 0;
        if (filled > width) filled = width;
        return new string('▓', filled) + new string('░', width - filled);
    }

    [Obsolete("Use Esc for HTML parse mode")]
    public static string EscapeMd(string s) => Esc(s);
}
