using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using EpWatch.Models;
using Shared;

namespace EpWatch.Services;

public static class BalancerProbe
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static volatile int _detectedPort;
    static volatile string _detectedHost;

    public static void RememberLocal(string host, int port)
    {
        if (port > 0) _detectedPort = port;
        if (!string.IsNullOrEmpty(host)) _detectedHost = host;
    }

    public static string HostBase()
    {
        var conf = ModInit.conf.lampac_host;
        if (!string.IsNullOrWhiteSpace(conf))
            return conf.TrimEnd('/');

        if (_detectedPort > 0)
        {
            var host = string.IsNullOrEmpty(_detectedHost) ? "127.0.0.1" : _detectedHost;
            return $"http://{host}:{_detectedPort}";
        }

        try
        {
            var listen = CoreInit.conf?.listen;
            if (listen != null)
            {
                var host = string.IsNullOrWhiteSpace(listen.localhost) ? "127.0.0.1" : listen.localhost;
                var port = listen.port > 0 ? listen.port : 9118;
                return $"http://{host}:{port}";
            }
        }
        catch { }

        return "http://127.0.0.1:9118";
    }

    public static async Task<List<BalancerEntry>> GetAvailableAsync(
        ShowParams sp, AuthQs auth, CancellationToken ct)
    {
        var url =
            $"{HostBase()}/lite/events" +
            "?serial=1&source=tmdb";

        if (sp != null)
        {
            if (sp.tmdb_id > 0) url += $"&tmdb_id={sp.tmdb_id}";
            if (!string.IsNullOrEmpty(sp.imdb_id)) url += "&imdb_id=" + HttpUtility.UrlEncode(sp.imdb_id);
            if (!string.IsNullOrEmpty(sp.title)) url += "&title=" + HttpUtility.UrlEncode(sp.title);
            if (!string.IsNullOrEmpty(sp.original_title)) url += "&original_title=" + HttpUtility.UrlEncode(sp.original_title);
            if (!string.IsNullOrEmpty(sp.original_language)) url += "&original_language=" + HttpUtility.UrlEncode(sp.original_language);
            if (sp.year > 0) url += $"&year={sp.year}";
        }

        url += BuildAuthQs(auth);

        var list = new List<BalancerEntry>();
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EpWatch] /lite/events HTTP {(int)resp.StatusCode}");
                return list;
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
            {
                Console.WriteLine($"[EpWatch] /lite/events returned empty (raw={(raw ?? "null")}) url={url}");
                return list;
            }

            JToken jt;
            try { jt = JToken.Parse(raw); }
            catch
            {
                var preview = raw.Length > 300 ? raw.Substring(0, 300) : raw;
                Console.WriteLine($"[EpWatch] /lite/events non-JSON ({raw.Length} chars): {preview}");
                return list;
            }

            JArray online = jt as JArray ?? jt["online"] as JArray;
            if (online == null)
            {
                Console.WriteLine($"[EpWatch] /lite/events unexpected shape, type={jt.Type}");
                return list;
            }

            int objCount = 0, strCount = 0;
            foreach (var e in online)
            {
                if (e.Type == JTokenType.String) { strCount++; continue; }
                if (e.Type != JTokenType.Object) continue;
                objCount++;

                var u = e.Value<string>("url");
                if (string.IsNullOrEmpty(u)) continue;

                if (u.Contains("{localhost}"))
                    u = u.Replace("{localhost}", HostBase());

                list.Add(new BalancerEntry
                {
                    name = e.Value<string>("name") ?? "",
                    balanser = e.Value<string>("balanser") ?? e.Value<string>("plugin") ?? GuessBalancer(u),
                    url = u
                });
            }

            if (strCount > 0)
                Console.WriteLine($"[EpWatch] /lite/events returned {strCount} string-codes (checkOnlineSearch mode), parsed objects={objCount}");

            Console.WriteLine($"[EpWatch] /lite/events tmdb={sp?.tmdb_id ?? 0} (auth={(auth?.IsEmpty ?? true ? "no" : "yes")}, imdb={(string.IsNullOrEmpty(sp?.imdb_id) ? "no" : "yes")}) -> {list.Count} balancers via {HostBase()}");
        }
        catch (Exception ex) { Console.WriteLine($"[EpWatch] /lite/events failed: {ex.Message}"); }

        var allowed = ModInit.conf.allowed_balancers;
        if (allowed != null && allowed.Length > 0)
            list = list.Where(x => allowed.Contains(x.balanser, StringComparer.OrdinalIgnoreCase)).ToList();

        return list;
    }

    static string GuessBalancer(string url)
    {
        var m = Regex.Match(url, @"/lite/([a-z0-9_\-]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
    }

    public static async Task<(int maxEpisode, List<BalancerVoice> voices)> ProbeAsync(
        BalancerEntry entry, ShowParams sp, int desiredSeason, string voiceName, AuthQs auth, CancellationToken ct)
    {
        var voices = new List<BalancerVoice>();
        int maxEp = 0;
        var timeoutSec = Math.Max(3, ModInit.conf.balancer_timeout_seconds);

        var first = await FetchAsync(entry, sp, auth, desiredSeason, -1, timeoutSec, ct);
        if (first.jo != null)
        {
            Console.WriteLine($"[EpWatch] probe {entry.balanser} s={desiredSeason} keys: [{string.Join(",", first.jo.Properties().Select(p => p.Name))}]");
            CollectVoices(first.jo, voices, entry.balanser);
            maxEp = CollectMaxEpisode(first.jo, voiceName);

            if (!string.IsNullOrEmpty(voiceName))
            {
                int tid = TryFindVoiceTid(first.jo, voiceName);
                if (tid >= 0)
                {
                    var byVoiceUrl = AppendQs(first.url, $"t={tid}");
                    Console.WriteLine($"[EpWatch] probe {entry.balanser} voice url: {byVoiceUrl}");
                    var byVoice = await FetchByUrlAsync(byVoiceUrl, auth, timeoutSec, ct);
                    if (byVoice != null)
                    {
                        Console.WriteLine($"[EpWatch] probe {entry.balanser} t={tid} keys: [{string.Join(",", byVoice.Properties().Select(p => p.Name))}]");
                        var voiceMax = CollectMaxEpisode(byVoice, null);
                        if (voiceMax > maxEp) maxEp = voiceMax;
                    }
                }
            }
        }

        if (voices.Count == 0 && maxEp == 0)
        {
            var disc = await FetchAsync(entry, sp, auth, -1, -1, timeoutSec, ct);
            if (disc.jo != null)
            {
                Console.WriteLine($"[EpWatch] probe {entry.balanser} discovery keys: [{string.Join(",", disc.jo.Properties().Select(p => p.Name))}]");
                CollectVoices(disc.jo, voices, entry.balanser);
                var tree = ExtractSeasonTree(disc.jo);
                if (tree.Count > 0)
                {
                    int pick = tree.ContainsKey(desiredSeason)
                        ? desiredSeason
                        : tree.Keys.Max();
                    Console.WriteLine($"[EpWatch] probe {entry.balanser} season tree: [{string.Join(",", tree.Keys)}] -> s={pick}");

                    var followUrl = tree[pick];
                    Console.WriteLine($"[EpWatch] probe {entry.balanser} following season url: {followUrl}");
                    var second = await FetchByUrlAsync(followUrl, auth, timeoutSec, ct);
                    if (second != null)
                    {
                        Console.WriteLine($"[EpWatch] probe {entry.balanser} s={pick} keys: [{string.Join(",", second.Properties().Select(p => p.Name))}]");
                        CollectVoices(second, voices, entry.balanser);
                        maxEp = CollectMaxEpisode(second, voiceName);

                        if (!string.IsNullOrEmpty(voiceName))
                        {
                            int tid = TryFindVoiceTid(second, voiceName);
                            if (tid >= 0)
                            {
                                var byVoiceUrl = AppendQs(followUrl, $"t={tid}");
                                Console.WriteLine($"[EpWatch] probe {entry.balanser} voice url: {byVoiceUrl}");
                                var byVoice = await FetchByUrlAsync(byVoiceUrl, auth, timeoutSec, ct);
                                if (byVoice != null)
                                {
                                    Console.WriteLine($"[EpWatch] probe {entry.balanser} t={tid} keys: [{string.Join(",", byVoice.Properties().Select(p => p.Name))}]");
                                    var voiceMax = CollectMaxEpisode(byVoice, null);
                                    if (voiceMax > maxEp) maxEp = voiceMax;
                                }
                            }
                        }
                    }
                }
                else
                {
                    maxEp = CollectMaxEpisode(disc.jo, voiceName);
                }
            }
        }

        Console.WriteLine($"[EpWatch] probe {entry.balanser} parsed: voices={voices.Count}, max_e={maxEp}");
        return (maxEp, voices);
    }

    static async Task<JObject> FetchByUrlAsync(string rawUrl, AuthQs auth, int timeoutSec, CancellationToken ct)
    {
        var url = NormalizeUrl(rawUrl);
        if (!Regex.IsMatch(url, @"[?&]rjson=true"))
            url = AppendQs(url, "rjson=true");
        var authQs = BuildAuthQs(auth);
        if (authQs.Length > 0) url = AppendQs(url, authQs.TrimStart('&'));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            using var resp = await http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EpWatch] follow HTTP {(int)resp.StatusCode}: {url}");
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(body) || body == "null") return null;
            try { return JObject.Parse(body); }
            catch { return null; }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Console.WriteLine($"[EpWatch] follow timeout: {url}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] follow failed: {ex.Message}");
            return null;
        }
    }

    static async Task<(JObject jo, string url)> FetchAsync(BalancerEntry entry, ShowParams sp, AuthQs auth, int s, int t, int timeoutSec, CancellationToken ct)
    {
        var qs = BuildShowQs(sp, entry.balanser, auth) + "&rjson=true";
        if (s >= 0) qs += $"&s={s}"; else qs += "&s=-1";
        if (t >= 0) qs += $"&t={t}";
        var url = AppendQs(NormalizeUrl(entry.url), qs);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            using var resp = await http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EpWatch] probe {entry.balanser} s={s} HTTP {(int)resp.StatusCode}");
                return (null, url);
            }
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(body) || body == "null")
            {
                Console.WriteLine($"[EpWatch] probe {entry.balanser} s={s} empty body");
                return (null, url);
            }
            try { return (JObject.Parse(body), url); }
            catch
            {
                var preview = body.Length > 200 ? body.Substring(0, 200) : body;
                Console.WriteLine($"[EpWatch] probe {entry.balanser} s={s} non-JSON ({body.Length} chars): {preview}");
                return (null, url);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Console.WriteLine($"[EpWatch] probe {entry.balanser} s={s} timeout after {timeoutSec}s");
            return (null, url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] probe {entry.balanser} s={s} failed: {ex.Message}");
            return (null, url);
        }
    }

    static void CollectVoices(JObject jo, List<BalancerVoice> voices, string balancer)
    {
        var seen = new HashSet<string>(voices.Select(x => x.name), StringComparer.OrdinalIgnoreCase);

        var v = jo["voice"] as JArray;
        if (v != null)
        {
            foreach (var vi in v)
            {
                var name = vi.Value<string>("name");
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

                int t = 0;
                var vu = vi.Value<string>("url") ?? "";
                var tm = Regex.Match(vu, @"[?&]t=(\d+)");
                if (tm.Success) int.TryParse(tm.Groups[1].Value, out t);

                voices.Add(new BalancerVoice { name = name, t = t, balancer = balancer });
            }
        }

        var data = jo["data"] as JArray;
        if (data != null)
        {
            var seenDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in data)
            {
                var details = d.Value<string>("details") ?? d.Value<string>("voice_name") ?? "";
                if (string.IsNullOrWhiteSpace(details) || !seenDetails.Add(details)) continue;
                foreach (var piece in details.Split(','))
                {
                    var name = piece.Trim().Trim('"');
                    if (name.Length > 1 && seen.Add(name))
                        voices.Add(new BalancerVoice { name = name, t = 0, balancer = balancer });
                }
            }
        }
    }

    static int TryFindVoiceTid(JObject jo, string voiceName)
    {
        if (string.IsNullOrEmpty(voiceName)) return -1;
        var v = jo["voice"] as JArray;
        if (v == null) return -1;

        foreach (var vi in v)
        {
            var name = vi.Value<string>("name");
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.Equals(voiceName, StringComparison.OrdinalIgnoreCase)) continue;

            var url = vi.Value<string>("url") ?? "";
            var m = Regex.Match(url, @"[?&]t=(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var t))
                return t;
        }
        return -1;
    }

    static int CollectMaxEpisode(JObject jo, string voiceName)
    {
        var data = jo["data"] as JArray;
        if (data == null) return 0;

        int maxEp = 0;
        foreach (var d in data)
        {
            int ep = d.Value<int?>("e") ?? d.Value<int?>("episode") ?? 0;
            if (ep <= 0) continue;

            if (!string.IsNullOrEmpty(voiceName))
            {
                var details = d.Value<string>("details") ?? d.Value<string>("voice_name") ?? "";
                if (string.IsNullOrEmpty(details)) continue;
                if (!details.Contains(voiceName, StringComparison.OrdinalIgnoreCase)) continue;
            }

            if (ep > maxEp) maxEp = ep;
        }
        return maxEp;
    }

    static Dictionary<int, string> ExtractSeasonTree(JObject jo)
    {
        var seasons = new Dictionary<int, string>();
        var data = jo["data"] as JArray;
        if (data == null) return seasons;

        foreach (var d in data)
        {
            var u = d.Value<string>("url") ?? d.Value<string>("playlist") ?? "";
            if (string.IsNullOrEmpty(u)) continue;

            if (d.Value<int?>("e").HasValue) continue;

            int sNum = d.Value<int?>("season_number") ?? d.Value<int?>("season") ?? 0;

            if (sNum <= 0)
            {
                var m = Regex.Match(u, @"[?&]season=(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var us2) && us2 > 0)
                    sNum = us2;
            }

            if (sNum <= 0)
            {
                var m = Regex.Match(u, @"[?&]s=(\d+)(?!\d)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var us3) && us3 > 0)
                    sNum = us3;
            }

            if (sNum <= 0)
            {
                var method = d.Value<string>("method") ?? "";
                if (method == "link")
                {
                    var idStr = d.Value<string>("id") ?? "";
                    if (int.TryParse(idStr, out var idNum) && idNum > 0 && idNum < 100)
                        sNum = idNum;
                }
            }

            if (sNum > 0 && !seasons.ContainsKey(sNum))
                seasons[sNum] = u;
        }
        return seasons;
    }

    public static async Task<int> ProbeForVoiceAsync(BalancerEntry entry, ShowParams sp, int season, int t, AuthQs auth, CancellationToken ct)
    {
        var probeUrl = NormalizeUrl(entry.url);
        var qs = BuildShowQs(sp, entry.balanser, auth) + $"&rjson=true&s={season}&t={t}";
        var url = AppendQs(probeUrl, qs);
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return 0;

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body) || body == "null") return 0;

            var jo = JObject.Parse(body);
            var data = jo["data"] as JArray;
            if (data == null) return 0;

            int max = 0;
            foreach (var d in data)
            {
                int ep = d.Value<int?>("e") ?? 0;
                if (ep > max) max = ep;
            }
            return max;
        }
        catch { return 0; }
    }

    static readonly HashSet<string> _clarificationBalancers = new(StringComparer.OrdinalIgnoreCase)
    {
        "filmix", "filmixtv", "fxapi", "kinoukr", "rezka", "rhsprem", "kinopub", "alloha",
        "fancdn", "kinotochka", "remux", "kinogo", "kinobase", "getstv", "leproduction"
    };

    static string BuildShowQs(ShowParams sp, string balancer, AuthQs auth)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("serial=1&source=tmdb");
        if (sp != null)
        {
            if (sp.tmdb_id > 0) sb.Append("&tmdb_id=").Append(sp.tmdb_id).Append("&id=").Append(sp.tmdb_id);
            if (!string.IsNullOrEmpty(sp.title)) sb.Append("&title=").Append(HttpUtility.UrlEncode(sp.title));
            if (!string.IsNullOrEmpty(sp.original_title)) sb.Append("&original_title=").Append(HttpUtility.UrlEncode(sp.original_title));
            if (!string.IsNullOrEmpty(sp.original_language)) sb.Append("&original_language=").Append(HttpUtility.UrlEncode(sp.original_language));
            if (!string.IsNullOrEmpty(sp.imdb_id)) sb.Append("&imdb_id=").Append(HttpUtility.UrlEncode(sp.imdb_id));
            if (sp.year > 0) sb.Append("&year=").Append(sp.year);

            var lang = sp.original_language?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(balancer)
                && _clarificationBalancers.Contains(balancer)
                && (lang == "ru" || lang == "ja" || lang == "ko" || lang == "zh" || lang == "cn"))
            {
                sb.Append("&clarification=1");
            }
        }
        sb.Append(BuildAuthQs(auth));
        return sb.ToString();
    }

    static string BuildAuthQs(AuthQs a)
    {
        var overrideUid = ModInit.conf.balancer_uid;
        if (!string.IsNullOrWhiteSpace(overrideUid))
        {
            var enc = HttpUtility.UrlEncode(overrideUid.Trim());
            return $"&token={enc}&account_email={enc}&uid={enc}";
        }

        if (a == null || a.IsEmpty) return "";
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(a.token))         sb.Append("&token=").Append(HttpUtility.UrlEncode(a.token));
        if (!string.IsNullOrEmpty(a.account_email)) sb.Append("&account_email=").Append(HttpUtility.UrlEncode(a.account_email));
        if (!string.IsNullOrEmpty(a.uid))           sb.Append("&uid=").Append(HttpUtility.UrlEncode(a.uid));
        if (!string.IsNullOrEmpty(a.box_mac))       sb.Append("&box_mac=").Append(HttpUtility.UrlEncode(a.box_mac));
        return sb.ToString();
    }

    static string NormalizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.Contains("{localhost}")) url = url.Replace("{localhost}", HostBase());
        if (url.StartsWith("//")) return "http:" + url;
        if (url.StartsWith("/")) return HostBase() + url;
        return url;
    }

    static string AppendQs(string url, string qs)
        => url.Contains('?') ? url + "&" + qs : url + "?" + qs;
}
