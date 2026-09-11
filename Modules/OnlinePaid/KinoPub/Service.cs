using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace KinoPub;

public class KinoPubInvoke
{
    #region KinoPubInvoke
    string host, token;
    string apihost;
    HttpHydra http;
    Func<string, string, string> onstreamfile;

    public KinoPubInvoke(string host, string apihost, string token, HttpHydra httpHydra, Func<string, string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.apihost = apihost;
        this.token = token;
        http = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Search
    async public Task<SearchResult> Search(string title, string original_title, short year, short clarification, string imdb_id, long kinopoisk_id)
    {
        if (string.IsNullOrEmpty(title ?? original_title))
            return null;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        #region goSearch
        async Task<SearchResult> goSearch(string q)
        {
            if (string.IsNullOrEmpty(q))
                return null;

            var root = await http.Get<SearchObject>($"{apihost}/v1/items/search?q={HttpUtility.UrlEncode(q)}&access_token={token}&field=title&perpage=200", safety: true, textJson: true);
            if (root?.items == null)
                return null;

            var ids = new List<int>(root.items.Length);
            var result = new SearchResult() { similars = new SimilarTpl(root.items.Length) };

            string _q = SearchNameTo.Convert(q);

            foreach (var item in root.items)
            {
                string img = PosterApi.Size(item.posters?.Skip(1)?.First().Value);
                result.similars.Append(item.title, item.year.ToString(), item.voice, host + $"lite/kinopub?postid={item.id}&title={enc_title}&original_title={enc_original_title}", img);

                if ((item.kinopoisk > 0 && item.kinopoisk == kinopoisk_id) || $"tt{item.imdb}" == imdb_id)
                {
                    if (item.type != "3d")
                        result.id = item.id;
                }
                else
                {
                    if (item.year == year || (item.year == year - 1) || (item.year == year + 1))
                    {
                        if (SearchNameTo.StartsWith(item.title, _q) ||
                            SearchNameTo.EndsWith(item.title, _q))
                            ids.Add(item.id);
                    }
                }
            }

            if (ids.Count == 1 && result.id == 0)
                result.id = ids[0];

            return result;
        }
        #endregion

        if (clarification == 1)
            return await goSearch(title);

        return (await goSearch(original_title)) ?? (await goSearch(title));
    }
    #endregion

    #region Post
    async public Task<RootObject> Post(int postid)
    {
        var root = await http.Get<RootObject>($"{apihost}/v1/items/{postid}?access_token={token}", safety: true, textJson: true);
        if (root?.item.seasons == null && root?.item.videos == null)
            return null;

        return root;
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(RootObject root, string filetype, string title, string original_title, int postid, short s = -1, int t = -1, string codec = null, string lang = null, VastConf vast = null, bool rjson = false)
    {
        if (root == null)
            return default;

        if (string.IsNullOrEmpty(codec))
            codec = null;

        if (string.IsNullOrEmpty(lang))
            lang = null;

        if (root?.item.videos != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, root.item.videos.Length);

            if (filetype == "hls")
            {
                foreach (var a in root.item.videos[0].audios)
                {
                    var streamquality = new StreamQualityTpl();

                    foreach (var f in root.item.videos[0].files)
                    {
                        if (!string.IsNullOrEmpty(f.url.hls))
                            streamquality.Append(onstreamfile(f.url.hls.Replace("a1.m3u8", $"a{a.index}.m3u8"), null), f.quality);
                    }

                    if (streamquality.IsEmpty)
                        continue;

                    string voice = a.type?.title ?? a.lang ?? "оригинал";
                    if (!string.IsNullOrEmpty(a?.author?.title))
                        voice += $" ({a.author.title})";

                    #region subtitle
                    var subtitles = new SubtitleTpl(root.item.videos[0]?.subtitles?.Length ?? 0);

                    if (root.item.videos[0].subtitles != null)
                    {
                        foreach (var sub in root.item.videos[0].subtitles)
                        {
                            if (sub.url != null)
                                subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                        }
                    }

                    string subtitles_call = null;
                    if (subtitles.IsEmpty)
                        subtitles_call = host + $"lite/kinopub/subtitles.json?mid={root.item.videos[0].id}";
                    #endregion

                    var first = streamquality.Firts();

                    mtpl.Append(
                        voice,
                        first.link,
                        quality: first.quality,
                        streamquality: streamquality,
                        subtitles: subtitles,
                        subtitles_call: subtitles_call,
                        voice_name: a.codec,
                        vast: vast
                    );
                }
            }
            else
            {
                foreach (var v in root.item.videos)
                {
                    #region voicename
                    string voicename = string.Empty;

                    if (v.audios != null)
                    {
                        foreach (var audio in v.audios)
                        {
                            if (audio.lang == "eng")
                            {
                                if (!voicename.Contains(audio.lang))
                                    voicename += "eng, ";
                            }
                            else
                            {
                                string a = audio?.author?.title ?? audio?.type?.title;
                                if (a != null)
                                {
                                    a = $"{a} ({audio.lang})";
                                    if (!voicename.Contains(a))
                                        voicename += $"{a}, ";
                                }
                            }
                        }

                        voicename = Regex.Replace(voicename, "[, ]+$", "");
                    }
                    #endregion

                    if (filetype == "hls4")
                    {
                        if (v.files[0].url.hls4 != null)
                        {
                            mtpl.Append(
                                v.files[0].quality,
                                onstreamfile(v.files[0].url.hls4, null),
                                voice_name: voicename,
                                vast: vast
                            );
                        }
                    }
                    else
                    {
                        if (v.files[0].url.http == null)
                            continue;

                        #region subtitle
                        var subtitles = new SubtitleTpl(v.subtitles?.Length ?? 0);

                        if (v.subtitles != null)
                        {
                            foreach (var sub in v.subtitles)
                            {
                                if (sub.url != null)
                                    subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                            }
                        }

                        string subtitles_call = null;
                        if (subtitles.IsEmpty)
                            subtitles_call = host + $"lite/kinopub/subtitles.json?mid={v.id}";
                        #endregion

                        var streamquality = new StreamQualityTpl(v.files.Length);

                        foreach (var f in v.files)
                            streamquality.Append(onstreamfile(f.url.http, f.file), f.quality);

                        var first = streamquality.Firts();
                        if (first != null)
                        {
                            mtpl.Append(
                                first.quality,
                                first.link,
                                subtitles: subtitles,
                                subtitles_call: subtitles_call,
                                voice_name: voicename,
                                streamquality: streamquality,
                                vast: vast
                            );
                        }
                    }
                }
            }

            return mtpl;
            #endregion
        }
        else
        {
            if (root?.item.seasons == null || root.item.seasons.Length == 0)
                return default;

            #region Сериал
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl(root.item.quality > 0 ? $"{root.item.quality}p" : null, root.item.seasons.Length);

                foreach (var season in root.item.seasons)
                {
                    tpl.Append(
                        $"{season.number} сезон",
                        host + $"lite/kinopub?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={season.number}",
                        season.number
                    );
                }

                return tpl;
                #endregion
            }
            else
            {
                #region Серии
                if (filetype == "hls")
                {
                    var season = root.item.seasons.FirstOrDefault(i => i.number == s);
                    if (season?.episodes == null || season.episodes.Length == 0)
                        return default;

                    #region Перевод
                    // озвучки со всех серий сезона; root закэширован и общий для запросов - только читаем
                    var voices = new List<KinoPubVoice>(20);
                    var voicesByKey = new Dictionary<string, KinoPubVoice>(20);
                    var knownLangs = KnownLangs(season.episodes);

                    foreach (var episode in season.episodes)
                    {
                        if (episode.audios == null)
                            continue;

                        var episodeKeys = new HashSet<string>(20);

                        foreach (var a in episode.audios)
                        {
                            var (idt, voice) = VoiceId(a);
                            if (string.IsNullOrEmpty(voice))
                                continue;

                            string alang = EpisodeLang(a, episode.audios, knownLangs);
                            string key = VoiceKey(idt, a.codec, alang);
                            if (!episodeKeys.Add(key))
                                continue;

                            if (!voicesByKey.TryGetValue(key, out var v))
                            {
                                v = new KinoPubVoice(idt, voice, a.codec, alang);
                                voicesByKey.Add(key, v);
                                voices.Add(v);
                            }

                            v.episodes++;
                        }
                    }

                    if (voices.Count == 0)
                        return default;

                    KinoPubVoice selected = null;

                    if (t != -1)
                    {
                        // точное совпадение, затем ссылки без codec/lang
                        selected = voices.FirstOrDefault(v => v.id == t && v.codec == codec && v.lang == lang)
                            ?? voices.FirstOrDefault(v => v.id == t && (codec == null || codec == v.codec) && (lang == null || lang == v.lang));
                    }

                    // по умолчанию озвучка с наибольшим весом, см. DefaultWeight
                    selected ??= voices.OrderByDescending(DefaultWeight).First();

                    var vtpl = new VoiceTpl(voices.Count);
                    var names = VoiceNames(voices);

                    for (int i = 0; i < voices.Count; i++)
                    {
                        var v = voices[i];
                        string link = host + $"lite/kinopub?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={s}&t={v.id}&codec={v.codec}&lang={v.lang}";

                        vtpl.Append(
                            names[i],
                            v == selected,
                            link
                        );
                    }
                    #endregion

                    #region Серии
                    var etpl = new EpisodeTpl(vtpl, season.episodes.Length);
                    string selectedKey = VoiceKey(selected.id, selected.codec, selected.lang);

                    foreach (var episode in season.episodes)
                    {
                        var audio = episode.audios?.FirstOrDefault(a => VoiceKey(VoiceId(a).id, a.codec, EpisodeLang(a, episode.audios, knownLangs)) == selectedKey);

                        // в серии нет выбранной озвучки - пропускаем только эту серию
                        if (audio == null)
                            continue;

                        var streamquality = new StreamQualityTpl();

                        foreach (var f in episode.files)
                        {
                            if (!string.IsNullOrEmpty(f.url.hls))
                                streamquality.Append(onstreamfile(f.url.hls.Replace("a1.m3u8", $"a{audio.index}.m3u8"), null), f.quality);
                        }

                        #region subtitle
                        var subtitles = new SubtitleTpl(episode.subtitles?.Length ?? 0);

                        if (episode.subtitles != null)
                        {
                            foreach (var sub in episode.subtitles)
                            {
                                if (sub.url != null)
                                    subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                            }
                        }

                        string subtitles_call = null;
                        if (subtitles.IsEmpty)
                            subtitles_call = host + $"lite/kinopub/subtitles.json?mid={episode.id}";
                        #endregion

                        var first = streamquality.Firts();
                        if (first != null)
                        {
                            etpl.Append(
                                $"{episode.number} серия",
                                title ?? original_title,
                                s,
                                episode.number,
                                first.link,
                                streamquality: streamquality,
                                subtitles: subtitles,
                                subtitles_call: subtitles_call,
                                vast: vast
                            );
                        }
                    }
                    #endregion

                    return etpl;
                }
                else
                {
                    var episodes = root.item.seasons.First(i => i.number == s).episodes;

                    var etpl = new EpisodeTpl(episodes.Length);

                    foreach (var episode in episodes)
                    {
                        #region voicename
                        string voicename = string.Empty;

                        if (episode.audios != null)
                        {
                            foreach (var audio in episode.audios)
                            {
                                string a = audio.author?.title ?? audio.lang;
                                if (a != null && !voicename.Contains(a) && a != "rus")
                                    voicename += $"{a}, ";
                            }

                            voicename = Regex.Replace(voicename, "[, ]+$", "");
                        }
                        #endregion

                        if (filetype == "hls4")
                        {
                            if (episode.files[0].url.hls4 == null)
                                continue;

                            etpl.Append(
                                $"{episode.number} серия",
                                title ?? original_title,
                                s,
                                episode.number,
                                onstreamfile(episode.files[0].url.hls4,
                                null),
                                voice_name: voicename,
                                vast: vast
                            );
                        }
                        else
                        {
                            if (episode.files[0].url.http == null)
                                continue;

                            #region subtitle
                            var subtitles = new SubtitleTpl(episode.subtitles?.Length ?? 0);

                            if (episode.subtitles != null)
                            {
                                foreach (var sub in episode.subtitles)
                                {
                                    if (sub.url != null)
                                        subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                                }
                            }

                            string subtitles_call = null;
                            if (subtitles.IsEmpty)
                                subtitles_call = host + $"lite/kinopub/subtitles.json?mid={episode.id}";
                            #endregion

                            #region streams
                            var streamquality = new StreamQualityTpl();

                            foreach (var f in episode.files)
                            {
                                if (f.url.http != null)
                                    streamquality.Append(onstreamfile(f.url.http, f.file), f.quality);
                            }
                            #endregion

                            var first = streamquality.Firts();
                            if (first != null)
                            {
                                etpl.Append(
                                    $"{episode.number} серия",
                                    title ?? original_title,
                                    s,
                                    episode.number,
                                    first.link,
                                    subtitles: subtitles,
                                    subtitles_call: subtitles_call,
                                    voice_name: voicename,
                                    streamquality: streamquality,
                                    vast: vast
                                );
                            }
                        }
                    }

                    return etpl;
                }
                #endregion
            }
            #endregion
        }
    }
    #endregion

    #region Voice
    sealed class KinoPubVoice
    {
        public KinoPubVoice(int id, string name, string codec, string lang)
        {
            this.id = id;
            this.name = name;
            this.codec = string.IsNullOrEmpty(codec) ? null : codec;
            this.lang = string.IsNullOrEmpty(lang) ? null : lang;
        }

        public int id { get; }

        public string name { get; }

        public string codec { get; }

        public string lang { get; }

        public int episodes { get; set; }
    }

    // id = author.id ?? type.id; без автора и типа: 6 - оригинал на английском, 0 - по умолчанию
    static (int id, string name) VoiceId(Audio a)
    {
        int? id = a.author?.id ?? a.type?.id;
        if (id != null)
            return ((int)id, a.author?.title ?? a.type?.title);

        if (a.lang == "eng")
            return (6, "Оригинал");

        return (0, "По умолчанию");
    }

    static string VoiceKey(int id, string codec, string lang)
        => $"{id}:{codec}:{lang}";

    // число серий с озвучкой: русская x4, без языка x2, aac x2
    static int DefaultWeight(KinoPubVoice v)
        => v.episodes * (v.lang == "rus" ? 4 : v.lang == null ? 2 : 1) * (v.codec == "aac" ? 2 : 1);

    static string KnownLang(string lang)
        => string.IsNullOrEmpty(lang) || lang == "und" || lang == "unk" ? null : lang;

    // известные языки каждой озвучки (id + codec) в сезоне
    static Dictionary<string, HashSet<string>> KnownLangs(Episode[] episodes)
    {
        var result = new Dictionary<string, HashSet<string>>(20);

        foreach (var episode in episodes)
        {
            if (episode.audios == null)
                continue;

            foreach (var a in episode.audios)
            {
                string alang = KnownLang(a.lang);
                if (alang == null)
                    continue;

                string key = VoiceKey(VoiceId(a).id, a.codec, null);
                if (!result.TryGetValue(key, out var langs))
                    result.Add(key, langs = new HashSet<string>());

                langs.Add(alang);
            }
        }

        return result;
    }

    // неизвестный язык (пусто, und, unk) относим к единственному известному языку этой озвучки в сезоне,
    // если в серии нет её дорожки с языком
    static string EpisodeLang(Audio a, Audio[] audios, Dictionary<string, HashSet<string>> knownLangs)
    {
        string alang = KnownLang(a.lang);
        if (alang != null)
            return alang;

        string key = VoiceKey(VoiceId(a).id, a.codec, null);

        if (audios.Any(i => i != a && KnownLang(i.lang) != null && VoiceKey(VoiceId(i).id, i.codec, null) == key))
            return null;

        return knownLangs.TryGetValue(key, out var langs) && langs.Count == 1 ? langs.First() : null;
    }

    // язык в названии для нерусских дорожек, кроме английского оригинала;
    // при совпадении названий его сохраняет озвучка с известным языком и наибольшим числом серий,
    // остальным добавляется язык (und, если неизвестен), затем id
    static string[] VoiceNames(List<KinoPubVoice> voices)
    {
        var names = voices.Select(v => VoiceName(v, v.lang == "rus" || (v.id == 6 && v.lang == "eng") ? null : v.lang)).ToArray();

        for (int pass = 0; pass < 2; pass++)
        {
            var duplicates = Enumerable.Range(0, names.Length)
                .GroupBy(i => names[i])
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicates)
            {
                int main = group.OrderBy(i => voices[i].lang == null).ThenByDescending(i => voices[i].episodes).First();

                foreach (int i in group)
                {
                    if (i != main)
                        names[i] = pass == 0 ? VoiceName(voices[i], voices[i].lang ?? "und") : $"{names[i]} #{voices[i].id}";
                }
            }
        }

        return names;
    }

    static string VoiceName(KinoPubVoice v, string lang)
        => lang == null ? $"{v.name} ({v.codec})" : $"{v.name} ({v.codec}, {lang})";
    #endregion
}
