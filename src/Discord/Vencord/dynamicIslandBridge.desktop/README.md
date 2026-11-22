# Dynamic Island для Vencord

Плагин для настольного Discord на Windows. Он использует локальный мост Dynamic Island, передаёт личные сообщения и упоминания, состояние звонка и активность речи. Токен аккаунта Discord не используется.

1. Подготовьте исходники [Vencord](https://docs.vencord.dev/installing/) и установите зависимости.
2. Распакуйте папку `dynamicIslandBridge.desktop` в `src/userplugins` исходников Vencord.
3. Соберите Vencord командой `pnpm build`. Для Discord Desktop установите сборку командой `pnpm inject` и полностью перезапустите Discord.
4. Включите `DynamicIslandDiscordBridge` в **Vencord → Plugins**. В острове включите интеграцию Discord.
5. Статус **«Плагин подключён»** означает, что мост работает. Для проверки сообщения попросите отправить личное сообщение, пока его чат закрыт.

Уведомления и показ текста настраиваются отдельно в острове и в Vencord.
