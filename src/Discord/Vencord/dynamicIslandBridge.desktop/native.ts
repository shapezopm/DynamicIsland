import { ConnectSrc, CspPolicies } from "@main/csp";
import { IpcMainInvokeEvent } from "electron";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

// The island selects an available port and binds only to the IPv4 loopback address.
CspPolicies["ws://127.0.0.1:*"] = ConnectSrc;

export async function readBridgeConfig(_event: IpcMainInvokeEvent) {
    if (process.platform !== "win32" || !process.env.APPDATA) return null;

    try {
        const { port, token } = JSON.parse(await readFile(join(process.env.APPDATA, "DynamicIsland", "bridge.json"), "utf8"));
        if (!Number.isInteger(port) || port < 1 || port > 65535
            || typeof token !== "string" || !/^[\da-f]{64}$/i.test(token)) return null;
        return { port: port as number, token };
    } catch {
        return null;
    }
}
