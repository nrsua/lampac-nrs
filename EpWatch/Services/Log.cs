using System;
using System.Text.RegularExpressions;

namespace EpWatch;

public static class Log
{
    public static bool DebugEnabled => ModInit.conf?.debug ?? false;

    public static void Info(string msg) => Console.WriteLine(msg);

    public static void Warn(string msg) => Console.WriteLine(msg);

    public static void Dbg(string msg)
    {
        if (DebugEnabled) Console.WriteLine(msg);
    }

    static readonly Regex _secrets = new Regex(
        @"((?:token|account_email|uid|account|box_mac)=)[^&\s""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Url(string url)
        => string.IsNullOrEmpty(url) ? url : _secrets.Replace(url, "$1***");
}
