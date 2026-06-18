using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using EpWatch.Models;

namespace EpWatch.Services;

public static class TmdbClient
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly ConcurrentDictionary<string, (DateTime exp, object value)> cache = new();

    static T GetCached<T>(string key) where T : class
        => cache.TryGetValue(key, out var e) && e.exp > DateTime.UtcNow ? e.value as T : null;

    static void SetCached(string key, object value, TimeSpan ttl)
        => cache[key] = (DateTime.UtcNow + ttl, value);

    static string ApiKey()
    {
        var k = ModInit.conf.tmdb_api_key;
        if (!string.IsNullOrWhiteSpace(k))
            return k;

        try { return Shared.CoreInit.conf?.cub?.api_key; } catch { return null; }
    }

    public static string ResolveLang(string userLang)
    {
        if (string.IsNullOrWhiteSpace(userLang))
            return string.IsNullOrWhiteSpace(ModInit.conf.tmdb_lang) ? "uk-UA" : ModInit.conf.tmdb_lang;

        var l = userLang.Trim().ToLowerInvariant();
        return l switch
        {
            "ru" => "ru-RU",
            "uk" => "uk-UA",
            "en" => "en-US",
            _ => l
        };
    }

    public static async Task<TmdbShow> GetShowAsync(int tmdbId, string lang, CancellationToken ct)
    {
        var locale = ResolveLang(lang);
        var key = $"show:{tmdbId}:{locale}";
        var hit = GetCached<TmdbShow>(key);
        if (hit != null) return hit;

        var api = ApiKey();
        if (string.IsNullOrEmpty(api)) return null;

        try
        {
            using var resp = await http.GetAsync(
                $"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={api}&language={locale}&append_to_response=external_ids", ct);
            if (!resp.IsSuccessStatusCode) return null;

            var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            var show = new TmdbShow
            {
                id = tmdbId,
                name = jo.Value<string>("name"),
                original_name = jo.Value<string>("original_name"),
                original_language = jo.Value<string>("original_language") ?? "",
                poster_path = jo.Value<string>("poster_path"),
                number_of_seasons = jo.Value<int?>("number_of_seasons") ?? 0,
                status = jo.Value<string>("status") ?? "",
                imdb_id = (jo["external_ids"] as JObject)?.Value<string>("imdb_id") ?? ""
            };

            if (DateTime.TryParse(jo.Value<string>("first_air_date"), out var fad))
                show.first_air_year = fad.Year;

            int latest = 0;
            int currentTotal = 0;
            DateTime? next = null;
            var seasons = jo["seasons"] as JArray;
            if (seasons != null)
            {
                foreach (var s in seasons)
                {
                    var num = s.Value<int?>("season_number") ?? 0;
                    if (num <= 0) continue;

                    var ep = s.Value<int?>("episode_count") ?? 0;
                    var ad = s.Value<string>("air_date");
                    DateTime? airDt = DateTime.TryParse(ad, out var dt) ? dt : null;

                    show.seasons.Add(new TmdbSeasonInfo
                    {
                        season_number = num,
                        name = s.Value<string>("name") ?? "",
                        episode_count = ep,
                        air_date = airDt
                    });

                    if (ep > 0 && airDt.HasValue)
                    {
                        if (airDt.Value.Date <= DateTime.UtcNow.Date && num > latest)
                        {
                            latest = num;
                            currentTotal = ep;
                        }
                    }
                }
            }
            show.latest_aired_season = latest > 0 ? latest : show.number_of_seasons;
            show.current_season_total = currentTotal;

            var ne = jo["next_episode_to_air"] as JObject;
            if (ne != null && DateTime.TryParse(ne.Value<string>("air_date"), out var nd))
                next = nd;
            show.next_air_date = next;

            if (show.latest_aired_season > 0)
            {
                var seasonEps = await GetSeasonAsync(tmdbId, show.latest_aired_season, lang, ct);
                int aired = 0;
                foreach (var ep in seasonEps)
                    if (ep.air_date.HasValue && ep.air_date.Value.Date <= DateTime.UtcNow.Date) aired++;
                show.current_season_aired = aired;
                if (show.current_season_total <= 0)
                    show.current_season_total = seasonEps.Count;
            }

            SetCached(key, show, TimeSpan.FromHours(3));
            return show;
        }
        catch { return null; }
    }

    public static async Task<List<TmdbEpisode>> GetSeasonAsync(int tmdbId, int season, string lang, CancellationToken ct)
    {
        var locale = ResolveLang(lang);
        var key = $"season:{tmdbId}:{season}:{locale}";
        var hit = GetCached<List<TmdbEpisode>>(key);
        if (hit != null) return hit;

        var api = ApiKey();
        if (string.IsNullOrEmpty(api)) return new List<TmdbEpisode>();

        var result = new List<TmdbEpisode>();
        try
        {
            using var resp = await http.GetAsync(
                $"https://api.themoviedb.org/3/tv/{tmdbId}/season/{season}?api_key={api}&language={locale}", ct);
            if (!resp.IsSuccessStatusCode) return result;

            var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            var eps = jo["episodes"] as JArray;
            if (eps == null) return result;

            foreach (var e in eps)
            {
                DateTime? air = DateTime.TryParse(e.Value<string>("air_date"), out var dt) ? dt : null;
                var still = e.Value<string>("still_path");
                result.Add(new TmdbEpisode
                {
                    season = e.Value<int?>("season_number") ?? season,
                    episode = e.Value<int?>("episode_number") ?? 0,
                    name = e.Value<string>("name") ?? "",
                    overview = e.Value<string>("overview") ?? "",
                    air_date = air,
                    still_url = !string.IsNullOrEmpty(still) ? $"https://image.tmdb.org/t/p/w500{still}" : null
                });
            }

            SetCached(key, result, TimeSpan.FromHours(1));
            return result;
        }
        catch { return result; }
    }

    public static async Task<TmdbShow> GetMovieAsync(int tmdbId, string lang, CancellationToken ct)
    {
        var locale = ResolveLang(lang);
        var key = $"movie:{tmdbId}:{locale}";
        var hit = GetCached<TmdbShow>(key);
        if (hit != null) return hit;

        var api = ApiKey();
        if (string.IsNullOrEmpty(api)) return null;

        try
        {
            using var resp = await http.GetAsync(
                $"https://api.themoviedb.org/3/movie/{tmdbId}?api_key={api}&language={locale}&append_to_response=external_ids", ct);
            if (!resp.IsSuccessStatusCode) return null;

            var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            var show = new TmdbShow
            {
                id = tmdbId,
                name = jo.Value<string>("title"),
                original_name = jo.Value<string>("original_title"),
                original_language = jo.Value<string>("original_language") ?? "",
                poster_path = jo.Value<string>("poster_path"),
                status = jo.Value<string>("status") ?? "",
                imdb_id = jo.Value<string>("imdb_id") ?? (jo["external_ids"] as JObject)?.Value<string>("imdb_id") ?? ""
            };

            if (DateTime.TryParse(jo.Value<string>("release_date"), out var rd))
                show.first_air_year = rd.Year;

            SetCached(key, show, TimeSpan.FromHours(6));
            return show;
        }
        catch { return null; }
    }
}
