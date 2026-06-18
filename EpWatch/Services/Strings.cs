using System;
using System.Collections.Generic;
using System.Linq;

namespace EpWatch.Services;

public static class Strings
{
    public const string DefaultLang = "uk";

    static readonly string[] Supported = { "uk", "en", "ru" };

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DefaultLang;
        var code = raw.Trim().ToLowerInvariant();
        if (code.Length >= 2)
        {
            var two = code.Substring(0, 2);
            if (Supported.Contains(two)) return two;
        }
        return DefaultLang;
    }

    static readonly Dictionary<string, Dictionary<string, string>> _ = new()
    {
        ["linked"] = new()
        {
            ["uk"] = "✅ <b>Акаунт прив'язано</b>\nВикористовуйте кнопки нижче.",
            ["en"] = "✅ <b>Account linked</b>\nUse the buttons below.",
            ["ru"] = "✅ <b>Аккаунт привязан</b>\nИспользуйте кнопки ниже."
        },
        ["welcome"] = new()
        {
            ["uk"] = "🎬 <b>EpWatch</b>\n\nВідкрийте картку серіалу в Lampa, натисніть 🔔 та оберіть озвучення. Я надішлю сповіщення, коли вийде нова серія.",
            ["en"] = "🎬 <b>EpWatch</b>\n\nOpen a TV show in Lampa, press 🔔 and pick a voice‑over. I will notify you when a new episode airs.",
            ["ru"] = "🎬 <b>EpWatch</b>\n\nОткройте карточку сериала в Lampa, нажмите 🔔 и выберите озвучку. Когда выйдет новая серия - я пришлю уведомление."
        },
        ["help"] = new()
        {
            ["uk"] = "📖 <b>Довідка</b>\n\n• Відкрийте картку серіалу → 🔔 → оберіть озвучення\n• «Будь-яке озвучення» - сповіщення за TMDB\n• Конкретне озвучення - сповіщення, коли серія з'явиться у цьому озвученні\n\n<b>Команди</b>\n<code>/list</code> - підписки\n<code>/check</code> - перевірити зараз\n<code>/lang</code> - мова\n<code>/unlink</code> - відв'язати",
            ["en"] = "📖 <b>Help</b>\n\n• Open a TV show → 🔔 → pick a voice‑over\n• «Any voice‑over» - TMDB notifications\n• Specific voice‑over - notify when the episode is out in that voice‑over\n\n<b>Commands</b>\n<code>/list</code> - subscriptions\n<code>/check</code> - check now\n<code>/lang</code> - language\n<code>/unlink</code> - unlink",
            ["ru"] = "📖 <b>Помощь</b>\n\n• Откройте карточку сериала → 🔔 → выберите озвучку\n• «Любая озвучка» - уведомление по TMDB\n• Конкретная озвучка - уведомление, когда серия выйдет в этой озвучке\n\n<b>Команды</b>\n<code>/list</code> - подписки\n<code>/check</code> - проверить сейчас\n<code>/lang</code> - язык\n<code>/unlink</code> - отвязать"
        },
        ["unlinked"] = new()
        {
            ["uk"] = "🔓 Акаунт і всі підписки видалено.",
            ["en"] = "🔓 Account and all subscriptions removed.",
            ["ru"] = "🔓 Аккаунт и все подписки удалены."
        },
        ["checking"] = new()
        {
            ["uk"] = "🔍 Перевіряю…",
            ["en"] = "🔍 Checking…",
            ["ru"] = "🔍 Проверяю…"
        },
        ["check_found"] = new()
        {
            ["uk"] = "✅ Знайдено нових: {0}",
            ["en"] = "✅ New episodes found: {0}",
            ["ru"] = "✅ Найдено новых: {0}"
        },
        ["check_summary_header"] = new()
        {
            ["uk"] = "✅ <b>Нові серії ({0})</b>\n\n",
            ["en"] = "✅ <b>New episodes ({0})</b>\n\n",
            ["ru"] = "✅ <b>Новые серии ({0})</b>\n\n"
        },
        ["check_none"] = new()
        {
            ["uk"] = "Нових серій немає.",
            ["en"] = "No new episodes.",
            ["ru"] = "Новых серий нет."
        },
        ["list_empty"] = new()
        {
            ["uk"] = "📋 Підписок немає.",
            ["en"] = "📋 No subscriptions.",
            ["ru"] = "📋 Подписок нет."
        },
        ["list_header"] = new()
        {
            ["uk"] = "📋 <b>Ваші підписки</b>\n\n",
            ["en"] = "📋 <b>Your subscriptions</b>\n\n",
            ["ru"] = "📋 <b>Ваши подписки</b>\n\n"
        },
        ["list_voice_any"] = new()
        {
            ["uk"] = "Будь-яка",
            ["en"] = "Any",
            ["ru"] = "Любая"
        },
        ["list_voice_note"] = new()
        {
            ["uk"] = "озв",
            ["en"] = "voice",
            ["ru"] = "озв"
        },
        ["btn_subs"] = new()   { ["uk"] = "📋 Підписки",   ["en"] = "📋 Subscriptions", ["ru"] = "📋 Подписки"  },
        ["btn_check"] = new()  { ["uk"] = "🔍 Перевірити", ["en"] = "🔍 Check",          ["ru"] = "🔍 Проверить" },
        ["btn_help"] = new()   { ["uk"] = "📖 Довідка",    ["en"] = "📖 Help",           ["ru"] = "📖 Помощь"    },
        ["btn_unlink"] = new() { ["uk"] = "🔓 Відв'язати", ["en"] = "🔓 Unlink",         ["ru"] = "🔓 Отвязать"  },

        ["cmd_list"] = new()   { ["uk"] = "Мої підписки",      ["en"] = "My subscriptions", ["ru"] = "Мои подписки"     },
        ["cmd_check"] = new()  { ["uk"] = "Перевірити зараз",  ["en"] = "Check now",         ["ru"] = "Проверить сейчас" },
        ["cmd_unlink"] = new() { ["uk"] = "Відв'язати акаунт", ["en"] = "Unlink account",   ["ru"] = "Отвязать аккаунт" },
        ["cmd_help"] = new()   { ["uk"] = "Довідка",           ["en"] = "Help",              ["ru"] = "Помощь"           },
        ["cmd_lang"] = new()   { ["uk"] = "Змінити мову",      ["en"] = "Change language",   ["ru"] = "Сменить язык"     },

        ["lang_pick"] = new()
        {
            ["uk"] = "Оберіть мову:",
            ["en"] = "Choose language:",
            ["ru"] = "Выберите язык:"
        },
        ["lang_set"] = new()
        {
            ["uk"] = "🌐 <b>Мову встановлено:</b> українська",
            ["en"] = "🌐 <b>Language set:</b> English",
            ["ru"] = "🌐 <b>Язык установлен:</b> русский"
        },
        ["cb_removed"] = new()
        {
            ["uk"] = "Видалено",
            ["en"] = "Removed",
            ["ru"] = "Удалено"
        },
        ["cb_notfound"] = new()
        {
            ["uk"] = "Не знайдено",
            ["en"] = "Not found",
            ["ru"] = "Не найдено"
        },
        ["sub_added"] = new()
        {
            ["uk"] = "🔔 <b>Нова підписка</b>\n\n🎬 <b>{0}</b>\n🎙 {1}",
            ["en"] = "🔔 <b>New subscription</b>\n\n🎬 <b>{0}</b>\n🎙 {1}",
            ["ru"] = "🔔 <b>Новая подписка</b>\n\n🎬 <b>{0}</b>\n🎙 {1}"
        },
        ["sub_removed"] = new()
        {
            ["uk"] = "❌ <b>Підписку скасовано</b>\n\n🎬 <b>{0}</b>\n🎙 {1}",
            ["en"] = "❌ <b>Subscription removed</b>\n\n🎬 <b>{0}</b>\n🎙 {1}",
            ["ru"] = "❌ <b>Подписка отменена</b>\n\n🎬 <b>{0}</b>\n🎙 {1}"
        },
        ["voice_any"] = new()
        {
            ["uk"] = "будь-яке озвучення",
            ["en"] = "any voice‑over",
            ["ru"] = "любая озвучка"
        },
        ["show_ended"] = new()
        {
            ["uk"] = "завершено",
            ["en"] = "ended",
            ["ru"] = "завершён"
        },
        ["show_canceled"] = new()
        {
            ["uk"] = "скасовано",
            ["en"] = "canceled",
            ["ru"] = "отменён"
        },
        ["show_ongoing"] = new()
        {
            ["uk"] = "виходить",
            ["en"] = "airing",
            ["ru"] = "выходит"
        },
        ["status_header"] = new()
        {
            ["uk"] = "📊 <b>Статус підписок</b>\n\n",
            ["en"] = "📊 <b>Subscriptions status</b>\n\n",
            ["ru"] = "📊 <b>Статус подписок</b>\n\n"
        },
        ["new_header"] = new()
        {
            ["uk"] = "✨ <b>Нові серії ({0})</b>\n\n",
            ["en"] = "✨ <b>New episodes ({0})</b>\n\n",
            ["ru"] = "✨ <b>Новые серии ({0})</b>\n\n"
        },
        ["next_air"] = new()
        {
            ["uk"] = "наст.",
            ["en"] = "next",
            ["ru"] = "след."
        },
        ["season_label"] = new()
        {
            ["uk"] = "сезон",
            ["en"] = "season",
            ["ru"] = "сезон"
        },
        ["awaiting_release"] = new()
        {
            ["uk"] = "очікується",
            ["en"] = "awaiting",
            ["ru"] = "ожидается"
        },
        ["all_aired"] = new()
        {
            ["uk"] = "усі серії вийшли ✅",
            ["en"] = "all episodes out ✅",
            ["ru"] = "все серии вышли ✅"
        },
        ["season_complete_waiting"] = new()
        {
            ["uk"] = "сезон завершено, очікуємо новий",
            ["en"] = "season complete, awaiting next",
            ["ru"] = "сезон завершён, ждём новый"
        },
        ["voice_waiting"] = new()
        {
            ["uk"] = "очікуємо нові серії в озвученні",
            ["en"] = "awaiting new episodes in this voice‑over",
            ["ru"] = "ждём новые серии в озвучке"
        },
        ["tmdb_aired_note"] = new()
        {
            ["uk"] = "TMDB",
            ["en"] = "TMDB",
            ["ru"] = "TMDB"
        },
        ["already_aired_body"] = new()
        {
            ["uk"] = "✅ <b>Сезон уже повністю вийшов</b>\n\n🎬 <b>{0}</b>\n📺 S{1}: {2}/{3} серій\n\nПереключити підписку на «авто» - сповіщати про нові сезони та серії?",
            ["en"] = "✅ <b>This season has fully aired</b>\n\n🎬 <b>{0}</b>\n📺 S{1}: {2}/{3} episodes\n\nSwitch this subscription to «auto» - notify about new seasons/episodes?",
            ["ru"] = "✅ <b>Сезон уже полностью вышел</b>\n\n🎬 <b>{0}</b>\n📺 S{1}: {2}/{3} серий\n\nПереключить подписку в режим «авто» - уведомлять о новых сезонах/сериях?"
        },
        ["btn_switch_auto"] = new()
        {
            ["uk"] = "🔄 Перейти на авто",
            ["en"] = "🔄 Switch to auto",
            ["ru"] = "🔄 Переключить на авто"
        },
        ["btn_keep"] = new()
        {
            ["uk"] = "❌ Залишити",
            ["en"] = "❌ Keep as is",
            ["ru"] = "❌ Оставить"
        },
        ["switched_to_auto"] = new()
        {
            ["uk"] = "✅ <b>Перейшли на авто-режим</b>\n\n🎬 <b>{0}</b>",
            ["en"] = "✅ <b>Switched to auto</b>\n\n🎬 <b>{0}</b>",
            ["ru"] = "✅ <b>Переключено в авто-режим</b>\n\n🎬 <b>{0}</b>"
        },
        ["movie_available_header"] = new()
        {
            ["uk"] = "🎬 <b>Фільм доступний</b>",
            ["en"] = "🎬 <b>Movie available</b>",
            ["ru"] = "🎬 <b>Фильм доступен</b>"
        },
        ["movie_btn_wait"] = new()
        {
            ["uk"] = "⏳ Чекати інші озвучення",
            ["en"] = "⏳ Wait for other voices",
            ["ru"] = "⏳ Ждать другие озвучки"
        },
        ["movie_btn_stop"] = new()
        {
            ["uk"] = "❌ Відписатися",
            ["en"] = "❌ Unsubscribe",
            ["ru"] = "❌ Отписаться"
        },
        ["movie_sub_added"] = new()
        {
            ["uk"] = "🎬 <b>Підписка на фільм</b>\n\n<b>{0}</b>\n🌐 {1}\n\nСповіщу про нові озвучення на цьому балансирі.",
            ["en"] = "🎬 <b>Movie subscription</b>\n\n<b>{0}</b>\n🌐 {1}\n\nI'll notify you about new voices on this balancer.",
            ["ru"] = "🎬 <b>Подписка на фильм</b>\n\n<b>{0}</b>\n🌐 {1}\n\nСообщу о новых озвучках на этом балансере."
        },
        ["movie_unsub"] = new()
        {
            ["uk"] = "❌ <b>Підписку на фільм скасовано</b>\n\n<b>{0}</b>",
            ["en"] = "❌ <b>Movie subscription removed</b>\n\n<b>{0}</b>",
            ["ru"] = "❌ <b>Подписка на фильм отменена</b>\n\n<b>{0}</b>"
        },
        ["movie_any_balancer"] = new()
        {
            ["uk"] = "будь-який балансир",
            ["en"] = "any balancer",
            ["ru"] = "любой балансер"
        },
        ["movie_list_available"] = new()
        {
            ["uk"] = "доступний",
            ["en"] = "available",
            ["ru"] = "доступен"
        },
        ["movie_list_waiting"] = new()
        {
            ["uk"] = "очікується",
            ["en"] = "awaiting",
            ["ru"] = "ожидается"
        },
        ["movie_waiting_new"] = new()
        {
            ["uk"] = "чекаємо нові озвучення",
            ["en"] = "waiting for new voices",
            ["ru"] = "ждём новые озвучки"
        },
        ["cb_waiting"] = new()
        {
            ["uk"] = "Чекаємо далі",
            ["en"] = "Waiting",
            ["ru"] = "Ждём дальше"
        },
        ["open_button"] = new()
        {
            ["uk"] = "▶️ Дивитись у Lampa",
            ["en"] = "▶️ Watch in Lampa",
            ["ru"] = "▶️ Смотреть в Lampa"
        }
    };

    public static string ShowStatusLabel(string lang, string status)
    {
        if (string.IsNullOrEmpty(status)) return "";
        if (string.Equals(status, "Ended", StringComparison.OrdinalIgnoreCase))     return T(lang, "show_ended");
        if (string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))  return T(lang, "show_canceled");
        if (string.Equals(status, "Returning Series", StringComparison.OrdinalIgnoreCase)) return T(lang, "show_ongoing");
        return status;
    }

    public static string T(string lang, string key)
    {
        var l = Normalize(lang);
        if (_.TryGetValue(key, out var dict))
        {
            if (dict.TryGetValue(l, out var v)) return v;
            if (dict.TryGetValue(DefaultLang, out var d)) return d;
        }
        return key;
    }

    public static string T(string lang, string key, params object[] args)
        => string.Format(T(lang, key), args);
}
