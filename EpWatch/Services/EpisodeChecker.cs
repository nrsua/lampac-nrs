using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using EpWatch.Models;

namespace EpWatch.Services;

public sealed class EpisodeChecker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!ModInit.conf.enable)
            return;

        var initial = TimeSpan.FromMinutes(Math.Max(1, ModInit.conf.initial_delay_minutes));
        try { await Task.Delay(initial, ct); } catch { return; }

        var interval = TimeSpan.FromMinutes(Math.Max(5, ModInit.conf.check_interval_minutes));

        while (!ct.IsCancellationRequested)
        {
            try { await CheckOnceAsync(null, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[EpWatch] check loop error: {ex.Message}"); }

            try { await Task.Delay(interval, ct); } catch { break; }
        }
    }

    public static async Task<int> CheckOnceAsync(long? onlyChatId, CancellationToken ct)
    {
        if (!Notifier.Ready)
            return 0;

        bool aggregate = onlyChatId.HasValue;
        int notified = 0;
        var now = DateTime.UtcNow;
        var throttle = TimeSpan.FromMilliseconds(Math.Max(0, ModInit.conf.balancer_throttle_ms));

        List<SubscriptionRow> subs;
        Dictionary<long, string> chatLang;
        Dictionary<long, string> chatToken;

        using (var db = SqlContext.Create())
        {
            IQueryable<SubscriptionRow> q = db.subs.AsNoTracking();
            if (onlyChatId.HasValue) q = q.Where(s => s.chat_id == onlyChatId.Value);
            else q = q.Where(s => s.next_check_at == null || s.next_check_at <= now);
            subs = await q.ToListAsync(ct);

            var chatIds = subs.Select(s => s.chat_id).Distinct().ToList();
            var rows = await db.users.AsNoTracking()
                .Where(u => chatIds.Contains(u.chat_id))
                .ToListAsync(ct);
            chatLang = rows.ToDictionary(u => u.chat_id, u => Strings.Normalize(u.lang));
            chatToken = rows.ToDictionary(u => u.chat_id, u => u.lampac_uid ?? "");
        }

        if (subs.Count == 0)
        {
            if (aggregate)
                await Notifier.SendTextAsync(onlyChatId.Value, Strings.T(ChatLangOrDefault(chatLang, onlyChatId.Value), "check_none"), ct);
            return 0;
        }

        var grouped = subs
            .Select(s => new { sub = s, lang = chatLang.TryGetValue(s.chat_id, out var l) ? l : Strings.DefaultLang })
            .GroupBy(x => new { x.sub.tmdb_id, x.lang })
            .ToList();

        var summary = new Dictionary<long, List<string>>();

        foreach (var grp in grouped)
        {
            if (ct.IsCancellationRequested) break;

            int tmdbId = grp.Key.tmdb_id;
            string lang = grp.Key.lang;

            var show = await TmdbClient.GetShowAsync(tmdbId, lang, ct);
            if (show == null) { ResetNextCheck(grp.Select(x => x.sub), TimeSpan.FromHours(2)); continue; }

            int latestSeason = show.latest_aired_season;
            if (latestSeason <= 0) latestSeason = show.number_of_seasons;
            if (latestSeason <= 0) { ResetNextCheck(grp.Select(x => x.sub), TimeSpan.FromHours(6)); continue; }

            var seasonCache = new Dictionary<int, List<TmdbEpisode>>();

            List<BalancerEntry> balancers = null;
            string title = grp.First().sub.title;
            string groupToken = chatToken.TryGetValue(grp.First().sub.chat_id, out var tk) ? tk : "";
            var groupAuth = new AuthQs { token = groupToken, account_email = groupToken, uid = groupToken };

            var sp = new ShowParams
            {
                tmdb_id = tmdbId,
                title = !string.IsNullOrWhiteSpace(show.name) ? show.name : title,
                original_title = show.original_name,
                original_language = show.original_language,
                imdb_id = show.imdb_id,
                year = show.first_air_year > 0 ? show.first_air_year : 0
            };

            if (grp.Any(x => !string.IsNullOrEmpty(x.sub.voice)))
            {
                balancers = await BalancerProbe.GetAvailableAsync(sp, groupAuth, ct);
                if (throttle > TimeSpan.Zero) await Task.Delay(throttle, ct);
            }

            foreach (var item in grp)
            {
                if (ct.IsCancellationRequested) break;
                var sub = item.sub;

                try
                {
                    bool changed = false;

                    int effectiveSeason = sub.target_season > 0 ? sub.target_season : latestSeason;

                    if (!seasonCache.TryGetValue(effectiveSeason, out var seasonEps))
                    {
                        seasonEps = await TmdbClient.GetSeasonAsync(tmdbId, effectiveSeason, lang, ct);
                        seasonCache[effectiveSeason] = seasonEps;
                    }
                    var airedNow = seasonEps.Where(e => e.air_date.HasValue && e.air_date.Value.Date <= now.Date).ToList();

                    if (effectiveSeason != sub.last_season)
                    {
                        sub.last_episode = 0;
                        sub.last_voice_episode = 0;
                        sub.last_season = effectiveSeason;
                        changed = true;
                    }

                    foreach (var ep in airedNow.OrderBy(x => x.episode))
                    {
                        if (ep.episode <= sub.last_episode) continue;

                        if (string.IsNullOrEmpty(sub.voice))
                        {
                            ep.season = effectiveSeason;
                            if (aggregate)
                            {
                                AddSummary(summary, sub, ep);
                                notified++;
                            }
                            else if (await Notifier.SendEpisodeAsync(sub, ep, ct))
                            {
                                notified++;
                            }
                        }

                        sub.last_episode = ep.episode;
                        changed = true;
                    }

                    if (!string.IsNullOrEmpty(sub.voice) && balancers != null && balancers.Count > 0)
                    {
                        int newMax = sub.last_voice_episode;

                        foreach (var b in balancers)
                        {
                            if (!string.IsNullOrEmpty(sub.balancer)
                                && !string.Equals(sub.balancer, b.balanser, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var subToken = chatToken.TryGetValue(sub.chat_id, out var st) ? st : "";
                            var subAuth = new AuthQs { token = subToken, account_email = subToken, uid = subToken };
                            var probed = await BalancerProbe.ProbeAsync(b, sp, effectiveSeason, sub.voice, subAuth, ct);
                            if (probed.maxEpisode > newMax) newMax = probed.maxEpisode;
                            if (throttle > TimeSpan.Zero) await Task.Delay(throttle, ct);
                        }

                        if (newMax > sub.last_voice_episode)
                        {
                            for (int e = sub.last_voice_episode + 1; e <= newMax; e++)
                            {
                                var epInfo = airedNow.FirstOrDefault(x => x.episode == e)
                                             ?? new TmdbEpisode { season = effectiveSeason, episode = e };
                                epInfo.season = effectiveSeason;

                                if (aggregate)
                                {
                                    AddSummary(summary, sub, epInfo);
                                    notified++;
                                }
                                else if (await Notifier.SendEpisodeAsync(sub, epInfo, ct))
                                {
                                    notified++;
                                }
                            }
                            sub.last_voice_episode = newMax;
                            changed = true;
                        }
                    }

                    if (effectiveSeason == latestSeason)
                    {
                        sub.season_total = show.current_season_total;
                        sub.season_aired = show.current_season_aired;
                    }
                    else
                    {
                        var sInfo = show.seasons.FirstOrDefault(x => x.season_number == effectiveSeason);
                        sub.season_total = sInfo?.episode_count ?? 0;
                        sub.season_aired = airedNow.Count;
                    }
                    sub.show_status = show.status ?? "";
                    sub.next_air_date = show.next_air_date;
                    sub.last_checked_at = now;
                    sub.next_check_at = ComputeNextCheck(now, show);
                    changed = true;

                    if (changed)
                    {
                        using var db = SqlContext.Create();
                        db.subs.Update(sub);
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EpWatch] sub {sub.tmdb_id}/{sub.chat_id} error: {ex.Message}");
                }
            }
        }

        if (aggregate)
        {
            var chatId = onlyChatId.Value;
            var lang = ChatLangOrDefault(chatLang, chatId);

            List<SubscriptionRow> allUserSubs;
            using (var db = SqlContext.Create())
            {
                allUserSubs = await db.subs.AsNoTracking()
                    .Where(s => s.chat_id == chatId)
                    .OrderByDescending(s => s.subscribed_at)
                    .ToListAsync(ct);
            }

            var sb = new StringBuilder();
            if (summary.TryGetValue(chatId, out var lines) && lines.Count > 0)
            {
                sb.Append(Strings.T(lang, "new_header", lines.Count));
                sb.Append(string.Join("\n", lines));
                sb.Append("\n\n");
            }

            if (allUserSubs.Count > 0)
            {
                sb.Append(Strings.T(lang, "status_header"));
                foreach (var s in allUserSubs)
                    sb.Append(TelegramBotService.FormatSubscriptionBlock(s, lang)).Append('\n');
            }
            else
            {
                sb.Append(Strings.T(lang, "list_empty"));
            }

            await Notifier.SendTextAsync(chatId, sb.ToString(), ct);
        }

        return notified;
    }

    static string ChatLangOrDefault(Dictionary<long, string> chatLang, long chatId)
        => chatLang.TryGetValue(chatId, out var l) ? l : Strings.DefaultLang;

    static void AddSummary(Dictionary<long, List<string>> summary, SubscriptionRow sub, TmdbEpisode ep)
    {
        if (!summary.TryGetValue(sub.chat_id, out var lines))
        {
            lines = new List<string>();
            summary[sub.chat_id] = lines;
        }

        var line = $"🆕 <b>{Notifier.Esc(sub.title)}</b> · {Notifier.FormatSE(ep.season, ep.episode)}";
        if (!string.IsNullOrEmpty(ep.name)) line += $" - <i>{Notifier.Esc(ep.name)}</i>";
        if (!string.IsNullOrEmpty(sub.voice)) line += $"\n   🎙 {Notifier.Esc(sub.voice)}";
        lines.Add(line);
    }

    static void ResetNextCheck(IEnumerable<SubscriptionRow> rows, TimeSpan defer)
    {
        try
        {
            using var db = SqlContext.Create();
            foreach (var r in rows)
            {
                r.next_check_at = DateTime.UtcNow + defer;
                db.subs.Update(r);
            }
            db.SaveChanges();
        }
        catch { }
    }

    static DateTime ComputeNextCheck(DateTime now, TmdbShow show)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, ModInit.conf.check_interval_minutes));

        bool ended = string.Equals(show.status, "Ended", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(show.status, "Canceled", StringComparison.OrdinalIgnoreCase);

        bool seasonComplete = show.current_season_total > 0
                              && show.current_season_aired >= show.current_season_total;

        if (ended && seasonComplete && !show.next_air_date.HasValue)
            return now.AddDays(30);

        if (show.next_air_date.HasValue)
        {
            var na = show.next_air_date.Value;
            if (na.Date > now.Date.AddDays(1))
                return na.AddHours(-2);
        }

        if (seasonComplete && !show.next_air_date.HasValue)
            return now.AddDays(3);

        return now + interval;
    }

}
