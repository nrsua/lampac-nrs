using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EpWatch.Models;

namespace EpWatch.Services;

public static class NoticeFeed
{
    const int KeepPerChat = 60;

    public static async Task AddEpisodesAsync(SubscriptionRow sub, List<TmdbEpisode> eps, CancellationToken ct)
    {
        if (sub == null || eps == null || eps.Count == 0) return;

        try
        {
            using var db = SqlContext.Create();
            var now = DateTime.UtcNow;

            foreach (var ep in eps.OrderBy(e => e.episode))
            {
                db.notices.Add(new NoticeRow
                {
                    chat_id = sub.chat_id,
                    kind = "episode",
                    tmdb_id = sub.tmdb_id,
                    media_type = "tv",
                    title = sub.title ?? "",
                    poster_path = sub.poster_path ?? "",
                    season = ep.season,
                    episode = ep.episode,
                    ep_name = ep.name ?? "",
                    still_url = ep.still_url ?? "",
                    voice = sub.voice ?? "",
                    balancer = sub.balancer ?? "",
                    extra = "",
                    created_at = now
                });
            }

            await db.SaveChangesAsync(ct);
            await TrimAsync(db, sub.chat_id, ct);
        }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] notice feed (episodes) failed: {ex.Message}");
        }
    }

    public static async Task AddMovieAsync(SubscriptionRow sub, List<(string balancer, string voice)> items, CancellationToken ct)
    {
        if (sub == null || items == null || items.Count == 0) return;

        try
        {
            using var db = SqlContext.Create();

            var extra = string.Join("\n", items.Select(i =>
                BalancerProbe.DisplayName(i.balancer ?? "") + "\t" + (i.voice ?? "")));

            db.notices.Add(new NoticeRow
            {
                chat_id = sub.chat_id,
                kind = "movie",
                tmdb_id = sub.tmdb_id,
                media_type = "movie",
                title = sub.title ?? "",
                poster_path = sub.poster_path ?? "",
                ep_name = "",
                still_url = "",
                voice = items.Select(i => i.voice).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "",
                balancer = items[0].balancer ?? "",
                extra = extra,
                created_at = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            await TrimAsync(db, sub.chat_id, ct);
        }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] notice feed (movie) failed: {ex.Message}");
        }
    }

    static async Task TrimAsync(SqlContext db, long chatId, CancellationToken ct)
    {
        try
        {
            var stale = await db.notices
                .Where(n => n.chat_id == chatId)
                .OrderByDescending(n => n.Id)
                .Skip(KeepPerChat)
                .ToListAsync(ct);

            if (stale.Count == 0) return;

            db.notices.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Dbg($"[EpWatch] notice feed trim failed: {ex.Message}");
        }
    }
}