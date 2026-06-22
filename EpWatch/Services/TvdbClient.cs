using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using EpWatch.Models;

namespace EpWatch.Services;

public static class TvdbClient
{
    static readonly HttpClient http = CreateHttp();
    static readonly ConcurrentDictionary<string, (DateTime exp, object value)> cache = new();
    static readonly TvdbShow Absent = new() { tvdb_id = -1 };

    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EpWatch/0.1");
        return c;
    }

    static T GetCached<T>(string key) where T : class
        => cache.TryGetValue(key, out var e) && e.exp > DateTime.UtcNow ? e.value as T : null;

    static void SetCached(string key, object value, TimeSpan ttl)
        => cache[key] = (DateTime.UtcNow + ttl, value);

    static string Host()
    {
        var h = ModInit.conf.tvdb_host;
        return string.IsNullOrWhiteSpace(h) ? "https://skyhook.sonarr.tv/v1/tvdb" : h.TrimEnd('/');
    }

    public static async Task<TvdbShow> GetByImdbAsync(string imdbId, int tmdbId, CancellationToken ct)
    {
        if (!ModInit.conf.tvdb_enable || string.IsNullOrWhiteSpace(imdbId))
            return null;

        var key = $"imdb:{imdbId}";
        var hit = GetCached<TvdbShow>(key);
        if (hit != null) return ReferenceEquals(hit, Absent) ? null : hit;

        try
        {
            int tvdbId = await ResolveTvdbIdAsync(imdbId, tmdbId, ct);
            if (tvdbId <= 0)
            {
                SetCached(key, Absent, TimeSpan.FromHours(12));
                return null;
            }

            var show = await GetShowAsync(tvdbId, ct);
            SetCached(key, show ?? (object)Absent, TimeSpan.FromHours(show != null ? 24 : 12));
            return show;
        }
        catch (Exception ex)
        {
            Log.Dbg($"[EpWatch] tvdb imdb={imdbId} failed: {ex.Message}");
            return null;
        }
    }

    static async Task<int> ResolveTvdbIdAsync(string imdbId, int tmdbId, CancellationToken ct)
    {
        var url = $"{Host()}/search/en/?term=imdb:{HttpUtility.UrlEncode(imdbId)}";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return 0;

        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return 0;

        var arr = JArray.Parse(raw);
        if (arr.Count == 0) return 0;

        if (tmdbId > 0)
        {
            foreach (var it in arr)
                if ((it.Value<int?>("tmdbId") ?? 0) == tmdbId)
                    return it.Value<int?>("tvdbId") ?? 0;
        }
        return arr[0].Value<int?>("tvdbId") ?? 0;
    }

    public static async Task<TvdbShow> GetShowAsync(int tvdbId, CancellationToken ct)
    {
        if (tvdbId <= 0) return null;

        var key = $"show:{tvdbId}";
        var hit = GetCached<TvdbShow>(key);
        if (hit != null) return ReferenceEquals(hit, Absent) ? null : hit;

        try
        {
            var url = $"{Host()}/shows/en/{tvdbId}";
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                SetCached(key, Absent, TimeSpan.FromHours(6));
                return null;
            }

            var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            var show = new TvdbShow
            {
                tvdb_id = jo.Value<int?>("tvdbId") ?? tvdbId,
                tmdb_id = jo.Value<int?>("tmdbId") ?? 0,
                imdb_id = jo.Value<string>("imdbId") ?? "",
                status = jo.Value<string>("status") ?? ""
            };

            var eps = jo["episodes"] as JArray;
            if (eps != null)
            {
                foreach (var e in eps)
                {
                    int s = e.Value<int?>("seasonNumber") ?? -1;
                    if (s < 0) continue;

                    DateTime? air = DateTime.TryParse(e.Value<string>("airDate"), out var dt) ? dt : null;

                    show.episodes.Add(new TvdbEpisode
                    {
                        season = s,
                        episode = e.Value<int?>("episodeNumber") ?? 0,
                        absolute = e.Value<int?>("absoluteEpisodeNumber") ?? 0,
                        title = e.Value<string>("title") ?? "",
                        overview = e.Value<string>("overview") ?? "",
                        image = e.Value<string>("image") ?? "",
                        air_date = air
                    });
                }
            }

            SetCached(key, show, TimeSpan.FromHours(24));
            return show;
        }
        catch (Exception ex)
        {
            Log.Dbg($"[EpWatch] tvdb show={tvdbId} failed: {ex.Message}");
            return null;
        }
    }
}
