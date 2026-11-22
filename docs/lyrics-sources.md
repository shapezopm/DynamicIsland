# Тексты песен и источники

Островок ищет текст для текущего трека. Выбрать нужные режимы отображения можно в разделе **«Настройки → Текст песен»**.

| Экран | Как пользоваться |
| --- | --- |
| [Настройки текста](lyrics-settings.png) | Выбери источники и режимы отображения |
| [Эффекты текста](lyrics-effects.png) | Сравни «Без эффекта», «Подъём», «Сияние» и «Перелив» |

Обычный текст отображается, если в настройках включён режим **«Без синхронизации»**. Подсветка по времени зависит от доступной позиции воспроизведения и таймкодов.

## Провайдеры

| Источник | Включён по умолчанию | Реализация |
| --- | --- | --- |
| LRCLIB | Да | [LrclibProvider](../src/Api/Lyrics/LrclibProvider.cs) |
| Deezer | Да | [CatalogueLyricsProvider](../src/Api/Lyrics/CatalogueLyricsProvider.cs) |
| NetEase | Да | [NetEaseLyricsProvider](../src/Api/Lyrics/NetEaseLyricsProvider.cs) |
| QQ Music | Да | [CatalogueLyricsProvider](../src/Api/Lyrics/CatalogueLyricsProvider.cs) |
| YouTube Music | Да | [YouTubeMusicLyricsProvider](../src/Api/Lyrics/YouTubeMusicLyricsProvider.cs) |
| AMLL TTML DB | Да | [LrclibProvider](../src/Api/Lyrics/LrclibProvider.cs) |
| LiriQo | Да | [LiriQoProvider](../src/Api/Lyrics/LiriQoProvider.cs) |
| Kugou | Нет | [CatalogueLyricsProvider](../src/Api/Lyrics/CatalogueLyricsProvider.cs) |
| Paxsenix | Нет | [CatalogueLyricsProvider](../src/Api/Lyrics/CatalogueLyricsProvider.cs) |
| Genius | Нет | [LyricsWebSearch](../src/Lyrics/Search/LyricsWebSearch.cs) |
| Karalyr | Нет | [LrclibProvider](../src/Api/Lyrics/LrclibProvider.cs) |
| Better Lyrics | Нет | [BetterLyricsProvider](../src/Api/Lyrics/BetterLyricsProvider.cs) |
| LRC Mux | Нет | [LrcMuxProvider](../src/Api/Lyrics/LrcMuxProvider.cs) |

Список определяется [настройками](../src/App/Settings.cs) и [цепочкой провайдеров](../src/Lyrics/Search/LyricsSources.cs). Сторонние сервисы могут ограничивать частоту запросов или быть временно недоступны.
