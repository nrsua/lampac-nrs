using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EpWatch.Models;

namespace EpWatch.Services;

public sealed class EffectiveStructure
{
    public string source { get; private set; } = StructureResolver.TMDB;
    public int effectiveSeason { get; private set; }
    public List<TmdbEpisode> aired { get; private set; } = new();
    public int seasonTotal { get; private set; }
    public int seasonAired { get; private set; }

    public static async Task<EffectiveStructure> BuildAsync(
        SubscriptionRow sub, TmdbShow show, TvdbShow tvdb, int latestSeason, string lang, CancellationToken ct)
    {
        bool tvdbReady = tvdb != null && tvdb.episodes.Count > 0;

        if (tvdbReady && string.Equals(sub.structure_source, StructureResolver.ABSOLUTE, StringComparison.OrdinalIgnoreCase))
            return await BuildAbsoluteAsync(sub, show, tvdb, lang, ct);

        if (tvdbReady && string.Equals(sub.structure_source, StructureResolver.TVDB, StringComparison.OrdinalIgnoreCase))
            return await BuildTvdbAsync(sub, show, tvdb, lang, ct);

        return await BuildTmdbAsync(sub, show, tvdb, latestSeason, lang, ct);
    }

    static async Task<EffectiveStructure> BuildAbsoluteAsync(
        SubscriptionRow sub, TmdbShow show, TvdbShow tvdb, string lang, CancellationToken ct)
    {
        var es = new EffectiveStructure { source = StructureResolver.ABSOLUTE, effectiveSeason = 1 };

        var now = DateTime.UtcNow.Date;
        var ordered = tvdb.episodes.Where(e => e.absolute > 0).OrderBy(e => e.absolute).ToList();
        es.seasonTotal = ordered.Count > 0 ? ordered.Max(e => e.absolute) : 0;

        foreach (var te in ordered)
        {
            if (!(te.air_date.HasValue && te.air_date.Value.Date <= now)) continue;

            var loc = await LocalizeByAbsoluteAsync(te.absolute, show, lang, ct);
            es.aired.Add(new TmdbEpisode
            {
                season = 1,
                episode = te.absolute,
                name = !string.IsNullOrEmpty(loc?.name) ? loc.name : (te.title ?? ""),
                overview = !string.IsNullOrEmpty(loc?.overview) ? loc.overview : (te.overview ?? ""),
                air_date = te.air_date,
                still_url = loc?.still_url
            });
        }
        es.seasonAired = es.aired.Count;
        return es;
    }

    static async Task<EffectiveStructure> BuildTmdbAsync(
        SubscriptionRow sub, TmdbShow show, TvdbShow tvdb, int latestSeason, string lang, CancellationToken ct)
    {
        var es = new EffectiveStructure
        {
            source = StructureResolver.TMDB,
            effectiveSeason = sub.target_season > 0 ? sub.target_season : latestSeason
        };

        var now = DateTime.UtcNow.Date;
        var seasonEps = await TmdbClient.GetSeasonAsync(show.id, es.effectiveSeason, lang, ct);
        es.aired = seasonEps
            .Where(e => e.air_date.HasValue && e.air_date.Value.Date <= now)
            .Select(e => { e.season = es.effectiveSeason; return e; })
            .ToList();

        if (es.effectiveSeason == latestSeason)
        {
            es.seasonTotal = show.current_season_total;
            es.seasonAired = show.current_season_aired;
        }
        else
        {
            var sInfo = show.seasons.FirstOrDefault(x => x.season_number == es.effectiveSeason);
            es.seasonTotal = sInfo?.episode_count ?? 0;
            es.seasonAired = es.aired.Count;
        }

        EnrichFromTvdb(es.aired, tvdb);
        return es;
    }

    static void EnrichFromTvdb(List<TmdbEpisode> eps, TvdbShow tvdb)
    {
        if (tvdb == null || tvdb.episodes.Count == 0) return;

        foreach (var ep in eps)
        {
            var te = tvdb.episodes.FirstOrDefault(x => x.season == ep.season && x.episode == ep.episode);
            if (te == null) continue;

            if (string.IsNullOrEmpty(ep.overview) && !string.IsNullOrEmpty(te.overview))
                ep.overview = te.overview;

            if ((string.IsNullOrEmpty(ep.name) || IsGenericName(ep.name)) && !string.IsNullOrEmpty(te.title))
                ep.name = te.title;

            if (string.IsNullOrEmpty(ep.still_url) && !string.IsNullOrEmpty(te.image)
                && te.image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                ep.still_url = te.image;
        }
    }

    static bool IsGenericName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(
            name.Trim(), @"^(?:Episode|Серія|Серия|Эпизод|Епізод)\s*\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    static async Task<EffectiveStructure> BuildTvdbAsync(
        SubscriptionRow sub, TmdbShow show, TvdbShow tvdb, string lang, CancellationToken ct)
    {
        var es = new EffectiveStructure { source = StructureResolver.TVDB };

        int latestTvdb = tvdb.LatestAiredSeason();
        int mapped = sub.target_season > 0 ? MapTmdbSeasonToTvdb(sub.target_season, show, tvdb) : 0;
        es.effectiveSeason = mapped > 0 ? mapped : latestTvdb;

        var now = DateTime.UtcNow.Date;
        var seasonEps = tvdb.episodes
            .Where(e => e.season == es.effectiveSeason)
            .OrderBy(e => e.episode)
            .ToList();
        es.seasonTotal = seasonEps.Count;

        foreach (var te in seasonEps)
        {
            if (!(te.air_date.HasValue && te.air_date.Value.Date <= now)) continue;

            TmdbEpisode loc = te.absolute > 0
                ? await LocalizeByAbsoluteAsync(te.absolute, show, lang, ct)
                : null;

            es.aired.Add(new TmdbEpisode
            {
                season = es.effectiveSeason,
                episode = te.episode,
                name = !string.IsNullOrEmpty(loc?.name) ? loc.name : (te.title ?? ""),
                overview = !string.IsNullOrEmpty(loc?.overview) ? loc.overview : (te.overview ?? ""),
                air_date = te.air_date,
                still_url = loc?.still_url
            });
        }
        es.seasonAired = es.aired.Count;
        return es;
    }

    static async Task<TmdbEpisode> LocalizeByAbsoluteAsync(int absolute, TmdbShow show, string lang, CancellationToken ct)
    {
        if (absolute <= 0 || show == null) return null;

        int prior = 0;
        foreach (var s in show.seasons.Where(x => x.season_number > 0).OrderBy(x => x.season_number))
        {
            int cnt = Math.Max(0, s.episode_count);
            if (cnt == 0) continue;

            if (absolute <= prior + cnt)
            {
                int tmdbEp = absolute - prior;
                var eps = await TmdbClient.GetSeasonAsync(show.id, s.season_number, lang, ct);
                return eps.FirstOrDefault(e => e.episode == tmdbEp);
            }
            prior += cnt;
        }
        return null;
    }

    static int MapTmdbSeasonToTvdb(int tmdbSeason, TmdbShow show, TvdbShow tvdb)
    {
        int prior = 0;
        foreach (var s in show.seasons.Where(x => x.season_number > 0).OrderBy(x => x.season_number))
        {
            if (s.season_number == tmdbSeason)
            {
                int firstAbs = prior + 1;
                var match = tvdb.episodes.FirstOrDefault(e => e.season > 0 && e.absolute == firstAbs);
                return match?.season ?? 0;
            }
            prior += Math.Max(0, s.episode_count);
        }
        return 0;
    }
}
