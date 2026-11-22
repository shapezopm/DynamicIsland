// Port of DynamicIslandDiscordBridge by Ruslik31.
const MESSAGE_TYPES = new Set([0, 19, 20, 21]); // обычное, ответ, команда, ответ на команду
const MAX_TEXT = 600;

export class DynamicIslandDiscordBridge {
    constructor(api) {
        this.api = api;
    }

    // ---------- жизненный цикл ----------

    start() {
        this.stopped = false;
        this.sock = null;
        this.authed = false;
        this.retryMs = 1000;
        this.timers = new Set();
        this.speaking = new Map();
        this.lastVoiceKey = "";
        this.handlers = {};

        this.resolveModules();
        this.subscribe();
        this.connect();
    }

    stop() {
        this.stopped = true;
        const d = this.m && this.m.dispatcher;
        if (d) for (const [type, fn] of Object.entries(this.handlers)) {
            try { d.unsubscribe(type, fn); } catch (e) { /* ничего */ }
        }
        this.handlers = {};
        for (const s of [this.m && this.m.voice, this.m && this.m.selChannel, this.m && this.m.media]) {
            try { s && s.removeChangeListener(this.onStoreChange); } catch (e) { /* ничего */ }
        }
        for (const t of this.timers) clearTimeout(t);
        this.timers.clear();
        clearInterval(this.ping);
        const s = this.sock;
        this.sock = null;
        if (s) { try { s.onclose = null; s.close(); } catch (e) { /* ничего */ } }
    }

    // ---------- модули Discord ----------

    resolveModules() {
        const { findByProps, findStore, dispatcher } = this.api;
        const safe = (fn) => { try { return fn(); } catch (e) { this.err("resolve", e); return null; } };
        const store = name => safe(() => findStore(name));
        this.m = {
            user: store("UserStore"),
            channel: store("ChannelStore"),
            guild: store("GuildStore"),
            member: store("GuildMemberStore"),
            selChannel: store("SelectedChannelStore"),
            voice: store("VoiceStateStore"),
            media: store("MediaEngineStore"),
            rel: store("RelationshipStore"),
            ugs: store("UserGuildSettingsStore"),
            roles: store("GuildRoleStore"),
            dispatcher,
            mediaActions: safe(() => findByProps("toggleSelfMute", "toggleSelfDeaf")),
            volumeActions: safe(() => findByProps("setLocalVolume")),
            channelActions: safe(() => findByProps("selectVoiceChannel", "disconnect")),
        };
        this.caps = {};
        for (const [k, v] of Object.entries(this.m)) this.caps[k] = !!v;
    }

    subscribe() {
        const d = this.m.dispatcher;
        this.onStoreChange = () => this.scheduleVoice();
        for (const s of [this.m.voice, this.m.selChannel, this.m.media]) {
            try { s && s.addChangeListener(this.onStoreChange); } catch (e) { this.err("listener", e); }
        }
        if (!d) return;
        this.handlers = {
            SPEAKING: (a) => this.guard("speaking", () => this.onSpeaking(a)),
            MESSAGE_CREATE: (a) => this.guard("message", () => this.onMessage(a)),
        };
        for (const [type, fn] of Object.entries(this.handlers)) {
            try { d.subscribe(type, fn); } catch (e) { this.err("subscribe " + type, e); }
        }
    }

    // ---------- транспорт ----------

    async connect() {
        if (this.stopped) return;
        const retry = () => this.later(() => this.connect(), this.retryMs = Math.min(this.retryMs * 1.5, 5000));
        let cfg;
        try { cfg = await this.api.readBridgeConfig(); }
        catch { if (!this.stopped) retry(); return; }
        if (this.stopped) return;
        if (!cfg) return retry();

        let ws;
        try { ws = new WebSocket(`ws://127.0.0.1:${cfg.port}/`); } catch (e) { return retry(); }
        this.sock = ws;
        this.authed = false;

        this.ping = setInterval(() => { if (ws.readyState === 1) ws.send('{"t":"ping"}'); }, 15000);
        ws.onopen = () => {
            this.retryMs = 1000;
            ws.send(JSON.stringify({
                t: "hello", token: cfg.token, plugin: "1.1.0-vencord",
                discord: this.discordVersion(), caps: this.caps,
            }));
        };
        ws.onmessage = (ev) => this.guard("rx", () => this.onLine(String(ev.data)));
        ws.onerror = () => { /* onclose придёт следом */ };
        ws.onclose = () => {
            clearInterval(this.ping);
            if (this.sock !== ws) return;
            this.sock = null;
            this.authed = false;
            retry();
        };
    }

    later(fn, ms) {
        const t = setTimeout(() => {
            this.timers.delete(t);
            if (!this.stopped) fn();
        }, ms);
        this.timers.add(t);
    }

    send(obj) {
        const s = this.sock;
        if (!s || s.readyState !== 1) return false;
        if (!this.authed) return false;
        try { s.send(JSON.stringify(obj)); return true; } catch (e) { return false; }
    }

    onLine(line) {
        const msg = JSON.parse(line);
        if (msg.t === "welcome") {
            this.authed = true;
            this.lastVoiceKey = "";
            this.sendSelf();
            this.sendVoice(true);
            return;
        }
        if (this.authed && msg.c) this.onCommand(msg);
    }

    // ---------- команды от острова ----------

    onCommand(msg) {
        const ok = (extra) => this.send(Object.assign({ t: "ack", id: msg.id, ok: true }, extra));
        const fail = (why) => this.send({ t: "ack", id: msg.id, ok: false, error: why });
        const { mediaActions: ma, volumeActions: va, media } = this.m;
        switch (msg.c) {
            case "toggleMute":
                if (!ma) return fail("no mediaActions");
                ma.toggleSelfMute();
                return ok();
            case "toggleDeaf":
                if (!ma) return fail("no mediaActions");
                ma.toggleSelfDeaf();
                return ok();
            case "setMute": {
                if (!ma || !media) return fail("no mediaActions");
                if (!!media.isSelfMute() !== !!msg.value) ma.toggleSelfMute();
                return ok();
            }
            case "setDeaf": {
                if (!ma || !media) return fail("no mediaActions");
                if (!!media.isSelfDeaf() !== !!msg.value) ma.toggleSelfDeaf();
                return ok();
            }
            case "setVolume":
                if (!va) return fail("no volumeActions");
                va.setLocalVolume(String(msg.userId), Math.max(0, Math.min(200, Number(msg.volume))));
                this.scheduleVoice();
                return ok();
            case "leave": {
                const channels = this.m.channelActions;
                if (!channels || !channels.disconnect) return fail("no disconnect");
                channels.disconnect();
                return ok();
            }
            case "toggleLocalMute":
                if (!ma || !ma.toggleLocalMute) return fail("no toggleLocalMute");
                ma.toggleLocalMute(String(msg.userId));
                this.scheduleVoice();
                return ok();
            case "setInputVolume":
                if (!ma || !ma.setInputVolume) return fail("no setInputVolume");
                ma.setInputVolume(Math.max(0, Math.min(100, Number(msg.volume))));
                return ok();
            case "setOutputVolume":
                if (!ma || !ma.setOutputVolume) return fail("no setOutputVolume");
                ma.setOutputVolume(Math.max(0, Math.min(200, Number(msg.volume))));
                return ok();
            case "snapshot":
                this.lastVoiceKey = "";
                this.sendSelf();
                this.sendVoice(true);
                return ok();
            case "sniff":
                return fail("debug sniff not available");
            case "ping":
                return ok();
            default:
                return fail("unknown command");
        }
    }

    // ---------- данные ----------

    discordVersion() {
        const m = /discord\/([\d.]+)/i.exec(navigator.userAgent || "");
        return m ? m[1] : undefined;
    }

    // Остров рисует через WPF, который не читает WebP, поэтому всегда PNG: настоящий аватар или стандартный.
    avatarOf(user) {
        if (!user || !user.id) return null;
        if (user.avatar) return `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png?size=128`;
        let index = 0;
        try { index = Number((BigInt(user.id) >> 22n) % 6n); } catch (e) { /* стандартный номер 0 */ }
        return `https://cdn.discordapp.com/embed/avatars/${index}.png`;
    }

    nameOf(userId, guildId) {
        const { user, member } = this.m;
        let nick = null;
        try { nick = guildId && member ? member.getNick(guildId, userId) : null; } catch (e) { /* пусто */ }
        const u = user && user.getUser(userId);
        return nick || (u && (u.globalName || u.username)) || null;
    }

    sendSelf() {
        const me = this.m.user && this.m.user.getCurrentUser();
        if (!me) return;
        this.send({ t: "self", user: { id: me.id, name: me.globalName || me.username, avatar: this.avatarOf(me) } });
    }

    scheduleVoice() {
        if (this.voiceTimer) return;
        this.voiceTimer = true;
        this.later(() => {
            this.voiceTimer = false;
            this.guard("voice", () => this.sendVoice(false));
        }, 40);
    }

    voiceSnapshot() {
        const { selChannel, channel, guild, voice, media, user } = this.m;
        const selfMute = media && media.isSelfMute ? !!media.isSelfMute() : false;
        const selfDeaf = media && media.isSelfDeaf ? !!media.isSelfDeaf() : false;
        const chId = selChannel && selChannel.getVoiceChannelId && selChannel.getVoiceChannelId();
        if (!chId) return { t: "voice", channel: null, members: [], selfMute, selfDeaf };

        const ch = channel && channel.getChannel(chId);
        const g = ch && ch.guild_id && guild ? guild.getGuild(ch.guild_id) : null;
        const guildId = (ch && ch.guild_id) || null;
        const states = (voice && voice.getVoiceStatesForChannel && voice.getVoiceStatesForChannel(chId)) || {};
        const list = states instanceof Map ? Array.from(states.values()) : Object.values(states);
        const me = user && user.getCurrentUser();

        const members = list.map((s) => {
            const id = s.userId;
            const u = user && user.getUser(id);
            let volume = null, localMute = false;
            try { volume = media && media.getLocalVolume ? media.getLocalVolume(id) : null; } catch (e) { /* пусто */ }
            try { localMute = media && media.isLocalMute ? !!media.isLocalMute(id) : false; } catch (e) { /* пусто */ }
            return {
                id,
                name: this.nameOf(id, guildId) || "user",
                avatar: this.avatarOf(u),
                self: !!(me && me.id === id),
                selfMute: !!s.selfMute, selfDeaf: !!s.selfDeaf, mute: !!s.mute, deaf: !!s.deaf,
                stream: !!s.selfStream, video: !!s.selfVideo,
                speaking: (this.speaking.get(id) || 0) > 0,
                volume, localMute,
            };
        });
        return {
            t: "voice",
            channel: {
                id: chId, name: (ch && (ch.name || ch.rawRecipients && ch.rawRecipients.map((r) => r.username).join(", "))) || "",
                guildId, guildName: g ? g.name : null, type: ch ? ch.type : -1,
            },
            members, selfMute, selfDeaf,
        };
    }

    sendVoice(force) {
        const snap = this.voiceSnapshot();
        // ключ без поля speaking: речь идёт отдельными событиями
        const key = JSON.stringify(snap, (k, v) => (k === "speaking" ? undefined : v));
        if (!force && key === this.lastVoiceKey) return;
        this.lastVoiceKey = key;
        if (!snap.channel) this.speaking.clear();
        this.send(snap);
    }

    onSpeaking(a) {
        const id = a.userId;
        if (!id) return;
        const flags = Number(a.speakingFlags != null ? a.speakingFlags : (a.speaking ? 1 : 0)) || 0;
        const was = (this.speaking.get(id) || 0) > 0;
        if (flags > 0) this.speaking.set(id, flags); else this.speaking.delete(id);
        if (was !== flags > 0) this.send({ t: "speaking", userId: id, speaking: flags > 0, flags });
    }

    // ---------- сообщения ----------

    plain(text, guildId) {
        if (!text) return "";
        const { channel, guild, roles } = this.m;
        return text
            .replace(/\|\|[\s\S]*?\|\|/g, "[спойлер]")
            .replace(/<@!?(\d+)>/g, (_, id) => "@" + (this.nameOf(id, guildId) || "user"))
            .replace(/<@&(\d+)>/g, (_, id) => {
                let r = null;
                try { r = (roles && roles.getRole && roles.getRole(guildId, id)) || (guild && guild.getRole && guild.getRole(guildId, id)); } catch (e) { /* пусто */ }
                return "@" + ((r && r.name) || "роль");
            })
            .replace(/<#(\d+)>/g, (_, id) => {
                const c = channel && channel.getChannel(id);
                return "#" + ((c && c.name) || "канал");
            })
            .replace(/<a?:(\w+):\d+>/g, ":$1:")
            .slice(0, MAX_TEXT);
    }

    onMessage(a) {
        const m = a.message;
        if (!m || a.optimistic || a.isPushNotification) return;
        if (!MESSAGE_TYPES.has(m.type || 0)) return;
        if (m.flags && (m.flags & 4096)) return; // @silent

        const { user, channel, guild, member, rel, ugs } = this.m;
        const me = user && user.getCurrentUser();
        if (!me || !m.author || m.author.id === me.id) return;
        const ch = channel && channel.getChannel(a.channelId || m.channel_id);
        if (!ch) return;
        try { if (rel && rel.isBlocked && rel.isBlocked(m.author.id)) return; } catch (e) { /* пусто */ }

        const isDM = ch.type === 1 || ch.type === 3;
        const guildId = ch.guild_id || null;
        const direct = Array.isArray(m.mentions) && m.mentions.some((u) => (u && u.id || u) === me.id);
        const everyone = !!m.mention_everyone;
        let role = false;
        try {
            const mm = guildId && member && member.getMember(guildId, me.id);
            role = !!(mm && Array.isArray(m.mention_roles) && m.mention_roles.some((r) => mm.roles.includes(r)));
        } catch (e) { /* пусто */ }

        let muted = false;
        try {
            muted = !!(ugs && (isDM ? ugs.isChannelMuted(null, ch.id) : (ugs.isChannelMuted(guildId, ch.id) || ugs.isMuted(guildId))));
        } catch (e) { /* пусто */ }

        let reason = null;
        if (isDM) reason = "dm";
        else if (direct) reason = "mention";
        else if (role) reason = "role";
        else if (everyone) reason = "everyone";
        else if (this.api.settings.forward === "all") reason = "all";
        if (!reason) return;
        if (muted && reason !== "mention") return;

        const g = guildId && guild ? guild.getGuild(guildId) : null;
        const au = user.getUser(m.author.id) || m.author;
        const text = this.api.settings.includeText ? this.plain(m.content || "", guildId) : "";
        const sel = this.m.selChannel;
        this.send({
            t: "message",
            id: m.id, reason, muted,
            channelId: ch.id, guildId,
            channelName: isDM ? "" : (ch.name || ""), guildName: g ? g.name : null, isDM,
            author: {
                id: m.author.id,
                name: this.nameOf(m.author.id, guildId) || m.author.global_name || m.author.username || "user",
                avatar: this.avatarOf(au),
                bot: !!m.author.bot,
            },
            text,
            attachments: (m.attachments || []).slice(0, 8).map((x) => ({ name: x.filename, type: x.content_type || "" })),
            stickers: (m.sticker_items || []).length,
            embeds: (m.embeds || []).length,
            timestamp: m.timestamp,
            windowFocused: document.hasFocus(),
            currentChannelId: sel && sel.getChannelId ? sel.getChannelId() : null,
        });
    }

    guard(where, fn) {
        try { fn(); } catch (e) { this.err(where, e); }
    }

    err(where, error) {
        this.api.log(where, error);
    }
}
