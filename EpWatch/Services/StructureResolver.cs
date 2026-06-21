using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EpWatch.Models;

namespace EpWatch.Services;

public static class StructureResolver
{
    public const string TMDB = "tmdb";
    public const string TVDB = "tvdb";
    public const string ABSOLUTE = "absolute";

    public static async Task<string> ResolveAsync(
        TmdbShow tmdb, TvdbShow tvdb, BalancerEntry balancer, ShowParams sp, AuthQs auth, CancellationToken ct)
    {
        if (tmdb == null || tvdb == null || balancer == null)
            return TMDB;

        int tmdbSeasons = tmdb.seasons.Count(s => s.season_number > 0);
        int tvdbSeasons = tvdb.SeasonCount();

        if (tmdbSeasons == tvdbSeasons && SameSeasonSizes(tmdb, tvdb))
            return TMDB;

        var balSeasons = await BalancerProbe.DiscoverSeasonsAsync(balancer, sp, auth, ct);
        if (balSeasons.Count == 0)
            return TMDB;
        int balCount = balSeasons.Count;

        int scoreT = 0, scoreV = 0;
        if (tmdbSeasons == balCount) scoreT += 10;
        if (tvdbSeasons == balCount) scoreV += 10;

        var balProbeCache = new Dictionary<int, int>();
        async Task<int> BalSeasonCount(int season)
        {
            if (balProbeCache.TryGetValue(season, out var c)) return c;
            int cnt = (await BalancerProbe.ProbeAsync(balancer, sp, season, null, auth, ct)).maxEpisode;
            balProbeCache[season] = cnt;
            return cnt;
        }

        var tAnchor = LatestFullyAiredTmdb(tmdb);
        if (tAnchor.season > 0 && balSeasons.Contains(tAnchor.season))
        {
            int bc = await BalSeasonCount(tAnchor.season);
            if (bc > 0 && bc == tAnchor.count) scoreT += 5;
        }

        var vAnchor = LatestFullyAiredTvdb(tvdb);
        if (vAnchor.season > 0 && balSeasons.Contains(vAnchor.season))
        {
            int bc = await BalSeasonCount(vAnchor.season);
            if (bc > 0 && bc == vAnchor.count) scoreV += 5;
        }

        string src;
        if (scoreT == 0 && scoreV == 0 && balCount == 1 && Math.Max(tmdbSeasons, tvdbSeasons) > 1)
            src = ABSOLUTE;
        else
            src = scoreV > scoreT ? TVDB : TMDB;

        Log.Dbg($"[EpWatch] structure resolve {balancer.balanser}: tmdbSeasons={tmdbSeasons} tvdbSeasons={tvdbSeasons} bal={balCount} scoreT={scoreT} scoreV={scoreV} -> {src}");
        return src;
    }

    static bool SameSeasonSizes(TmdbShow tmdb, TvdbShow tvdb)
    {
        foreach (var s in tmdb.seasons.Where(x => x.season_number > 0))
        {
            if (tvdb.EpisodeCount(s.season_number) != s.episode_count)
                return false;
        }
        return true;
    }

    static (int season, int count) LatestFullyAiredTmdb(TmdbShow tmdb)
    {
        foreach (var s in tmdb.seasons.Where(x => x.season_number > 0 && x.episode_count > 0)
                                      .OrderByDescending(x => x.season_number))
        {
            if (s.season_number < tmdb.latest_aired_season)
                return (s.season_number, s.episode_count);

            if (s.season_number == tmdb.latest_aired_season
                && tmdb.current_season_total > 0
                && tmdb.current_season_aired >= tmdb.current_season_total)
                return (s.season_number, s.episode_count);
        }
        return (0, 0);
    }

    static (int season, int count) LatestFullyAiredTvdb(TvdbShow tvdb)
    {
        var now = DateTime.UtcNow.Date;
        foreach (var season in tvdb.SeasonNumbers().OrderByDescending(x => x))
        {
            var eps = tvdb.episodes.Where(e => e.season == season).ToList();
            if (eps.Count == 0) continue;
            if (eps.All(e => e.air_date.HasValue && e.air_date.Value.Date <= now))
                return (season, eps.Count);
        }
        return (0, 0);
    }
}
