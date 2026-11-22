import { definePluginSettings } from "@api/Settings";
import { Logger } from "@utils/Logger";
import definePlugin, { OptionType, PluginNative } from "@utils/types";
import { findByProps, findStore } from "@webpack";
import { FluxDispatcher } from "@webpack/common";

import { DynamicIslandDiscordBridge } from "./bridge.js";

const name = "DynamicIslandDiscordBridge";
const native = VencordNative.pluginHelpers[name] as PluginNative<typeof import("./native")>;
const logger = new Logger(name);
const settings = definePluginSettings({
    forward: {
        type: OptionType.SELECT,
        description: "Какие сообщения передавать в остров",
        options: [
            { label: "Личные сообщения и упоминания", value: "notify", default: true },
            { label: "Все сообщения", value: "all" },
        ],
    },
    includeText: {
        type: OptionType.BOOLEAN,
        description: "Передавать текст сообщений в остров",
        default: true,
    },
});

let bridge: DynamicIslandDiscordBridge | undefined;

export default definePlugin({
    name: "DynamicIslandDiscordBridge",
    description: "Передаёт состояние звонка и уведомления в Dynamic Island через локальный мост.",
    authors: [{ name: "Ruslik31", id: 0n }],
    settings,
    start() {
        bridge = new DynamicIslandDiscordBridge({
            findByProps,
            findStore,
            dispatcher: FluxDispatcher,
            readBridgeConfig: native.readBridgeConfig,
            settings: settings.store,
            log: (where: string, error: unknown) => logger.error(where, error),
        });
        bridge.start();
    },
    stop() {
        bridge?.stop();
        bridge = undefined;
    },
});
