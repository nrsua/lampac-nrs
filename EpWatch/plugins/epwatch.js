(function () {
    'use strict';

    if (window.epwatch_ready) return;
    window.epwatch_ready = true;

    var HOST = '{localhost}';
    var TOKEN_PLACEHOLDER = '{token}';

    var META = {
        name:    'EpWatch',
        version: '0.2.4',
        author:  'nrsua'
    };

    var DICT = {
        epwatch_menu:            { uk: 'EpWatch',                               en: 'EpWatch',                             ru: 'EpWatch' },
        epwatch_unavailable:     { uk: 'EpWatch недоступний',                   en: 'EpWatch is unavailable',              ru: 'EpWatch недоступен' },
        epwatch_link_title:      { uk: 'Прив’яжіть Telegram',                   en: 'Link Telegram',                       ru: 'Привяжите Telegram' },
        epwatch_link_open_bot:   { uk: 'Відкрити бота',                         en: 'Open the bot',                        ru: 'Открыть бота' },
        epwatch_link_show:       { uk: 'Показати посилання',                    en: 'Show the link',                       ru: 'Показать ссылку' },
        epwatch_bot_offline:     { uk: 'Бот не запущено',                       en: 'The bot is offline',                  ru: 'Бот не запущен' },
        epwatch_title:           { uk: 'TG-сповіщення',                         en: 'TG notifications',                    ru: 'TG-уведомления' },
        epwatch_voice_any:       { uk: '🔔 Будь-яке озвучення',                 en: '🔔 Any voice‑over',                  ru: '🔔 Любая озвучка' },
        epwatch_voice_any_sub:   { uk: 'Коли вийде серія (за TMDB)',            en: 'When the episode airs (TMDB)',        ru: 'При выходе серии (по TMDB)' },
        epwatch_voice_list:      { uk: 'Озвучення на балансирах:',              en: 'Voice‑overs on balancers:',           ru: 'Озвучки на балансирах:' },
        epwatch_subscribe_only:  { uk: '🔔 Підписатися (без озвучення)',        en: '🔔 Subscribe (no voice‑over)',       ru: '🔔 Подписаться (без озвучки)' },
        epwatch_unsub:           { uk: '❌ Відписатися',                        en: '❌ Unsubscribe',                     ru: '❌ Отписаться' },
        epwatch_unsub_prefix:    { uk: '❌ Відписатися: ',                      en: '❌ Unsubscribe: ',                   ru: '❌ Отписаться: ' },
        epwatch_subscribed:      { uk: '🔔 Підписку оформлено',                 en: '🔔 Subscribed',                      ru: '🔔 Подписка оформлена' },
        epwatch_subscribed_v:    { uk: '🔔 Підписка: ',                         en: '🔔 Subscribed: ',                    ru: '🔔 Подписка: ' },
        epwatch_unsubscribed:    { uk: 'Підписку скасовано',                    en: 'Subscription removed',                ru: 'Подписка отменена' },
        epwatch_err_network:     { uk: 'Мережева помилка',                      en: 'Network error',                       ru: 'Сетевая ошибка' },
        epwatch_err_unknown:     { uk: 'Помилка',                               en: 'Error',                               ru: 'Ошибка' },
        epwatch_voice_any_word:  { uk: 'Будь-яка',                              en: 'Any',                                 ru: 'Любая' },

        epwatch_settings_about:        { uk: 'Про додаток',                     en: 'About',                               ru: 'О плагине' },
        epwatch_settings_author:       { uk: 'Автор',                           en: 'Author',                              ru: 'Автор' },
        epwatch_settings_version:      { uk: 'Версія',                          en: 'Version',                             ru: 'Версия' },
        epwatch_settings_lang:         { uk: 'Мова інтерфейсу',                 en: 'Interface language',                  ru: 'Язык интерфейса' },
        epwatch_settings_lang_desc:    { uk: 'Мова повідомлень плагіна. «Авто» - як у Lampa.', en: 'Plugin UI language. "Auto" follows Lampa.', ru: 'Язык сообщений плагина. «Авто» - как в Lampa.' },
        epwatch_settings_open:         { uk: 'Відкрити сторінку підписок',      en: 'Open subscriptions page',             ru: 'Открыть страницу подписок' },
        epwatch_settings_link:         { uk: 'Прив’язати Telegram',             en: 'Link Telegram',                       ru: 'Привязать Telegram' },
        epwatch_settings_lang_auto:    { uk: 'Авто',                            en: 'Auto',                                ru: 'Авто' },
        epwatch_link_qr_hint:          { uk: 'Скануйте QR або відкрийте бота',  en: 'Scan the QR or open the bot',         ru: 'Сканируйте QR или откройте бота' },
        epwatch_link_manual:           { uk: 'Вже прив’язували цього бота чи писали йому повідомлення? Натисніть «Переприв’язати».', en: 'Already linked this bot or messaged it before? Tap "Re-link".', ru: 'Уже привязывали этого бота или писали ему? Нажмите «Перепривязать».' },
        epwatch_link_scan:             { uk: 'Відскануйте QR або напишіть повідомлення боту:', en: 'Scan the QR or send a message to the bot:', ru: 'Отсканируйте QR или напишите сообщение боту:' },
        epwatch_link_open_text:        { uk: 'Переприв’язати', en: 'Re-link', ru: 'Перепривязать' },
        epwatch_linked:                { uk: 'Telegram-бот прив’язаний',        en: 'Telegram bot linked',                 ru: 'Telegram-бот привязан' },
        epwatch_close:                 { uk: 'Закрити',                         en: 'Close',                               ru: 'Закрыть' },
        epwatch_settings_unlink:       { uk: 'Відв’язати Telegram',             en: 'Unlink Telegram',                     ru: 'Отвязать Telegram' },
        epwatch_settings_uid:          { uk: 'Перевизначення Sync UID',         en: 'Sync UID override',                   ru: 'Переопределение Sync UID' },
        epwatch_settings_uid_desc:     { uk: 'За замовчуванням EpWatch використовує локальний UID. Щоб використати інший - змініть на свій. Однаковий на різних пристроях - спільні підписки.', en: 'By default EpWatch uses the local UID. To use another one - change it to your own. The same UID on different devices - shared subscriptions.', ru: 'По умолчанию EpWatch использует локальный UID. Чтобы использовать другой - измените на свой. Одинаковый на разных устройствах - общие подписки.' },
        epwatch_unlink_confirm:        { uk: 'Видалити прив’язку та всі підписки?', en: 'Remove the link and all subscriptions?', ru: 'Удалить привязку и все подписки?' },
        epwatch_unlink_done:           { uk: 'Прив’язку видалено',              en: 'Link removed',                        ru: 'Привязка удалена' },
        epwatch_yes:                   { uk: 'Так',                             en: 'Yes',                                 ru: 'Да' },
        epwatch_no:                    { uk: 'Скасувати',                       en: 'Cancel',                              ru: 'Отмена' },
        epwatch_pick_season:           { uk: 'Оберіть сезон',                   en: 'Pick a season',                       ru: 'Выберите сезон' },
        epwatch_season_auto:           { uk: '🆕 Автоматично (новий сезон)',    en: '🆕 Auto (newest season)',            ru: '🆕 Автоматически (новый сезон)' },
        epwatch_season_auto_sub:       { uk: 'Слідкувати завжди за новим',      en: 'Always follow the latest one',        ru: 'Следить всегда за новейшим' },
        epwatch_season_upcoming:       { uk: 'планується',                      en: 'upcoming',                            ru: 'планируется' },
        epwatch_season_airing:         { uk: 'виходить',                        en: 'airing',                              ru: 'выходит' },
        epwatch_season_aired:          { uk: 'завершено',                       en: 'aired',                               ru: 'вышел' },
        epwatch_season_eps:            { uk: 'серій',                           en: 'episodes',                            ru: 'серий' },
        epwatch_tmdb_short:            { uk: 'TMDB',                            en: 'TMDB',                                ru: 'TMDB' },
        epwatch_movie_pick:            { uk: 'Оберіть балансир',                en: 'Pick a balancer',                     ru: 'Выберите балансер' },
        epwatch_movie_no_balancers:    { uk: 'Балансири недоступні',            en: 'No balancers available',              ru: 'Балансиры недоступны' },
        epwatch_movie_subscribed:      { uk: '🎬 Підписку на фільм оформлено',  en: '🎬 Subscribed to movie',             ru: '🎬 Подписка на фильм оформлена' },
        epwatch_movie_any_balancer:    { uk: 'будь-який балансир',              en: 'any balancer',                        ru: 'любой балансер' },
        epwatch_movie_any:             { uk: '🔔 Будь-який балансир',           en: '🔔 Any balancer',                    ru: '🔔 Любой балансер' },
        epwatch_movie_any_sub:         { uk: 'Коли фільм з’явиться на будь-якому балансирі', en: 'When the movie appears on any balancer', ru: 'Когда фильм появится на любом балансере' },
        epwatch_movie_list:            { uk: 'Доступно на балансирах:',         en: 'Available on balancers:',             ru: 'Доступно на балансерах:' }
    };

    function token() {
        if (TOKEN_PLACEHOLDER && TOKEN_PLACEHOLDER.indexOf('{') === -1 && TOKEN_PLACEHOLDER.length > 0)
            return TOKEN_PLACEHOLDER;
        try {
            return Lampa.Storage.get('token', '') ||
                   Lampa.Storage.get('lampac_unic_id', '') ||
                   Lampa.Storage.get('account_email', '') || '';
        } catch (e) { return ''; }
    }

    function customUid() {
        try { return (('' + (Lampa.Storage.get('epwatch_uid', '') || '')).trim()); }
        catch (e) { return ''; }
    }

    function localUid() {
        try {
            var u = Lampa.Storage.get('token', '') ||
                    Lampa.Storage.get('account_email', '') ||
                    Lampa.Storage.get('lampac_unic_id', '');
            return ('' + (u || token() || '')).trim();
        } catch (e) { return ''; }
    }

    function authQs() {
        var cu = customUid();
        if (cu) {
            var e = encodeURIComponent(cu);
            return '&token=' + e + '&account_email=' + e + '&uid=' + e;
        }
        var qs = '';
        try {
            var t = Lampa.Storage.get('token', '');
            var ae = Lampa.Storage.get('account_email', '');
            var u = Lampa.Storage.get('lampac_unic_id', '');
            if (t)  qs += '&token=' + encodeURIComponent(t);
            if (ae) qs += '&account_email=' + encodeURIComponent(ae);
            if (u)  qs += '&uid=' + encodeURIComponent(u);
        } catch (e) {}
        if (!qs) {
            var fb = token();
            if (fb) qs = '&token=' + encodeURIComponent(fb);
        }
        return qs;
    }

    function api(path) {
        var sep = path.indexOf('?') >= 0 ? '&' : '?';
        var a = authQs();
        return HOST + path + (a ? sep + a.substring(1) : '');
    }

    function get(url, ok, err, timeout) {
        var n = new Lampa.Reguest();
        n.timeout(timeout || 15000);
        n['native'](url, ok, err || function () { });
    }

    function post(url, body, ok, err, timeout) {
        var n = new Lampa.Reguest();
        n.timeout(timeout || 15000);
        n['native'](url, ok, err || function () { },
            JSON.stringify(body || {}),
            { dataType: 'json', contentType: 'application/json' });
    }

    function registerLang() {
        Lampa.Lang.add(DICT);
    }

    function resolveLang() {
        var pick = '';
        try { pick = Lampa.Storage.get('epwatch_lang', 'auto'); } catch (e) {}
        if (pick && pick !== 'auto') return pick;
        try {
            var l = (Lampa.Storage.get('language', '') || '').toLowerCase();
            if (l.indexOf('uk') === 0) return 'uk';
            if (l.indexOf('en') === 0) return 'en';
            if (l.indexOf('ru') === 0) return 'ru';
        } catch (e) {}
        return 'uk';
    }

    function L(key) {
        var l = resolveLang();
        var d = DICT[key];
        if (d && d[l]) return d[l];
        try { return Lampa.Lang.translate(key); } catch (e) { return key; }
    }

    var MENU_ACTION = 'epwatch_list';
    var ICON_LOGO = '<svg width="64" height="64" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">' +
        '<rect x="2" y="4" width="20" height="14" rx="2.5" stroke="currentColor" stroke-width="1.8"/>' +
        '<path d="M9 21h6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>' +
        '<path d="M12 18v3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>' +
        '<circle cx="8" cy="11" r="1.3" fill="currentColor"/>' +
        '<circle cx="12" cy="11" r="1.3" fill="currentColor"/>' +
        '<circle cx="16" cy="11" r="1.3" stroke="currentColor" stroke-width="1.5"/>' +
        '<circle cx="12" cy="4" r="2.5" fill="currentColor"/>' +
    '</svg>';

    function tmdbImg(path) {
        if (!path) return '';
        try { return Lampa.TMDB.image('t/p/w300' + path); } catch (e) {}
        return 'https://image.tmdb.org/t/p/w300' + path;
    }

    function cardFallback(sub) {
        var card = {
            id:          sub.tmdb_id,
            tmdb_id:     sub.tmdb_id,
            poster_path: sub.poster_path || '',
            source:      'tmdb',
            epwatch:     sub
        };
        if (sub.media_type === 'movie') {
            card.title          = sub.title;
            card.original_title = sub.title;
        } else {
            card.name              = sub.title;
            card.original_name     = sub.title;
            card.number_of_seasons = sub.last_season || 1;
        }
        return card;
    }

    function buildCards(self, subs) {
        var results = new Array(subs.length);
        var pending = subs.length;
        var built = false;
        var lang = '';
        try { lang = Lampa.Storage.get('tmdb_lang', ''); } catch (e) {}

        function finish() {
            if (built) return;
            built = true;
            clearTimeout(deadline);
            for (var k = 0; k < subs.length; k++)
                if (!results[k]) results[k] = cardFallback(subs[k]);
            self.build({ results: results, total_pages: 1, card_category: true });
            self.render().find('.category-full').addClass('mapping--grid').addClass('cols--6');
        }

        function done() { if (--pending <= 0) finish(); }

        var deadline = setTimeout(finish, 4000);

        subs.forEach(function (sub, i) {
            var path = (sub.media_type === 'movie' ? 'movie/' : 'tv/') + sub.tmdb_id +
                       '?api_key=' + Lampa.TMDB.key() +
                       (lang ? '&language=' + encodeURIComponent(lang) : '');
            var rq = new Lampa.Reguest();
            rq.timeout(6000);
            rq.silent(Lampa.TMDB.api(path), function (data) {
                if (data && data.id) {
                    data.source = 'tmdb';
                    if (!data.poster_path && sub.poster_path) data.poster_path = sub.poster_path;
                    data.epwatch = sub;
                    results[i] = data;
                } else {
                    results[i] = cardFallback(sub);
                }
                done();
            }, function () {
                results[i] = cardFallback(sub);
                done();
            });
        });
    }

    function SubsComponent(object) {
        var comp = new Lampa.InteractionCategory(object);
        var anyWord = L('epwatch_voice_any_word');

        comp.create = function () {
            var self = this;
            self.activity.loader(true);

            get(api('/epwatch/subscriptions'), function (r) {
                if (!r || !r.linked) return promptLink(self);
                if (!r.results || !r.results.length) return self.empty();
                buildCards(self, r.results);
            }, function () { self.empty(); });
        };

        comp.cardRender = function (object, element, card) {
            var sub = element.epwatch;
            if (!sub) return;

            var isMovie = sub.media_type === 'movie';
            var position, voiceLine, tmdbHint = '';

            if (isMovie) {
                position = '🎬 ' + (sub.seen_voices ? '✅' : '⏳');
                voiceLine = sub.balancer_name || sub.balancer || L('epwatch_movie_any_balancer');
            } else {
                var voice = sub.voice || anyWord;
                var srcTag = sub.structure_source === 'tvdb' ? ' · TVDB'
                           : sub.structure_source === 'absolute' ? ' · ABS' : '';
                var seasonNum = sub.target_season && sub.target_season > 0 ? sub.target_season : sub.last_season;
                var hasVoice = !!sub.voice;
                var shownAired = hasVoice ? (sub.last_voice_episode || 0) : (sub.season_aired || 0);
                var tmdbAired = sub.season_aired || 0;
                if (sub.season_total && sub.season_total > 0)
                    position = 'S' + seasonNum + ' E' + shownAired + '/' + sub.season_total;
                else
                    position = 'S' + seasonNum + ' E' + (hasVoice ? (sub.last_voice_episode || 0) : sub.last_episode);
                if (hasVoice && sub.season_total && sub.season_total > 0 && tmdbAired > shownAired)
                    tmdbHint = '<span class="card__subscribe-tmdb">' + L('epwatch_tmdb_short') + ' ' + tmdbAired + '/' + sub.season_total + '</span>';
                var balTag = (hasVoice && (sub.balancer_name || sub.balancer)) ? '<br>🌐 ' + (sub.balancer_name || sub.balancer) : '';
                voiceLine = '🎙 ' + voice + srcTag + balTag;
            }

            try {
                var $card = card.render();
                var $view = $card.find('.card__view').first();
                if ($view.length) {
                    var html =
                        '<div class="card__subscribe">' +
                            '<div class="card__subscribe-status on"></div>' +
                            '<div class="card__subscribe-position">' + position + tmdbHint + '</div>' +
                            '<div class="card__subscribe-voice">' + voiceLine + '</div>' +
                        '</div>';
                    $view.after(html);
                }
            } catch (e) {}

            card.onEnter = function (t, d) {
                Lampa.Activity.push({
                    url: '', component: 'full',
                    id: d.tmdb_id || d.id,
                    method: isMovie ? 'movie' : 'tv', card: d, source: 'tmdb'
                });
            };
        };

        return comp;
    }

    function promptLink(activity) {
        activity.activity.loader(false);
        get(api('/epwatch/link'), function (r) {
            if (!r || !r.success) return Lampa.Noty.show(L('epwatch_unavailable'));

            var pollTimer = null;
            var attempts = 0;
            var finished = false;

            function stopPoll() {
                if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
            }

            Lampa.Select.show({
                title: L('epwatch_link_title'),
                items: [{ title: L('epwatch_link_open_bot') + (r.bot ? ' @' + r.bot : ''), subtitle: r.link, link: r.link }],
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (typeof Android !== 'undefined' && Android.openBrowser) Android.openBrowser(a.link);
                    else if (window.open) window.open(a.link, '_blank');
                },
                onBack: function () { stopPoll(); Lampa.Activity.backward(); }
            });

            pollTimer = setInterval(function () {
                if (finished || ++attempts > 100) { stopPoll(); return; }
                get(api('/epwatch/status?tmdb_id=0'), function (s) {
                    if (finished || !s || !s.linked) return;
                    finished = true;
                    stopPoll();
                    if ($('body').hasClass('selectbox--open')) Lampa.Select.close();
                    Lampa.Noty.show(L('epwatch_linked'));
                    Lampa.Activity.replace();
                });
            }, 3000);
        });
    }

    function openSubsPage() {
        Lampa.Activity.push({ url: '', title: L('epwatch_menu'), component: MENU_ACTION, page: 1 });
    }

    function addMenu() {
        if ($('.menu__item[data-action="' + MENU_ACTION + '"]').length) return;
        Lampa.Menu.addButton(ICON_LOGO, L('epwatch_menu'), openSubsPage).attr('data-action', MENU_ACTION);
    }

    function bindFullListener() {
        Lampa.Listener.follow('full', function (e) {
            if (e.type !== 'complite') return;
            setTimeout(function () { overrideButton(e.object); }, 300);
        });
    }

    function overrideButton(obj) {
        if (!obj || !obj.card) return;
        var card = obj.card;
        var isMovie = !card.number_of_seasons &&
            (obj.method === 'movie' || !!card.release_date || (!!card.title && !card.name));
        if (!card.number_of_seasons && !isMovie) return;
        var $btn;
        try { $btn = obj.activity.render().find('.button--subscribe'); } catch (e) { return; }
        if (!$btn || !$btn.length) return;

        $btn.removeClass('hide');
        $btn.off('hover:enter');
        try { $btn.find('span').text('EpWatch'); } catch (e) {}

        function refreshFlag() {
            get(api('/epwatch/status?tmdb_id=' + card.id), function (s) {
                if (s && s.subscribed) $btn.addClass('active').find('path').attr('fill', 'currentColor');
                else $btn.removeClass('active').find('path').attr('fill', 'transparent');
            });
        }
        refreshFlag();

        $btn.on('hover:enter', function () {
            get(api('/epwatch/status?tmdb_id=' + card.id), function (fresh) {
                if (!fresh || !fresh.linked) return showLink(function () {
                    Lampa.Controller.toggle('content');
                });
                if (isMovie) showMovieMenu(card, $btn, fresh, refreshFlag);
                else showVoiceMenu(card, $btn, fresh, refreshFlag);
            });
        });
    }

    function showVoiceMenu(card, $btn, status, onChanged) {
        var title  = card.name || card.title || '';
        var year   = card.first_air_date ? parseInt(card.first_air_date) : (card.year || 0);
        var tmdbId = card.id || 0;
        var anyWord = L('epwatch_voice_any_word');

        Lampa.Loading.start(function () { Lampa.Loading.stop(); });

        get(api('/epwatch/voices?tmdb_id=' + tmdbId + '&title=' + encodeURIComponent(title) + '&year=' + year),
        function (r) {
            Lampa.Loading.stop();
            var season = (r && r.season) || card.number_of_seasons || 1;
            var items  = [];

            if (status && status.subscribed) {
                if (status.voices && status.voices.length) {
                    status.voices.forEach(function (v) {
                        items.push({ title: L('epwatch_unsub_prefix') + (v || anyWord), unsubscribe: true, voice: v });
                    });
                } else {
                    items.push({ title: L('epwatch_unsub'), unsubscribe: true, voice: null });
                }
                items.push({ title: '', separator: true });
            }

            items.push({
                title:    L('epwatch_voice_any'),
                subtitle: L('epwatch_voice_any_sub'),
                voice: '', balancer: ''
            });

            if (r && r.voices && r.voices.length) {
                items.push({ title: L('epwatch_voice_list'), separator: true });
                r.voices.forEach(function (v) {
                    items.push({
                        title:    '🎙 ' + v.name,
                        subtitle: v.balancer ? '[' + v.balancer + ']' : '',
                        voice:    v.name,
                        balancer: v.balancer || ''
                    });
                });
            }

            Lampa.Select.show({
                title: L('epwatch_title'),
                items: items,
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (a.unsubscribe) doUnsub(card, $btn, a.voice, onChanged);
                    else pickSeasonAndSubscribe(card, a.voice, a.balancer, season, $btn, onChanged);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        },
        function () {
            Lampa.Loading.stop();
            var items = [{ title: L('epwatch_subscribe_only'), voice: '', balancer: '' }];
            if (status && status.subscribed) items.unshift({ title: L('epwatch_unsub'), unsubscribe: true, voice: null });
            Lampa.Select.show({
                title: L('epwatch_title'), items: items,
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (a.unsubscribe) doUnsub(card, $btn, a.voice, onChanged);
                    else pickSeasonAndSubscribe(card, '', '', card.number_of_seasons || 1, $btn, onChanged);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        }, 60000);
    }

    function pickSeasonAndSubscribe(card, voice, balancer, fallbackSeason, $btn, onChanged) {
        Lampa.Loading.start(function () { Lampa.Loading.stop(); });

        get(api('/epwatch/seasons?tmdb_id=' + card.id),
        function (r) {
            Lampa.Loading.stop();
            if (!r || !r.success || !r.seasons || !r.seasons.length) {
                doSub(card, voice, balancer, fallbackSeason, 0, $btn, onChanged);
                return;
            }

            var items = [{
                title:    L('epwatch_season_auto'),
                subtitle: L('epwatch_season_auto_sub'),
                target:   0
            }];

            r.seasons.forEach(function (s) {
                var statusKey = s.status === 'upcoming' ? 'epwatch_season_upcoming'
                              : s.status === 'airing'   ? 'epwatch_season_airing'
                              : 'epwatch_season_aired';
                var sub = s.episode_count + ' ' + L('epwatch_season_eps') +
                          ' · ' + L(statusKey) +
                          (s.air_date ? ' · ' + s.air_date : '');
                items.push({
                    title:    'S' + (s.season_number < 10 ? '0' : '') + s.season_number + ' - ' + (s.name || ''),
                    subtitle: sub,
                    target:   s.season_number
                });
            });

            Lampa.Select.show({
                title: L('epwatch_pick_season'),
                items: items,
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    var initSeason = a.target > 0 ? a.target : (r.latest_aired_season || fallbackSeason);
                    doSub(card, voice, balancer, initSeason, a.target, $btn, onChanged);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        },
        function () {
            Lampa.Loading.stop();
            doSub(card, voice, balancer, fallbackSeason, 0, $btn, onChanged);
        });
    }

    function doSub(card, voice, balancer, season, targetSeason, $btn, onChanged) {
        var payload = {
            tmdb_id:       card.id,
            title:         card.name || card.title || '',
            voice:         voice || '',
            balancer:      balancer || '',
            poster_path:   card.poster_path || '',
            season:        season,
            episode:       0,
            voice_episode: 0,
            target_season: targetSeason || 0
        };
        Lampa.Loading.start(function () { Lampa.Loading.stop(); });
        post(api('/epwatch/subscribe'), payload, function (r) {
            Lampa.Loading.stop();
            if (r && r.success) {
                Lampa.Noty.show(voice ? (L('epwatch_subscribed_v') + voice) : L('epwatch_subscribed'));
                $btn.addClass('active').find('path').attr('fill', 'currentColor');
                if (typeof onChanged === 'function') onChanged();
            } else if (r && r.msg === 'not_linked') showLink(function () {
                Lampa.Controller.toggle('content');
            });
            else Lampa.Noty.show(L('epwatch_err_unknown') + ': ' + ((r && r.msg) || ''));
        }, function () { Lampa.Loading.stop(); Lampa.Noty.show(L('epwatch_err_network')); }, 90000);
    }

    function doUnsub(card, $btn, voice, onChanged) {
        var body = { tmdb_id: card.id };
        if (voice !== null && typeof voice !== 'undefined') body.voice = voice;
        post(api('/epwatch/unsubscribe'), body, function (r) {
            if (r && r.success) {
                Lampa.Noty.show(L('epwatch_unsubscribed'));
                get(api('/epwatch/status?tmdb_id=' + card.id), function (st) {
                    if (!st || !st.subscribed) $btn.removeClass('active').find('path').attr('fill', 'transparent');
                    if (typeof onChanged === 'function') onChanged();
                });
            }
        });
    }

    function showMovieMenu(card, $btn, status, onChanged) {
        var title  = card.title || card.name || '';
        var year   = card.release_date ? parseInt(card.release_date) : (card.year || 0);
        var tmdbId = card.id || 0;

        Lampa.Loading.start(function () { Lampa.Loading.stop(); });

        get(api('/epwatch/balancers?tmdb_id=' + tmdbId + '&title=' + encodeURIComponent(title) + '&year=' + year),
        function (r) {
            Lampa.Loading.stop();
            var items = [];

            if (status && status.subscribed)
                items.push({ title: L('epwatch_unsub'), unsubscribe: true });

            items.push({
                title:    L('epwatch_movie_any'),
                subtitle: L('epwatch_movie_any_sub'),
                balancer: ''
            });

            if (r && r.balancers && r.balancers.length) {
                items.push({ title: L('epwatch_movie_list'), separator: true });
                r.balancers.forEach(function (b) {
                    items.push({ title: '🌐 ' + (b.name || b.balancer), balancer: b.balancer });
                });
            }

            Lampa.Select.show({
                title: L('epwatch_movie_pick') + ' [BETA]',
                items: items,
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (a.unsubscribe) return doUnsub(card, $btn, null, onChanged);
                    doMovieSub(card, a.balancer, $btn, onChanged);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        },
        function () {
            Lampa.Loading.stop();
            var items = [{
                title:    L('epwatch_movie_any'),
                subtitle: L('epwatch_movie_any_sub'),
                balancer: ''
            }];
            if (status && status.subscribed)
                items.unshift({ title: L('epwatch_unsub'), unsubscribe: true });
            Lampa.Select.show({
                title: L('epwatch_movie_pick') + ' [BETA]',
                items: items,
                onSelect: function (a) {
                    Lampa.Controller.toggle('content');
                    if (a.unsubscribe) return doUnsub(card, $btn, null, onChanged);
                    doMovieSub(card, a.balancer, $btn, onChanged);
                },
                onBack: function () { Lampa.Controller.toggle('content'); }
            });
        }, 60000);
    }

    function doMovieSub(card, balancer, $btn, onChanged) {
        var payload = {
            tmdb_id:     card.id,
            title:       card.title || card.name || '',
            media_type:  'movie',
            balancer:    balancer || '',
            poster_path: card.poster_path || ''
        };
        Lampa.Loading.start(function () { Lampa.Loading.stop(); });
        post(api('/epwatch/subscribe'), payload, function (r) {
            Lampa.Loading.stop();
            if (r && r.success) {
                Lampa.Noty.show(L('epwatch_movie_subscribed'));
                $btn.addClass('active').find('path').attr('fill', 'currentColor');
                if (typeof onChanged === 'function') onChanged();
            } else if (r && r.msg === 'not_linked') showLink(function () { Lampa.Controller.toggle('content'); });
            else Lampa.Noty.show(L('epwatch_err_unknown') + ': ' + ((r && r.msg) || ''));
        }, function () { Lampa.Loading.stop(); Lampa.Noty.show(L('epwatch_err_network')); }, 90000);
    }

    function openExternal(link) {
        if (typeof Android !== 'undefined' && Android.openBrowser) Android.openBrowser(link);
        else if (window.open) window.open(link, '_blank');
    }

    function showLink(onClose) {
        get(api('/epwatch/link'), function (r) {
            if (!r || !r.success) return Lampa.Noty.show(L('epwatch_bot_offline'));
            showLinkModal(r.link, r.bot, onClose);
        }, function () { Lampa.Noty.show(L('epwatch_unavailable')); });
    }

    function injectStyles() {
        if (document.getElementById('epwatch-styles')) return;
        var css =
            '.epwatch-link{text-align:center}' +
            '.epwatch-link{max-height:88vh;overflow:auto}' +
            '.epwatch-link__qr{display:inline-block;background:#fff;padding:0.6em;border-radius:0.7em;margin-bottom:0.7em}' +
            '.epwatch-link__qr img{display:block;width:min(14em,38vh);height:min(14em,38vh)}' +
            '.epwatch-link__bot{font-size:1.4em;font-weight:600;margin-bottom:0.3em}' +
            '.epwatch-link__hint{opacity:0.6;font-size:1em;margin-bottom:0.8em}' +
            '.epwatch-link__url{font-size:0.85em;opacity:0.5;word-break:break-all;max-width:30em;margin:0 auto;line-height:1.4}' +
            '.epwatch-link__manual{font-size:0.85em;opacity:0.7;max-width:30em;margin:0.8em auto 0;line-height:1.4}' +
            '.epwatch-link__scan{font-size:0.95em;opacity:0.85;max-width:30em;margin:0.8em auto 0.2em;line-height:1.4}' +
            '.epwatch-link__code{display:inline-block;margin:0.5em auto 0;padding:0.45em 0.9em;background:rgba(255,255,255,0.12);border-radius:0.4em;font-size:1.05em;font-weight:600;word-break:break-all;max-width:30em;line-height:1.5}' +
            '.card__subscribe{font-size:0.85em}' +
            '.card__subscribe-tmdb{display:block;font-size:0.72em;opacity:0.55;margin-top:0.15em;line-height:1.1;letter-spacing:0.02em}';
        var style = document.createElement('style');
        style.id = 'epwatch-styles';
        style.appendChild(document.createTextNode(css));
        document.head.appendChild(style);
    }

    function showLinkModal(link, bot, onClose) {
        injectStyles();
        var qr = 'https://api.qrserver.com/v1/create-qr-code/?size=320x320&margin=10&data=' + encodeURIComponent(link);
        var botName = bot ? '@' + bot : '';
        var code = '';
        try { code = decodeURIComponent((link.match(/[?&](?:start|text)=([^&]+)/) || [])[1] || ''); }
        catch (e) { code = (link.match(/[?&](?:start|text)=([^&]+)/) || [])[1] || ''; }

        var body = $(
            '<div class="epwatch-link">' +
                '<div class="epwatch-link__qr"><img src="' + qr + '" alt="QR"/></div>' +
                '<div class="epwatch-link__bot">' + botName + '</div>' +
                '<div class="epwatch-link__hint">' + L('epwatch_link_qr_hint') + '</div>' +
                '<div class="epwatch-link__url">' + link + '</div>' +
                '<div class="epwatch-link__manual">' + L('epwatch_link_manual') + '</div>' +
                '<div class="epwatch-link__scan" style="display:none">' + L('epwatch_link_scan') + '</div>' +
                (code ? '<div class="epwatch-link__code" style="display:none">' + code + '</div>' : '') +
            '</div>'
        );

        var textLink = link.indexOf('?start=') >= 0 ? link.replace('?start=', '?text=') : link;
        var textQr = 'https://api.qrserver.com/v1/create-qr-code/?size=320x320&margin=10&data=' + encodeURIComponent(textLink);

        var footer = $(
            '<div class="modal__footer">' +
                '<div class="modal__button selector epwatch-link__open">' + L('epwatch_link_open_bot') + '</div>' +
                '<div class="modal__button selector epwatch-link__text">' + L('epwatch_link_open_text') + '</div>' +
                '<div class="modal__button selector epwatch-link__close">' + L('epwatch_close') + '</div>' +
            '</div>'
        );

        var html = $('<div></div>').append(body).append(footer);

        var pollTimer = null;
        var attempts = 0;
        var finished = false;

        function stopPoll() {
            if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
        }

        var closeAndCallback = function () {
            if (finished) return;
            finished = true;
            stopPoll();
            Lampa.Modal.close();
            if (typeof onClose === 'function') onClose();
        };

        var currentLink = link;
        footer.find('.epwatch-link__open').on('hover:enter', function () { openExternal(currentLink); });
        footer.find('.epwatch-link__text').on('hover:enter', function () {
            body.find('.epwatch-link__qr img').attr('src', textQr);
            currentLink = textLink;
            body.find('.epwatch-link__hint, .epwatch-link__url, .epwatch-link__manual').css('display', 'none');
            body.find('.epwatch-link__scan, .epwatch-link__code').css('display', '');
        });
        footer.find('.epwatch-link__close').on('hover:enter', function () { closeAndCallback(); });

        Lampa.Modal.open({
            title: L('epwatch_link_title'),
            html: html,
            size: 'medium',
            align: 'center',
            onBack: function () { closeAndCallback(); }
        });

        pollTimer = setInterval(function () {
            if (finished || ++attempts > 100) { stopPoll(); return; }
            get(api('/epwatch/status?tmdb_id=0'), function (s) {
                if (finished || !s || !s.linked) return;
                finished = true;
                stopPoll();
                Lampa.Modal.close();
                Lampa.Noty.show(L('epwatch_linked'));
                if (typeof onClose === 'function') onClose();
            });
        }, 3000);
    }

    function addSettings() {
        if (window.epwatch_settings_ready) return;
        window.epwatch_settings_ready = true;

        Lampa.SettingsApi.addComponent({
            component: 'epwatch',
            name:      L('epwatch_menu'),
            icon:      ICON_LOGO
        });

        Lampa.SettingsApi.addParam({
            component: 'epwatch',
            param: { name: 'epwatch_about', type: 'static' },
            field: {
                name:        META.name + ' · v' + META.version,
                description: L('epwatch_settings_author') + ': ' + META.author
            },
            onRender: function (item) {
                try {
                    item.find('.settings-param__name').css({ 'font-weight': '600' });
                    item.find('.settings-param__descr').css({ 'opacity': '0.75' });
                } catch (e) {}
            }
        });

        Lampa.SettingsApi.addParam({
            component: 'epwatch',
            param: {
                name:    'epwatch_lang',
                type:    'select',
                values:  {
                    auto: L('epwatch_settings_lang_auto'),
                    uk:   'Українська',
                    en:   'English',
                    ru:   'Русский'
                },
                'default': 'auto'
            },
            field: {
                name:        L('epwatch_settings_lang'),
                description: L('epwatch_settings_lang_desc')
            },
            onChange: function () {}
        });

        Lampa.SettingsApi.addParam({
            component: 'epwatch',
            param: { name: 'epwatch_uid', type: 'input', values: '', 'default': localUid() },
            field: {
                name:        L('epwatch_settings_uid'),
                description: L('epwatch_settings_uid_desc')
            },
            onRender: function (item) {
                try {
                    if (!customUid()) {
                        var lu = localUid();
                        if (lu) item.find('.settings-param__value').text(lu);
                    }
                } catch (e) {}
            },
            onChange: function () {}
        });

        Lampa.SettingsApi.addParam({
            component: 'epwatch',
            param: { type: 'button' },
            field:  { name: L('epwatch_settings_open') },
            onChange: function () {
                Lampa.Controller.toggle('settings_component');
                openSubsPage();
            }
        });

        Lampa.SettingsApi.addParam({
            component: 'epwatch',
            param: { name: 'epwatch_telegram', type: 'button' },
            field:  { name: L('epwatch_settings_link') },
            onRender: function (item) {
                try {
                    get(api('/epwatch/status?tmdb_id=0'), function (s) {
                        var linked = s && s.linked;
                        item.find('.settings-param__name').text(
                            linked ? L('epwatch_settings_unlink') : L('epwatch_settings_link')
                        );
                    });
                } catch (e) {}
            },
            onChange: function () {
                get(api('/epwatch/status?tmdb_id=0'), function (s) {
                    if (s && s.linked) confirmUnlink();
                    else showLink(function () {
                        Lampa.Controller.toggle('settings_component');
                    });
                });
            }
        });
    }

    function confirmUnlink() {
        Lampa.Select.show({
            title: L('epwatch_unlink_confirm'),
            items: [
                { title: L('epwatch_yes'), action: 'yes' },
                { title: L('epwatch_no'),  action: 'no'  }
            ],
            onSelect: function (a) {
                Lampa.Controller.toggle('settings_component');
                if (a.action === 'yes') {
                    post(api('/epwatch/unlink'), {}, function (r) {
                        if (r && r.success) Lampa.Noty.show(L('epwatch_unlink_done'));
                        else Lampa.Noty.show(L('epwatch_err_unknown'));
                    }, function () { Lampa.Noty.show(L('epwatch_err_network')); });
                }
            },
            onBack: function () { Lampa.Controller.toggle('settings_component'); }
        });
    }

    function init() {
        registerLang();
        injectStyles();
        Lampa.Component.add(MENU_ACTION, SubsComponent);
        setTimeout(addMenu, 500);
        bindFullListener();
        addSettings();
        console.log('[EpWatch] plugin loaded');
    }

    if (window.appready) init();
    else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') init(); });
})();