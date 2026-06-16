using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using EpWatch.Services;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;

namespace EpWatch;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static string modpath { get; private set; }
    public static ModuleConf conf { get; private set; } = new();

    static Delegate _mwHandler;
    static FieldInfo _mwField;

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
        HookMiddleware();

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
        UnhookMiddleware();
    }

    static void HookMiddleware()
    {
        try
        {
            var field = typeof(EventListener).GetField("Middleware", BindingFlags.Public | BindingFlags.Static);
            if (field == null) return;

            var delType = field.FieldType;
            var args = delType.IsGenericType ? delType.GetGenericArguments() : Array.Empty<Type>();
            var retType = args.Length > 0 ? args[args.Length - 1] : null;

            var methodName = retType == typeof(bool) ? nameof(OnMiddlewareSync) : nameof(OnMiddlewareAsync);
            var method = typeof(ModInit).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

            var handler = Delegate.CreateDelegate(delType, method);
            var current = (Delegate)field.GetValue(null);
            field.SetValue(null, Delegate.Combine(current, handler));

            _mwHandler = handler;
            _mwField = field;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EpWatch] middleware hook skipped: {ex.Message}");
        }
    }

    static void UnhookMiddleware()
    {
        try
        {
            if (_mwField == null || _mwHandler == null) return;
            var current = (Delegate)_mwField.GetValue(null);
            _mwField.SetValue(null, Delegate.Remove(current, _mwHandler));
            _mwHandler = null;
        }
        catch { }
    }

    static bool OnMiddlewareSync(bool first, EventMiddleware e)
    {
        ApplyMiddleware(first, e);
        return true;
    }

    static Task<bool> OnMiddlewareAsync(bool first, EventMiddleware e)
    {
        ApplyMiddleware(first, e);
        return Task.FromResult(true);
    }

    static void ApplyMiddleware(bool first, EventMiddleware e)
    {
        if (!first || string.IsNullOrWhiteSpace(conf.balancer_uid))
            return;

        var path = e.httpContext.Request.Path.Value;
        if (path != null &&
            (path.StartsWith("/epwatch/", StringComparison.OrdinalIgnoreCase) ||
             path.Equals("/epwatch.js", StringComparison.OrdinalIgnoreCase)))
        {
            var requestInfo = e.httpContext.Features.Get<RequestModel>();
            if (requestInfo != null)
                requestInfo.IsAnonymousRequest = true;
        }
    }

    static void SyncConf()
    {
        conf = ModuleInvoke.Init("EpWatch", new ModuleConf()) ?? new ModuleConf();
    }
}
