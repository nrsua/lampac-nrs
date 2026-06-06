using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EpWatch;

public class SqlContext : DbContext
{
    public static IDbContextFactory<SqlContext> Factory { get; private set; }

    public static SqlContext Create()
        => Factory != null ? Factory.CreateDbContext() : new SqlContext();

    public static void Initialization(IServiceProvider services)
    {
        Directory.CreateDirectory("database");

        Factory = services.GetService<IDbContextFactory<SqlContext>>();

        using var db = new SqlContext();
        db.Database.EnsureCreated();
        EnsureColumns(db);
    }

    static void EnsureColumns(SqlContext db)
    {
        var subsAdds = new Dictionary<string, string>
        {
            ["season_total"]  = "INTEGER NOT NULL DEFAULT 0",
            ["season_aired"]  = "INTEGER NOT NULL DEFAULT 0",
            ["target_season"] = "INTEGER NOT NULL DEFAULT 0",
            ["show_status"]   = "TEXT NOT NULL DEFAULT ''",
            ["next_air_date"] = "TEXT NULL",
            ["next_check_at"] = "TEXT NULL",
            ["last_checked_at"] = "TEXT NULL",
            ["last_voice_episode"] = "INTEGER NOT NULL DEFAULT 0",
            ["balancer"]      = "TEXT NOT NULL DEFAULT ''",
            ["poster_path"]   = "TEXT NULL"
        };
        var userAdds = new Dictionary<string, string>
        {
            ["lang"] = "TEXT NOT NULL DEFAULT 'uk'"
        };

        ApplyAdds(db, "subscriptions", subsAdds);
        ApplyAdds(db, "users", userAdds);
    }

    static void ApplyAdds(SqlContext db, string table, Dictionary<string, string> cols)
    {
        HashSet<string> existing;
        try
        {
            existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                existing.Add(rd.GetString(1));
        }
        catch { return; }

        foreach (var (name, decl) in cols)
        {
            if (existing.Contains(name)) continue;
            try
            {
#pragma warning disable EF1002
                db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN {name} {decl}");
#pragma warning restore EF1002
            }
            catch (Exception ex) { Console.WriteLine($"[EpWatch] migrate {table}.{name}: {ex.Message}"); }
        }
    }

    static readonly string _connection = new SqliteConnectionStringBuilder
    {
        DataSource = "database/EpWatch.sql",
        Cache = SqliteCacheMode.Shared,
        DefaultTimeout = 10,
        Pooling = true
    }.ToString();

    public static void ConfiguringDbBuilder(DbContextOptionsBuilder o)
    {
        if (!o.IsConfigured)
        {
            o.UseSqlite(_connection);
            o.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder o) => ConfiguringDbBuilder(o);

    public DbSet<TgUserRow> users { get; set; }
    public DbSet<SubscriptionRow> subs { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TgUserRow>().HasIndex(x => x.lampac_uid).IsUnique();
        b.Entity<TgUserRow>().HasIndex(x => x.chat_id).IsUnique();
        b.Entity<SubscriptionRow>().HasIndex(x => new { x.chat_id, x.tmdb_id, x.voice });
        b.Entity<SubscriptionRow>().HasIndex(x => x.tmdb_id);
    }
}

[Table("users")]
public class TgUserRow
{
    [Key] public long Id { get; set; }
    [Required] public long chat_id { get; set; }
    [Required] public string lampac_uid { get; set; }
    public string username { get; set; }
    public string lang { get; set; } = "uk";
    public DateTime linked_at { get; set; }
}

[Table("subscriptions")]
public class SubscriptionRow
{
    [Key] public long Id { get; set; }

    [Required] public long chat_id { get; set; }
    [Required] public int tmdb_id { get; set; }
    [Required] public string title { get; set; }

    public string voice { get; set; } = "";

    public string balancer { get; set; } = "";

    public int last_season { get; set; }
    public int last_episode { get; set; }

    public int last_voice_episode { get; set; }

    public int season_total { get; set; }
    public int season_aired { get; set; }

    public int target_season { get; set; }

    public string show_status { get; set; } = "";

    public DateTime? next_air_date { get; set; }

    public DateTime subscribed_at { get; set; }
    public DateTime? next_check_at { get; set; }
    public DateTime? last_checked_at { get; set; }

    public string poster_path { get; set; }
}
