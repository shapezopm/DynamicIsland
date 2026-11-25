# DynamicIsland для Vencord на Windows

1. В [релизах DynamicIsland](https://github.com/shapezopm/DynamicIsland/releases) открой свою версию и скачай `Plugins.zip` из Assets. Распакуй внешний архив и открой `Plugins/Vencord`. Для локальной сборки эта папка уже есть в `publish/Assets/Plugins/Vencord`.
2. Оставь `Install-DynamicIslandVencord.cmd` и `DynamicIslandVencord-Build.zip` рядом. Установщик сам распакует внутренний архив.
3. Полностью закрой Discord через значок в трее. Дважды щёлкни по `Install-DynamicIslandVencord.cmd`.
4. В официальном окне Vencord Installer выбери свой Discord. Для обычного клиента это Discord Stable. Если Vencord уже установлен, нажми **Reinstall / Repair**. При первой установке нажми **Install**.
5. Запусти Discord. В настройках открой **Vencord → Plugins**, найди **DynamicIslandDiscordBridge** и включи тумблер.
6. В острове включи **Discord → Включить Discord**. В разделе **Discord → Плагины** на строке Vencord появится статус **«Подключён»**.

Комплект предназначен для настольного Discord на Windows. Установщик хранит файлы сборки в `%LOCALAPPDATA%\DynamicIsland\VencordSetup`, откуда Discord загружает Vencord. При повторном запуске с тем же архивом файлы проверяются и переиспользуются. После обновления Discord при необходимости запусти этот же установщик снова.

Чтобы вернуться к обычной сборке, скачай [официальный установщик Vencord](https://vencord.dev/download/), запусти его напрямую, выбери свой Discord и нажми **Reinstall / Repair**. После этого перезапусти Discord.

Внутри архива лежат сведения о сборке, исходники Vencord с добавленным плагином и лицензии. [Исходники установщика Vencord v1.4.2](https://github.com/Vencord/Installer/tree/v1.4.2)
