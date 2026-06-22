using System;
using System.Collections.Generic;
using System.Linq;

namespace EpWatch.Models;

public class TmdbEpisode
{
    public int season { get; set; }
    public int episode { get; set; }
    public string name { get; set; }
    public string overview { get; set; }
    public DateTime? air_date { get; set; }
    public string still_url { get; set; }
}

public class TmdbShow
{
    public int id { get; set; }
    public string name { get; set; }
    public string original_name { get; set; }
    public string original_language { get; set; }
    public string poster_path { get; set; }
    public string imdb_id { get; set; }
    public int first_air_year { get; set; }
    public int number_of_seasons { get; set; }
    public int latest_aired_season { get; set; }
    public int current_season_total { get; set; }
    public int current_season_aired { get; set; }
    public string status { get; set; }
    public DateTime? next_air_date { get; set; }
    public List<TmdbSeasonInfo> seasons { get; set; } = new();
}

public class TmdbSeasonInfo
{
    public int season_number { get; set; }
    public string name { get; set; }
    public int episode_count { get; set; }
    public DateTime? air_date { get; set; }
}

public class ShowParams
{
    public int tmdb_id { get; set; }
    public string title { get; set; }
    public string original_title { get; set; }
    public string original_language { get; set; }
    public string imdb_id { get; set; }
    public int year { get; set; }
}

public class AuthQs
{
    public string token { get; set; }
    public string account_email { get; set; }
    public string uid { get; set; }
    public string box_mac { get; set; }

    public bool IsEmpty
        => string.IsNullOrEmpty(token)
        && string.IsNullOrEmpty(account_email)
        && string.IsNullOrEmpty(uid)
        && string.IsNullOrEmpty(box_mac);
}

public class BalancerEntry
{
    public string name { get; set; }
    public string balanser { get; set; }
    public string url { get; set; }
}

public class BalancerVoice
{
    public string name { get; set; }
    public int t { get; set; }
    public string balancer { get; set; }
}

public class TvdbEpisode
{
    public int season { get; set; }
    public int episode { get; set; }
    public int absolute { get; set; }
    public string title { get; set; }
    public string overview { get; set; }
    public string image { get; set; }
    public DateTime? air_date { get; set; }
}

public class TvdbShow
{
    public int tvdb_id { get; set; }
    public int tmdb_id { get; set; }
    public string imdb_id { get; set; }
    public string status { get; set; }
    public List<TvdbEpisode> episodes { get; set; } = new();

    public List<int> SeasonNumbers()
        => episodes.Where(e => e.season > 0).Select(e => e.season).Distinct().OrderBy(x => x).ToList();

    public int SeasonCount()
        => SeasonNumbers().Count;

    public int EpisodeCount(int season)
        => episodes.Count(e => e.season == season);

    public int LatestAiredSeason()
    {
        var now = DateTime.UtcNow.Date;
        var aired = episodes
            .Where(e => e.season > 0 && e.air_date.HasValue && e.air_date.Value.Date <= now)
            .Select(e => e.season)
            .ToList();
        if (aired.Count > 0) return aired.Max();
        var all = SeasonNumbers();
        return all.Count > 0 ? all.Max() : 0;
    }
}
