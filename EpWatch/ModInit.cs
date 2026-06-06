using System;
using Microsoft.Extensions.DependencyInjection;
using EpWatch.Services;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;

namespace EpWatch;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static string modpath { get; private set; }
    public static ModuleConf conf { get; private set; } = new();

    public void Configure(ConfigureModel app)
    {
        SyncConf();

        app.services.AddDbContextFactory<SqlContext>(SqlContext.ConfiguringDbBuilder);
        app.services.AddHostedService<TelegramBotService>();
        app.services.AddHostedService<EpisodeChecker>();
    }

    public void Loaded(InitspaceModel init)
    {
        modpath = init.path;

        SyncConf();
        EventListener.UpdateInitFile += SyncConf;

        SqlContext.Initialization(init.app.ApplicationServices);

        if (conf.enable && string.IsNullOrWhiteSpace(conf.bot_token))
        {
            Console.WriteLine("\n\t[EpWatch] bot_token is not set; notifications are disabled.");
            Console.WriteLine("\t            Add a EpWatch section with bot_token to init.conf to enable.\n");
        }
        else if (conf.enable)
        {
            var keyTail = string.IsNullOrEmpty(conf.tmdb_api_key) ? "(empty)" : conf.tmdb_api_key.Substring(0, Math.Min(8, conf.tmdb_api_key.Length)) + "…";
            Console.WriteLine($"\n\t[EpWatch] loaded - check interval: {conf.check_interval_minutes} min, tmdb_api_key: {keyTail}, tmdb_lang: {conf.tmdb_lang}\n");
        }
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= SyncConf;
    }

    static void SyncConf()
    {
        conf = ModuleInvoke.Init("EpWatch", new ModuleConf()) ?? new ModuleConf();
    }
}
