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

        var rows = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == user.chat_id && s.tmdb_id == tmdb_id)
            .Select(s => new { s.Id, s.voice, s.balancer })
            .ToListAsync();

        var voices = rows.Select(r => r.voice ?? "").ToArray();
        var items = rows.Select(r => new
        {
            id = r.Id,
            voice = (r.voice ?? "").Trim(),
            balancer = r.balancer ?? "",
            balancer_name = BalancerProbe.DisplayName(r.balancer ?? "") ?? ""
        }).ToArray();

        return Json(new { success = true, linked = true, subscribed = voices.Length > 0, voices, items });
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
    public async Task<ActionResult> Voices(int tmdb_id, string title, int year = 0, int season = 0, string balancer = null, string link = null)
    {
        RememberLocalHost();

        var lang = await UserLang();
        var auth = RequestAuth();
        Log.Dbg($"[EpWatch] /voices IN: tmdb={tmdb_id} title=\"{title}\" year={year} season={season} lang={lang} auth={(auth.IsEmpty ? "no" : "yes")}");

        var show = await TmdbClient.GetShowAsync(tmdb_id, lang, HttpContext.RequestAborted);
        int activeSeason = season > 0 ? season : (show?.latest_aired_season ?? 1);
        int probeSeason = season > 0 ? season : 1;
        if (show == null)
            Log.Dbg($"[EpWatch] /voices: TMDB lookup returned null for tmdb_id={tmdb_id}");
        else
            Log.Dbg($"[EpWatch] /voices: TMDB ok name=\"{show.name}\" original=\"{show.original_name}\" lang={show.original_language} imdb={show.imdb_id} year={show.first_air_year} status={show.status} season_aired={show.current_season_aired}/{show.current_season_total}");

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
            Log.Dbg($"[EpWatch] /voices balancers: {string.Join(", ", balancers.Select(b => b.balanser + "@" + b.name))}");

        if (!string.IsNullOrWhiteSpace(balancer))
            return await VoicesForBalancer(balancers, balancer.Trim(), link, sp, probeSeason, activeSeason, auth);

        var probeTasks = balancers
            .Select(b => BalancerProbe.ProbeAsync(b, sp, probeSeason, null, auth, HttpContext.RequestAborted))
            .ToArray();
        var probed = await Task.WhenAll(probeTasks);

        var voices = new List<object>();
        for (int i = 0; i < balancers.Count; i++)
        {
            foreach (var v in probed[i].voices)
                voices.Add(new { name = v.name, t = v.t, balancer = balancers[i].balanser });
        }

        var dedup = voices
            .GroupBy(v => (((dynamic)v).name as string) + "\t" + (((dynamic)v).balancer as string), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        Log.Dbg($"[EpWatch] /voices tmdb={tmdb_id} probe_s={probeSeason} report_s={activeSeason} -> balancers:{balancers.Count} voices:{dedup.Count}");

        return Json(new
        {
            success = true,
            season = activeSeason,
            balancers = balancers.Select(b => new { name = b.name, balancer = b.balanser }).ToArray(),
            voices = dedup
        });
    }

    async Task<ActionResult> VoicesForBalancer(List<BalancerEntry> balancers, string balancer, string link,
        Models.ShowParams sp, int probeSeason, int activeSeason, AuthQs auth)
    {
        var ct = HttpContext.RequestAborted;
        var entry = balancers.FirstOrDefault(b => string.Equals(b.balanser, balancer, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return Json(new
            {
                success = true, season = activeSeason, balancer, balancer_name = BalancerProbe.DisplayName(balancer),
                link = "", seasons = new int[0], candidates = new object[0], voices = new object[0], msg = "balancer_unavailable"
            });
        }

        List<int> balSeasons;
        if (string.IsNullOrWhiteSpace(link))
        {
            var search = await BalancerProbe.SearchAsync(entry, sp, auth, ct);
            if (search.candidates.Count > 0)
            {
                return Json(new
                {
                    success = true, season = activeSeason, balancer = entry.balanser, balancer_name = entry.name,
                    link = "", seasons = new int[0], candidates = search.candidates, voices = new object[0]
                });
            }
            balSeasons = search.seasons;
        }
        else
        {
            var clean = BalancerProbe.SanitizeLink(link, entry.url);
            if (clean.Length == 0)
                return Json(new { success = false, msg = "bad_link" });

            entry = new BalancerEntry { name = entry.name, balanser = entry.balanser, url = entry.url, link = clean };
            balSeasons = await BalancerProbe.DiscoverSeasonsAsync(entry, sp, auth, ct);
        }

        var probed = await BalancerProbe.ProbeAsync(entry, sp, probeSeason, null, auth, ct);
        var voices = probed.voices
            .Select(v => new { name = v.name, t = v.t, balancer = entry.balanser })
            .ToList();

        Log.Dbg($"[EpWatch] /voices tmdb={sp.tmdb_id} balancer={entry.balanser} pinned={(entry.link.Length > 0 ? "yes" : "no")} -> seasons:[{string.Join(",", balSeasons)}] voices:{voices.Count}");

        return Json(new
        {
            success = true, season = activeSeason, balancer = entry.balanser, balancer_name = entry.name,
            link = entry.link ?? "", seasons = balSeasons, candidates = new object[0], voices
        });
    }
    #endregion

    #region /epwatch/balancers
    [HttpGet]
    [Route("epwatch/balancers")]
    public async Task<ActionResult> Balancers(int tmdb_id, string title, int year = 0, int serial = 0)
    {
        RememberLocalHost();

        var lang = await UserLang();
        var auth = RequestAuth();

        if (serial == 1)
        {
            var show = await TmdbClient.GetShowAsync(tmdb_id, lang, HttpContext.RequestAborted);
            var ssp = new Models.ShowParams
            {
                tmdb_id = tmdb_id,
                title = !string.IsNullOrWhiteSpace(show?.name) ? show.name : (title ?? ""),
                original_title = show?.original_name ?? "",
                original_language = show?.original_language ?? "",
                imdb_id = show?.imdb_id ?? "",
                year = year > 0 ? year : (show?.first_air_year ?? 0)
            };

            var tvBalancers = await BalancerProbe.GetAvailableAsync(ssp, auth, HttpContext.RequestAborted);
            Log.Dbg($"[EpWatch] /balancers serial tmdb={tmdb_id} -> {tvBalancers.Count}");

            return Json(new
            {
                success = true,
                season = show?.latest_aired_season ?? 1,
                balancers = tvBalancers.Select(b => new { name = b.name, balancer = b.balanser }).ToArray()
            });
        }

        var movie = await TmdbClient.GetMovieAsync(tmdb_id, lang, HttpContext.RequestAborted);

        var sp = new Models.ShowParams
        {
            tmdb_id = tmdb_id,
            title = !string.IsNullOrWhiteSpace(movie?.name) ? movie.name : (title ?? ""),
            original_title = movie?.original_name ?? "",
            original_language = movie?.original_language ?? "",
            imdb_id = movie?.imdb_id ?? "",
            year = year > 0 ? year : (movie?.first_air_year ?? 0)
        };

        var balancers = await BalancerProbe.GetAvailableAsync(sp, auth, HttpContext.RequestAborted, movie: true);
        Log.Dbg($"[EpWatch] /balancers tmdb={tmdb_id} -> {balancers.Count}");

        return Json(new
        {
            success = true,
            balancers = balancers.Select(b => new { name = b.name, balancer = b.balanser }).ToArray()
        });
    }
    #endregion

    #region /epwatch/movie_probe
    [HttpGet]
    [Route("epwatch/movie_probe")]
    public async Task<ActionResult> MovieProbe(int tmdb_id, string title, string balancer, int year = 0, string link = null)
    {
        RememberLocalHost();
        if (tmdb_id <= 0 || string.IsNullOrWhiteSpace(balancer))
            return Json(new { success = false, msg = "bad_args" });

        var ct = HttpContext.RequestAborted;
        var lang = await UserLang();
        var auth = RequestAuth();
        var movie = await TmdbClient.GetMovieAsync(tmdb_id, lang, ct);

        var sp = new Models.ShowParams
        {
            tmdb_id = tmdb_id,
            title = !string.IsNullOrWhiteSpace(movie?.name) ? movie.name : (title ?? ""),
            original_title = movie?.original_name ?? "",
            original_language = movie?.original_language ?? "",
            imdb_id = movie?.imdb_id ?? "",
            year = year > 0 ? year : (movie?.first_air_year ?? 0)
        };

        var balancers = await BalancerProbe.GetAvailableAsync(sp, auth, ct, movie: true);
        var entry = balancers.FirstOrDefault(b => string.Equals(b.balanser, balancer.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return Json(new { success = true, balancer, link = "", candidates = new object[0], available = false, voices = new string[0], msg = "balancer_unavailable" });

        if (string.IsNullOrWhiteSpace(link))
        {
            var search = await BalancerProbe.SearchAsync(entry, sp, auth, ct, movie: true);
            if (search.candidates.Count > 0)
                return Json(new { success = true, balancer = entry.balanser, balancer_name = entry.name, link = "", candidates = search.candidates, available = false, voices = new string[0] });
        }
        else
        {
            var clean = BalancerProbe.SanitizeLink(link, entry.url);
            if (clean.Length == 0) return Json(new { success = false, msg = "bad_link" });
            entry = new BalancerEntry { name = entry.name, balanser = entry.balanser, url = entry.url, link = clean };
        }

        var probed = await BalancerProbe.ProbeMovieAsync(entry, sp, auth, ct);
        Log.Dbg($"[EpWatch] /movie_probe tmdb={tmdb_id} balancer={entry.balanser} pinned={(entry.link.Length > 0 ? "yes" : "no")} -> available={probed.available} voices={probed.voices.Count}");

        return Json(new
        {
            success = true, balancer = entry.balanser, balancer_name = entry.name, link = entry.link ?? "",
            candidates = new object[0], available = probed.available, voices = probed.voices
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
        public string media_type { get; set; }
        public string link { get; set; }
    }

    #region /epwatch/subscribe
    [HttpPost]
    [Route("epwatch/subscribe")]
    public async Task<ActionResult> Subscribe()
    {
        RememberLocalHost();
        var uid = Uid();
        Log.Dbg($"[EpWatch] /subscribe IN uid=\"{uid}\"");
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = false, msg = "no_uid" });

        string body;
        using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        Log.Dbg($"[EpWatch] /subscribe body: {body}");

        SubscribeBody data;
        try { data = JsonConvert.DeserializeObject<SubscribeBody>(body); }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] /subscribe bad_body parse: {ex.Message}");
            return Json(new { success = false, msg = "bad_body" });
        }
        if (data == null || data.tmdb_id <= 0 || string.IsNullOrWhiteSpace(data.title))
        {
            Log.Warn($"[EpWatch] /subscribe bad_body: tmdb_id={data?.tmdb_id} title=\"{data?.title}\"");
            return Json(new { success = false, msg = "bad_body" });
        }

        using var db = SqlContext.Create();
        var user = await db.users.FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null) return Json(new { success = false, msg = "not_linked" });

        var gate = _subscribeGates.GetOrAdd(user.chat_id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (string.Equals(data.media_type, "movie", StringComparison.OrdinalIgnoreCase))
                return await SubscribeMovie(db, user, data);

            return await SubscribeTv(db, user, data);
        }
        finally { gate.Release(); }
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<long, SemaphoreSlim> _subscribeGates = new();

    async Task<ActionResult> SubscribeTv(SqlContext db, TgUserRow user, SubscribeBody data)
    {
        var v = (data.voice ?? "").Trim();
        var bal = (data.balancer ?? "").Trim();
        var link = v.Length > 0 && bal.Length > 0 ? BalancerProbe.SanitizeLink(data.link, null) : "";

        var sameShow = await db.subs
            .Where(s => s.chat_id == user.chat_id && s.tmdb_id == data.tmdb_id)
            .ToListAsync();
        sameShow = sameShow.Where(s => !string.Equals(s.media_type, "movie", StringComparison.OrdinalIgnoreCase)).ToList();

        var existing = sameShow.FirstOrDefault(s =>
                           string.Equals((s.voice ?? "").Trim(), v, StringComparison.OrdinalIgnoreCase)
                           && string.Equals((s.balancer ?? "").Trim(), bal, StringComparison.OrdinalIgnoreCase))
                       ?? (v.Length == 0 ? sameShow.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.voice)) : null);

        var L = Strings.Normalize(user.lang);
        var show = await TmdbClient.GetShowAsync(data.tmdb_id, L, HttpContext.RequestAborted);

        int effectiveSeason = data.target_season > 0
            ? data.target_season
            : (show?.latest_aired_season ?? data.season);

        int initialLastEpisode = 0;
        int initialLastVoiceEpisode = 0;
        int seasonAired = 0;
        int seasonTotal = 0;
        string structureSource = "";
        int tvdbId = 0;

        if (show != null)
        {
            var auth = RequestAuth();
            var sp = new Models.ShowParams
            {
                tmdb_id = data.tmdb_id,
                title = !string.IsNullOrWhiteSpace(show.name) ? show.name : data.title,
                original_title = show.original_name ?? "",
                original_language = show.original_language ?? "",
                imdb_id = show.imdb_id ?? "",
                year = show.first_air_year > 0 ? show.first_air_year : 0
            };

            TvdbShow tvdb = null;
            List<BalancerEntry> balancers = null;

            if (!string.IsNullOrEmpty(v))
            {
                try
                {
                    balancers = await BalancerProbe.GetAvailableAsync(sp, auth, HttpContext.RequestAborted);
                    var pinnedEntry = balancers.FirstOrDefault(x => bal.Length > 0
                                              && string.Equals(x.balanser, bal, StringComparison.OrdinalIgnoreCase));
                    if (pinnedEntry != null && link.Length > 0)
                        link = BalancerProbe.SanitizeLink(link, pinnedEntry.url);
                    balancers = BalancerProbe.WithLink(balancers, bal, link);

                    tvdb = await TvdbClient.GetByImdbAsync(show.imdb_id, data.tmdb_id, HttpContext.RequestAborted);
                    tvdbId = tvdb?.tvdb_id ?? 0;
                    var refBalancer = balancers.FirstOrDefault(x => bal.Length > 0
                                              && string.Equals(x.balanser, bal, StringComparison.OrdinalIgnoreCase))
                                      ?? balancers.FirstOrDefault();
                    structureSource = await StructureResolver.ResolveAsync(show, tvdb, refBalancer, sp, auth, HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    Log.Warn($"[EpWatch] /subscribe resolve failed: {ex.Message}");
                }
            }

            int latest = show.latest_aired_season > 0 ? show.latest_aired_season : show.number_of_seasons;
            var baselineRow = new SubscriptionRow { target_season = data.target_season, structure_source = structureSource };
            var es = await EffectiveStructure.BuildAsync(baselineRow, show, tvdb, latest, L, HttpContext.RequestAborted);

            effectiveSeason = es.effectiveSeason;
            seasonTotal = es.seasonTotal;
            seasonAired = es.seasonAired;
            initialLastEpisode = es.aired.Count == 0 ? 0 : es.aired.Max(e => e.episode);

            if (!string.IsNullOrEmpty(v) && balancers != null && effectiveSeason > 0)
            {
                try
                {
                    foreach (var b in balancers)
                    {
                        if (bal.Length > 0 && !string.Equals(b.balanser, bal, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var probed = await BalancerProbe.ProbeAsync(b, sp, effectiveSeason, v, auth, HttpContext.RequestAborted);
                        if (probed.maxEpisode > initialLastVoiceEpisode)
                            initialLastVoiceEpisode = probed.maxEpisode;
                    }
                    Log.Dbg($"[EpWatch] /subscribe initial probe: voice=\"{v}\" season={effectiveSeason} -> last_voice_episode={initialLastVoiceEpisode}, structure={structureSource}, tvdb={tvdbId}");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[EpWatch] /subscribe initial probe failed: {ex.Message}");
                }
            }
        }

        SubscriptionRow newRow = null;
        if (existing == null)
        {
            newRow = new SubscriptionRow
            {
                chat_id = user.chat_id,
                tmdb_id = data.tmdb_id,
                title = data.title,
                voice = v,
                balancer = bal,
                balancer_link = link,
                last_season = effectiveSeason,
                last_episode = initialLastEpisode,
                last_voice_episode = initialLastVoiceEpisode,
                poster_path = data.poster_path ?? "",
                season_total = seasonTotal,
                season_aired = seasonAired,
                target_season = data.target_season,
                tvdb_id = tvdbId,
                structure_source = structureSource,
                show_status = show?.status ?? "",
                next_air_date = show?.next_air_date,
                subscribed_at = DateTime.UtcNow,
                last_checked_at = DateTime.UtcNow,
                next_check_at = DateTime.UtcNow.AddMinutes(Math.Max(5, ModInit.conf.check_interval_minutes))
            };
            db.subs.Add(newRow);
        }
        else
        {
            existing.title = data.title;
            existing.voice = v;
            existing.balancer = bal;
            existing.balancer_link = link;
            existing.poster_path = data.poster_path ?? existing.poster_path;
            existing.target_season = data.target_season;
            existing.tvdb_id = tvdbId;
            existing.structure_source = structureSource;
            existing.last_season = effectiveSeason;
            existing.last_episode = initialLastEpisode;
            existing.last_voice_episode = initialLastVoiceEpisode;
            existing.season_total = seasonTotal;
            existing.season_aired = seasonAired;
            existing.show_status = show?.status ?? existing.show_status;
            existing.next_air_date = show?.next_air_date ?? existing.next_air_date;
            existing.last_checked_at = DateTime.UtcNow;
            existing.next_check_at = DateTime.UtcNow.AddMinutes(Math.Max(5, ModInit.conf.check_interval_minutes));
            db.subs.Update(existing);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] /subscribe SaveChanges failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Json(new { success = false, msg = "db_error", detail = ex.Message });
        }

        Log.Info($"[EpWatch] /subscribe OK chat_id={user.chat_id} tmdb_id={data.tmdb_id} voice=\"{v}\" balancer=\"{bal}\" pinned={(link.Length > 0 ? "yes" : "no")}");

        if (Notifier.Ready)
        {
            var vTxt = string.IsNullOrEmpty(v) ? Strings.T(L, "voice_any") : v;
            var text = Strings.T(L, "sub_added", Notifier.Esc(data.title), Notifier.Esc(vTxt));
            _ = Notifier.SendTextAsync(user.chat_id, text, HttpContext.RequestAborted);

            if (data.target_season > 0 && show != null && string.IsNullOrEmpty(v))
            {
                var sInfo = show.seasons.FirstOrDefault(x => x.season_number == data.target_season);
                if (sInfo != null && sInfo.episode_count > 0 && seasonAired >= sInfo.episode_count)
                {
                    long subId = newRow?.Id ?? existing?.Id ?? 0;
                    if (subId > 0)
                    {
                        var askText = Strings.T(L, "already_aired_body", Notifier.Esc(data.title), data.target_season, seasonAired, sInfo.episode_count);
                        var kb = TgMarkup.InlineKeyboard(
                            new (string, string)[] { (Strings.T(L, "btn_switch_auto"), "auto_" + subId) },
                            new (string, string)[] { (Strings.T(L, "btn_keep"), "noop") }
                        );
                        _ = Notifier.Bot.SendMessageAsync(user.chat_id, askText, kb, Notifier.PARSE_MODE, HttpContext.RequestAborted);
                    }
                }
            }
        }

        return Json(new { success = true });
    }

    async Task<ActionResult> SubscribeMovie(SqlContext db, TgUserRow user, SubscribeBody data)
    {
        var L = Strings.Normalize(user.lang);
        var movie = await TmdbClient.GetMovieAsync(data.tmdb_id, L, HttpContext.RequestAborted);
        var title = !string.IsNullOrWhiteSpace(movie?.name) ? movie.name : data.title;
        var bal = (data.balancer ?? "").Trim();
        var link = bal.Length > 0 ? BalancerProbe.SanitizeLink(data.link, null) : "";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var auth = RequestAuth();
            var sp = new Models.ShowParams
            {
                tmdb_id = data.tmdb_id,
                title = title,
                original_title = movie?.original_name ?? "",
                original_language = movie?.original_language ?? "",
                imdb_id = movie?.imdb_id ?? "",
                year = movie?.first_air_year ?? 0
            };

            var balancers = await BalancerProbe.GetAvailableAsync(sp, auth, HttpContext.RequestAborted, movie: true);
            if (bal.Length > 0)
            {
                balancers = balancers.Where(b => string.Equals(b.balanser, bal, StringComparison.OrdinalIgnoreCase)).ToList();
                if (balancers.Count > 0 && link.Length > 0)
                    link = BalancerProbe.SanitizeLink(link, balancers[0].url);
                balancers = BalancerProbe.WithLink(balancers, bal, link);
            }

            var probeTasks = balancers
                .Select(b => BalancerProbe.ProbeMovieAsync(b, sp, auth, HttpContext.RequestAborted))
                .ToArray();
            var probedAll = await Task.WhenAll(probeTasks);
            for (int i = 0; i < balancers.Count; i++)
            {
                if (!probedAll[i].available) continue;
                var vlist = probedAll[i].voices.Count > 0 ? probedAll[i].voices : new System.Collections.Generic.List<string> { "" };
                foreach (var v in vlist) seen.Add(EpisodeChecker.MovieKey(balancers[i].balanser, v));
            }
            Log.Dbg($"[EpWatch] /subscribe movie baseline: {seen.Count} voices already present");
        }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] /subscribe movie baseline probe failed: {ex.Message}");
        }

        var seedCsv = string.Join("\n", seen);

        var existing = await db.subs.FirstOrDefaultAsync(s =>
            s.chat_id == user.chat_id && s.tmdb_id == data.tmdb_id && s.media_type == "movie");

        if (existing == null)
        {
            db.subs.Add(new SubscriptionRow
            {
                chat_id = user.chat_id,
                tmdb_id = data.tmdb_id,
                title = title,
                media_type = "movie",
                voice = "",
                balancer = bal,
                balancer_link = link,
                seen_voices = seedCsv,
                poster_path = data.poster_path ?? movie?.poster_path ?? "",
                subscribed_at = DateTime.UtcNow,
                last_checked_at = DateTime.UtcNow,
                next_check_at = DateTime.UtcNow.AddMinutes(Math.Max(5, ModInit.conf.check_interval_minutes))
            });
        }
        else
        {
            existing.title = title;
            existing.balancer = bal;
            existing.balancer_link = link;
            existing.seen_voices = seedCsv;
            existing.poster_path = data.poster_path ?? existing.poster_path;
            existing.next_check_at = DateTime.UtcNow.AddMinutes(Math.Max(5, ModInit.conf.check_interval_minutes));
            db.subs.Update(existing);
        }

        try { await db.SaveChangesAsync(); }
        catch (Exception ex)
        {
            Log.Warn($"[EpWatch] /subscribe movie save failed: {ex.Message}");
            return Json(new { success = false, msg = "db_error" });
        }

        if (Notifier.Ready)
        {
            var balName = bal.Length == 0 ? Strings.T(L, "movie_any_balancer") : BalancerProbe.DisplayName(bal);
            _ = Notifier.SendTextAsync(user.chat_id, Strings.T(L, "movie_sub_added", Notifier.Esc(title), Notifier.Esc(balName)), HttpContext.RequestAborted);
        }

        Log.Info($"[EpWatch] /subscribe MOVIE chat_id={user.chat_id} tmdb_id={data.tmdb_id} balancer=\"{bal}\" pinned={(link.Length > 0 ? "yes" : "no")}");
        return Json(new { success = true });
    }
    #endregion

    public class UnsubscribeBody
    {
        public int tmdb_id { get; set; }
        public string voice { get; set; }
        public long id { get; set; }
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
        if (data.id > 0) q = q.Where(s => s.Id == data.id);
        else if (data.voice != null) q = q.Where(s => s.voice == data.voice);

        var rows = await q.ToListAsync();
        if (rows.Count == 0)
            return Json(new { success = true, removed = 0 });

        var snapshot = rows.Select(r => new { r.title, voice = Notifier.VoiceText(r.voice) }).ToList();
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

        var rows = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == user.chat_id)
            .OrderByDescending(s => s.subscribed_at)
            .ToListAsync();

        var list = rows.Select(s => new
        {
            id = s.Id,
            tmdb_id = s.tmdb_id,
            title = s.title,
            voice = s.voice,
            balancer = s.balancer,
            balancer_name = BalancerProbe.DisplayName(s.balancer),
            poster_path = s.poster_path,
            last_season = s.last_season,
            last_episode = s.last_episode,
            last_voice_episode = s.last_voice_episode,
            season_total = s.season_total,
            season_aired = s.season_aired,
            target_season = s.target_season,
            show_status = s.show_status,
            structure_source = s.structure_source,
            media_type = s.media_type,
            seen_voices = s.seen_voices,
            last_checked_at = s.last_checked_at,
            subscribed_at = s.subscribed_at
        }).ToArray();

        return Json(new { success = true, linked = true, results = list });
    }
    #endregion

    #region /epwatch/schedule
    [HttpGet]
    [Route("epwatch/schedule")]
    public async Task<ActionResult> Schedule()
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = true, linked = false, results = new object[0] });

        using var db = SqlContext.Create();
        var user = await db.users.AsNoTracking().FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null)
            return Json(new { success = true, linked = false, results = new object[0] });

        var lang = Strings.Normalize(user.lang);
        var ct = HttpContext.RequestAborted;

        var rows = await db.subs.AsNoTracking()
            .Where(s => s.chat_id == user.chat_id)
            .ToListAsync();

        var seriesSubs = rows
            .Where(s => !string.Equals(s.media_type, "movie", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-7);
        var until = today.AddDays(120);

        var raw = new List<(DateTime date, object item)>();

        foreach (var grp in seriesSubs.GroupBy(s => s.tmdb_id))
        {
            if (ct.IsCancellationRequested) break;

            var sub = grp.First();
            var show = await TmdbClient.GetShowAsync(sub.tmdb_id, lang, ct);
            if (show == null) continue;

            int season = sub.target_season > 0 ? sub.target_season : show.latest_aired_season;
            if (season <= 0) season = show.number_of_seasons;
            if (season <= 0) continue;

            var eps = await TmdbClient.GetSeasonAsync(show.id, season, lang, ct);
            var voice = grp.Select(g => Notifier.VoiceText(g.voice)).FirstOrDefault(v => v.Length > 0) ?? "";
            var balancer = grp.Select(g => g.balancer).FirstOrDefault(b => !string.IsNullOrEmpty(b)) ?? "";

            int added = 0;
            foreach (var ep in eps
                .Where(e => e.air_date.HasValue && e.air_date.Value.Date >= since && e.air_date.Value.Date <= until)
                .OrderBy(e => e.air_date.Value))
            {
                raw.Add((ep.air_date.Value.Date, new
                {
                    tmdb_id = sub.tmdb_id,
                    title = sub.title,
                    poster_path = sub.poster_path,
                    season = ep.season,
                    episode = ep.episode,
                    name = ep.name ?? "",
                    still_url = ep.still_url,
                    air_date = ep.air_date.Value.ToString("yyyy-MM-dd"),
                    aired = ep.air_date.Value.Date <= today,
                    voice = voice,
                    balancer = balancer,
                    balancer_name = BalancerProbe.DisplayName(balancer)
                }));

                if (++added >= 10) break;
            }
        }

        var results = raw
            .OrderBy(x => x.date)
            .Select(x => x.item)
            .Take(80)
            .ToArray();

        return Json(new { success = true, linked = true, results });
    }
    #endregion

    #region /epwatch/notices
    [HttpGet]
    [Route("epwatch/notices")]
    public async Task<ActionResult> Notices(long since = -1, int limit = 30)
    {
        var uid = Uid();
        if (string.IsNullOrEmpty(uid))
            return Json(new { success = true, linked = false, last_id = 0, results = new object[0] });

        using var db = SqlContext.Create();
        var user = await db.users.AsNoTracking().FirstOrDefaultAsync(u => u.lampac_uid == uid);
        if (user == null)
            return Json(new { success = true, linked = false, last_id = 0, results = new object[0] });

        long lastId = await db.notices.AsNoTracking()
            .Where(n => n.chat_id == user.chat_id)
            .OrderByDescending(n => n.Id)
            .Select(n => n.Id)
            .FirstOrDefaultAsync();

        if (since < 0)
            return Json(new { success = true, linked = true, last_id = lastId, results = new object[0] });

        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var rows = await db.notices.AsNoTracking()
            .Where(n => n.chat_id == user.chat_id && n.Id > since)
            .OrderByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync();

        var results = rows.OrderBy(n => n.Id).Select(n => new
        {
            id = n.Id,
            kind = n.kind,
            tmdb_id = n.tmdb_id,
            media_type = n.media_type,
            title = n.title,
            poster_path = n.poster_path,
            season = n.season,
            episode = n.episode,
            name = n.ep_name,
            still_url = n.still_url,
            voice = n.voice,
            balancer = n.balancer,
            balancer_name = BalancerProbe.DisplayName(n.balancer),
            extra = n.extra,
            time = new DateTimeOffset(DateTime.SpecifyKind(n.created_at, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
        }).ToArray();

        return Json(new { success = true, linked = true, last_id = lastId, results });
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
        var news = await db.notices.Where(n => n.chat_id == chatId).ToListAsync();
        db.notices.RemoveRange(news);
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
