using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using EpWatch.Models;
using EpWatch.Services;
using Shared;
using Shared.Services;

namespace EpWatch;

public class ApiController : BaseController
{
    #region /epwatch.js
    [HttpGet, AllowAnonymous]
    [Route("epwatch.js")]
    [Route("epwatch/js/{token}")]
    public ActionResult PluginJs(string token)
    {
        SetHeadersNoCache();

        var plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/epwatch.js", "epwatch.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token ?? ""));

        return Content(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    string Uid() => requestInfo?.user_uid ?? "";

    async Task<string> UserLang()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid)) return Strings.DefaultLang;

        using var db = SqlContext.Create();
        var u = await db.users.AsNoTracking().FirstOrDefaultAsync(x => x.lampac_uid == uid);
        return Strings.Normalize(u?.lang);
    }

    #region /epwatch/link
    [HttpGet]
    [Route("epwatch/link")]
    public ActionResult Link()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        if (!Notifier.Ready || string.IsNullOrEmpty(Notifier.BotUsername))
            return Json(new { success = false, msg = "bot_offline" });

        return Json(new
        {
            success = true,
            link = $"https://t.me/{Notifier.BotUsername}?start=link_{uid}",
            bot = Notifier.BotUsername
        });
    }
    #endregion

    #region /epwatch/status
    [HttpGet]
    [Route("epwatch/status")]
    public async Task<ActionResult> Status(int tmdb_id)
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = true, linked = false, subscribed = false, voices = new string[0] });

        using var db = SqlContext.Create();
        var user = await db.users.AsNoTracking().FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null)
            return Json(new { success = true, linked = false, subscribed = false, voices = new string[0] });

        var voices = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == user.chat_id && s.tmdb_id == tmdb_id)
            .Select(s => s.voice ?? "")
            .ToArrayAsync();

        return Json(new { success = true, linked = true, subscribed = voices.Length > 0, voices });
    }
    #endregion

    string RequestToken()
    {
        var q = HttpContext?.Request?.Query;
        if (q == null) return Uid();
        if (q.TryGetValue("token", out var t) && !string.IsNullOrEmpty(t)) return t;
        if (q.TryGetValue("account_email", out var ae) && !string.IsNullOrEmpty(ae)) return ae;
        if (q.TryGetValue("uid", out var u) && !string.IsNullOrEmpty(u)) return u;
        return Uid();
    }

    AuthQs RequestAuth()
    {
        var q = HttpContext?.Request?.Query;
        var a = new AuthQs();
        if (q != null)
        {
            q.TryGetValue("token", out var t);             a.token = t;
            q.TryGetValue("account_email", out var ae);    a.account_email = ae;
            q.TryGetValue("uid", out var u);               a.uid = u;
            q.TryGetValue("box_mac", out var bm);          a.box_mac = bm;
        }
        var uid = Uid();
        if (string.IsNullOrEmpty(a.token) && !string.IsNullOrEmpty(uid)) a.token = uid;
        if (string.IsNullOrEmpty(a.account_email) && !string.IsNullOrEmpty(uid)) a.account_email = uid;
        return a;
    }

    void RememberLocalHost()
    {
        try
        {
            var conn = HttpContext?.Connection;
            if (conn == null) return;
            var ip = conn.LocalIpAddress?.ToString();
            var port = conn.LocalPort;
            if (port > 0)
            {
                if (string.IsNullOrEmpty(ip) || ip == "::1" || ip == "0.0.0.0" || ip == "::") ip = "127.0.0.1";
                BalancerProbe.RememberLocal(ip, port);
            }
        }
        catch { }
    }

    #region /epwatch/seasons
    [HttpGet]
    [Route("epwatch/seasons")]
    public async Task<ActionResult> Seasons(int tmdb_id)
    {
        var lang = await UserLang();
        var show = await TmdbClient.GetShowAsync(tmdb_id, lang, HttpContext.RequestAborted);
        if (show == null)
            return Json(new { success = false, msg = "tmdb_failed" });

        var now = DateTime.UtcNow.Date;
        var result = show.seasons
            .Where(s => s.season_number > 0)
            .OrderBy(s => s.season_number)
            .Select(s => new
            {
                season_number = s.season_number,
                name = s.name,
                episode_count = s.episode_count,
                air_date = s.air_date?.ToString("yyyy-MM-dd"),
                status = s.air_date.HasValue
                    ? (s.air_date.Value.Date > now ? "upcoming"
                        : (s.season_number == show.latest_aired_season && show.current_season_aired < show.current_season_total ? "airing" : "aired"))
                    : "unknown"
            })
            .ToArray();

        return Json(new
        {
            success = true,
            latest_aired_season = show.latest_aired_season,
            current_season_aired = show.current_season_aired,
            current_season_total = show.current_season_total,
            show_status = show.status,
            seasons = result
        });
    }
    #endregion

    #region /epwatch/voices
    [HttpGet]
    [Route("epwatch/voices")]
    public async Task<ActionResult> Voices(int tmdb_id, string title, int year = 0, int season = 0)
    {
        RememberLocalHost();

        var lang = await UserLang();
        var auth = RequestAuth();
        Console.WriteLine($"[EpWatch] /voices IN: tmdb={tmdb_id} title=\"{title}\" year={year} season={season} lang={lang} auth.token={auth.token ?? "-"} auth.email={auth.account_email ?? "-"} auth.uid={auth.uid ?? "-"}");

        var show = await TmdbClient.GetShowAsync(tmdb_id, lang, HttpContext.RequestAborted);
        int activeSeason = season > 0 ? season : (show?.latest_aired_season ?? 1);
        if (show == null)
            Console.WriteLine($"[EpWatch] /voices: TMDB lookup returned null for tmdb_id={tmdb_id}");
        else
            Console.WriteLine($"[EpWatch] /voices: TMDB ok name=\"{show.name}\" original=\"{show.original_name}\" lang={show.original_language} imdb={show.imdb_id} year={show.first_air_year} status={show.status} season_aired={show.current_season_aired}/{show.current_season_total}");

        var sp = new Models.ShowParams
        {
            tmdb_id = tmdb_id,
            title = !string.IsNullOrWhiteSpace(show?.name) ? show.name : (title ?? ""),
            original_title = show?.original_name ?? "",
            original_language = show?.original_language ?? "",
            imdb_id = show?.imdb_id ?? "",
            year = year > 0 ? year : (show?.first_air_year ?? 0)
        };

        var balancers = await BalancerProbe.GetAvailableAsync(sp, auth, HttpContext.RequestAborted);
        if (balancers.Count > 0)
            Console.WriteLine($"[EpWatch] /voices balancers: {string.Join(", ", balancers.Select(b => b.balanser + "@" + b.name))}");

        var probeTasks = balancers
            .Select(b => BalancerProbe.ProbeAsync(b, sp, activeSeason, null, auth, HttpContext.RequestAborted))
            .ToArray();
        var probed = await Task.WhenAll(probeTasks);

        var voices = new List<object>();
        for (int i = 0; i < balancers.Count; i++)
        {
            foreach (var v in probed[i].voices)
                voices.Add(new { name = v.name, t = v.t, balancer = balancers[i].balanser });
        }

        var dedup = voices
            .GroupBy(v => ((dynamic)v).name as string, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"[EpWatch] /voices tmdb={tmdb_id} s={activeSeason} -> balancers:{balancers.Count} voices:{dedup.Count}");

        return Json(new
        {
            success = true,
            season = activeSeason,
            balancers = balancers.Select(b => new { name = b.name, balancer = b.balanser }).ToArray(),
            voices = dedup
        });
    }
    #endregion

    public class SubscribeBody
    {
        public int tmdb_id { get; set; }
        public string title { get; set; }
        public string voice { get; set; }
        public string balancer { get; set; }
        public string poster_path { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        public int voice_episode { get; set; }
        public int target_season { get; set; }
    }

    #region /epwatch/subscribe
    [HttpPost]
    [Route("epwatch/subscribe")]
    public async Task<ActionResult> Subscribe()
    {
        RememberLocalHost();
        var uid = Uid();
        Console.WriteLine($"[EpWatch] /subscribe IN uid=\"{uid}\"");
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        string body;
        using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        Console.WriteLine($"[EpWatch] /subscribe body: {body}");

        SubscribeBody data;
        try { data = JsonConvert.DeserializeObject<SubscribeBody>(body); }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] /subscribe bad_body parse: {ex.Message}");
            return Json(new { success = false, msg = "bad_body" });
        }
        if (data == null || data.tmdb_id <= 0 || string.IsNullOrWhiteSpace(data.title))
        {
            Console.WriteLine($"[EpWatch] /subscribe bad_body: tmdb_id={data?.tmdb_id} title=\"{data?.title}\"");
            return Json(new { success = false, msg = "bad_body" });
        }

        using var db = SqlContext.Create();
        var user = await db.users.FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null) return Json(new { success = false, msg = "not_linked" });

        var v = data.voice ?? "";
        var existing = await db.subs.FirstOrDefaultAsync(s =>
            s.chat_id == user.chat_id && s.tmdb_id == data.tmdb_id && s.voice == v);

        var show = await TmdbClient.GetShowAsync(data.tmdb_id, Strings.Normalize(user.lang), HttpContext.RequestAborted);

        int initialLastSeason = data.target_season > 0 ? data.target_season : data.season;

        SubscriptionRow newRow = null;
        if (existing == null)
        {
            newRow = new SubscriptionRow
            {
                chat_id = user.chat_id,
                tmdb_id = data.tmdb_id,
                title = data.title,
                voice = v,
                balancer = data.balancer ?? "",
                last_season = initialLastSeason,
                last_episode = data.episode,
                last_voice_episode = data.voice_episode,
                poster_path = data.poster_path ?? "",
                season_total = show?.current_season_total ?? 0,
                season_aired = show?.current_season_aired ?? 0,
                target_season = data.target_season,
                show_status = show?.status ?? "",
                subscribed_at = DateTime.UtcNow,
                next_check_at = DateTime.UtcNow.AddMinutes(1)
            };
            db.subs.Add(newRow);
        }
        else
        {
            existing.title = data.title;
            existing.balancer = data.balancer ?? existing.balancer;
            existing.poster_path = data.poster_path ?? existing.poster_path;
            if (data.target_season >= 0 && data.target_season != existing.target_season)
            {
                existing.target_season = data.target_season;
                existing.last_season = data.target_season > 0 ? data.target_season : (data.season > 0 ? data.season : existing.last_season);
                existing.last_episode = 0;
                existing.last_voice_episode = 0;
                existing.next_check_at = DateTime.UtcNow.AddMinutes(1);
            }
            else
            {
                if (data.season > 0) existing.last_season = data.season;
                if (data.episode > 0) existing.last_episode = data.episode;
            }
            if (show != null)
            {
                existing.season_total = show.current_season_total;
                existing.season_aired = show.current_season_aired;
                existing.show_status = show.status ?? "";
            }
            db.subs.Update(existing);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] /subscribe SaveChanges failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Json(new { success = false, msg = "db_error", detail = ex.Message });
        }

        Console.WriteLine($"[EpWatch] /subscribe OK chat_id={user.chat_id} tmdb_id={data.tmdb_id} voice=\"{v}\" balancer=\"{data.balancer}\"");

        if (Notifier.Ready)
        {
            var L = Strings.Normalize(user.lang);
            var vTxt = string.IsNullOrEmpty(v) ? Strings.T(L, "voice_any") : v;
            var text = Strings.T(L, "sub_added", Notifier.Esc(data.title), Notifier.Esc(vTxt));
            _ = Notifier.SendTextAsync(user.chat_id, text, HttpContext.RequestAborted);

            if (data.target_season > 0 && show != null && string.IsNullOrEmpty(v))
            {
                var sInfo = show.seasons.FirstOrDefault(x => x.season_number == data.target_season);
                if (sInfo != null && sInfo.episode_count > 0)
                {
                    var seasonEps = await TmdbClient.GetSeasonAsync(data.tmdb_id, data.target_season, L, HttpContext.RequestAborted);
                    int aired = seasonEps.Count(e => e.air_date.HasValue && e.air_date.Value.Date <= DateTime.UtcNow.Date);
                    if (aired >= sInfo.episode_count)
                    {
                        long subId = newRow?.Id ?? existing?.Id ?? 0;
                        if (subId > 0)
                        {
                            var askText = Strings.T(L, "already_aired_body", Notifier.Esc(data.title), data.target_season, aired, sInfo.episode_count);
                            var kb = TgMarkup.InlineKeyboard(
                                new (string, string)[] { (Strings.T(L, "btn_switch_auto"), "auto_" + subId) },
                                new (string, string)[] { (Strings.T(L, "btn_keep"), "noop") }
                            );
                            _ = Notifier.Bot.SendMessageAsync(user.chat_id, askText, kb, Notifier.PARSE_MODE, HttpContext.RequestAborted);
                        }
                    }
                }
            }
        }

        return Json(new { success = true });
    }
    #endregion

    public class UnsubscribeBody
    {
        public int tmdb_id { get; set; }
        public string voice { get; set; }
    }

    #region /epwatch/unsubscribe
    [HttpPost]
    [Route("epwatch/unsubscribe")]
    public async Task<ActionResult> Unsubscribe()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        string body;
        using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        UnsubscribeBody data;
        try { data = JsonConvert.DeserializeObject<UnsubscribeBody>(body); }
        catch { return Json(new { success = false, msg = "bad_body" }); }
        if (data == null || data.tmdb_id <= 0) return Json(new { success = false, msg = "bad_body" });

        using var db = SqlContext.Create();
        var user = await db.users.FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null) return Json(new { success = false, msg = "not_linked" });

        IQueryable<SubscriptionRow> q = db.subs.Where(s => s.chat_id == user.chat_id && s.tmdb_id == data.tmdb_id);
        if (data.voice != null) q = q.Where(s => s.voice == data.voice);

        var rows = await q.ToListAsync();
        if (rows.Count == 0)
            return Json(new { success = true, removed = 0 });

        var snapshot = rows.Select(r => new { r.title, r.voice }).ToList();
        db.subs.RemoveRange(rows);
        await db.SaveChangesAsync();

        if (Notifier.Ready)
        {
            var L = Strings.Normalize(user.lang);
            foreach (var s in snapshot)
            {
                var vTxt = string.IsNullOrEmpty(s.voice) ? Strings.T(L, "voice_any") : s.voice;
                var text = Strings.T(L, "sub_removed", Notifier.Esc(s.title), Notifier.Esc(vTxt));
                _ = Notifier.SendTextAsync(user.chat_id, text, HttpContext.RequestAborted);
            }
        }

        return Json(new { success = true, removed = rows.Count });
    }
    #endregion

    #region /epwatch/subscriptions
    [HttpGet]
    [Route("epwatch/subscriptions")]
    public async Task<ActionResult> Subscriptions()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = true, linked = false, results = new object[0] });

        using var db = SqlContext.Create();
        var user = await db.users.AsNoTracking().FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null)
            return Json(new { success = true, linked = false, results = new object[0] });

        var list = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == user.chat_id)
            .OrderByDescending(s => s.subscribed_at)
            .Select(s => new
            {
                id = s.Id,
                tmdb_id = s.tmdb_id,
                title = s.title,
                voice = s.voice,
                balancer = s.balancer,
                poster_path = s.poster_path,
                last_season = s.last_season,
                last_episode = s.last_episode,
                last_voice_episode = s.last_voice_episode,
                season_total = s.season_total,
                season_aired = s.season_aired,
                target_season = s.target_season,
                show_status = s.show_status,
                last_checked_at = s.last_checked_at,
                subscribed_at = s.subscribed_at
            })
            .ToArrayAsync();

        return Json(new { success = true, linked = true, results = list });
    }
    #endregion

    #region /epwatch/unlink
    [HttpPost, HttpGet]
    [Route("epwatch/unlink")]
    public async Task<ActionResult> UnlinkPlugin()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        using var db = SqlContext.Create();
        var user = await db.users.FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null) return Json(new { success = false, msg = "not_linked" });

        var chatId = user.chat_id;
        db.users.Remove(user);
        var subs = await db.subs.Where(s => s.chat_id == chatId).ToListAsync();
        db.subs.RemoveRange(subs);
        await db.SaveChangesAsync();

        if (Notifier.Ready)
        {
            var L = Strings.Normalize(user.lang);
            _ = Notifier.SendTextAsync(chatId, Strings.T(L, "unlinked"), HttpContext.RequestAborted);
        }

        return Json(new { success = true });
    }
    #endregion

    #region /epwatch/check
    [HttpGet]
    [Route("epwatch/check")]
    public async Task<ActionResult> CheckNow()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        using var db = SqlContext.Create();
        var user = await db.users.AsNoTracking().FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null) return Json(new { success = false, msg = "not_linked" });

        var notified = await EpisodeChecker.CheckOnceAsync(user.chat_id, HttpContext.RequestAborted);
        return Json(new { success = true, notified });
    }
    #endregion
}
