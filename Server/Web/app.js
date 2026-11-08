"use strict";
const populationModes = ["performanceMode", "ghostBusterOnly", "chaosInsurgencyOnly", "whiteTuskOnly", "noISBMode", "noBlackDivMode"];
const questDogtags = new Set(["6963dede566c81e5c5dc9b4a", "6966abf2e6001880baf0ada0"]);
const $ = (s) => document.querySelector(s),
    clone = (x) => JSON.parse(JSON.stringify(x)),
    eq = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const state = {
    catalog: [],
    botTypes: [],
    dogtags: [],
    spawnMaps: spawnCatalogue(),
    view: null,
    current: null,
    original: null,
    data: null,
    fields: [],
    originalFields: [],
    token: null,
    tokenHeader: null,
    busy: false,
    saving: false,
    requestId: 0,
    blackDivisionInstalled: true,
    estimateSettings: {blackoutMinute: 10, powerMinute: 20, reinforcementMinute: 15, includeRestore: false},
};
const labels = {
    containementBreachQueue: "Spawn Limit",
    blackoutQueue: "Blackout Spawn Limit",
    maximumBots: "Population target",
    squadOverflow: "Extra slots for whole squads",
    reinforcementIntervalSeconds: "Normal phase duration (s)",
    reinforcementWindowSeconds: "PMC / Raider phase duration (s)",
    genericRefillDelaySeconds: "Generic refill delay (s)",
    normal: "Normal faction targets",
    reinforcement: "PMC / Raider faction targets",
    performanceMaximumBots: "Performance population target",
    performanceNormal: "Performance normal faction targets",
    performanceReinforcement: "Performance PMC / Raider faction targets",
    externalFactions: "External Factions",
    chaosInsurgency: "Chaos Insurgency",
    locations: "Maps & Formations",
    laboratory: "The Lab",
    sandbox: "Ground Zero",
    sandbox_high: "Ground Zero · high level",
    bigmap: "Customs",
    factory4_day: "Factory · day",
    factory4_night: "Factory · night",
    woods: "Woods",
    shoreline: "Shoreline",
    interchange: "Interchange",
    rezervbase: "Reserve",
    lighthouse: "Lighthouse",
    tarkovstreets: "Streets of Tarkov",
    labyrinth: "Labyrinth",
    icebreaker: "Icebreaker",
    suburbs: "Suburbs",
    patrol: "Patrols",
    hunt: "Hunts",
    checkpoint: "Checkpoints",
    formations: "Formations",
    followers: "Followers",
    boss: "Leader",
    enabled: "Enabled",
    chance: "Chance (%)",
    weight: "Selection Chance (Weight) ",
    squadCount: "Squad Count",
    zones: "Zones",
    raidTimeMin: "Minimum raid duration for the spawn to occur (s)",
    raidTimeMax: "Maximum raid duration for the spawn to occur (s)",
    min: "Minimum",
    max: "Maximum",
    type: "Type",
    id: "Identifier",
    name: "Name",
    diagnosticLogs: "Diagnostic logs",
    dayNight: "Day & night",
    revenge: "Retaliation",
    forcedHostility: "Hostility & allies",
    dogtags: "Dogtags",
    blackDivisionLoot: "Black Division loot",
    botPresets: "ISB presets",
    blackDivisionBotPresets: "Black Division presets",
    labsBlackoutEvent: "Blackout",
    labsContainmentBreachEvent: "Containment Breach · legacy",
    aishiLoyaltyCondition: "Aishi loyalty",
    debug: "Debug",
    useSainLayersChance: "Use SAIN Layers Chance",
    enableISBCustomBehavior: "Enable Custom ISB Behavior",
    enableBlackDivCustomBehavior: "Enable Custom Black Division Behavior",
    changeBlackDivBehaviorAllMaps: "Enable Custom Black Division Behavior in All Maps",
    collabLoadouts: "Collaboration loadouts",
    PackNStrapLoadouts: "PackNStrap belts",
    collabPresets: "Collaboration Presets",
    enableCollabLoadoutsAndAppearances: "Collaboration Loadouts & Appearances",
    enableCollabPresets: "Collaboration Presets",
    maxBotPresetBatch: "Bots per Batch",
    botHealth: "Bot Health",
    Head: "Head",
    Chest: "Thorax",
    Stomach: "Stomach",
    LeftArm: "Left arm",
    RightArm: "Right arm",
    LeftLeg: "Left leg",
    RightLeg: "Right leg",
    questForceConditions: "Quest conditions",
    officeAccess: "Office access",
    phases: "Event phases",
    cleanup: "Cleanup",
    extraction: "Extraction",
    health: "Health",
    damagePlayers: "Damage players",
    damageBots: "Damage bots",
    damagePerSecond: "Damage per second",
    protectiveMasks: "Protective masks",
    sain: "SAIN",
    logs: "Enable Logs",
    spawnAlways: "ISB Bots Always Spawning",
    spawnInstantlyAlways: "ISB Bots Spawning Instantly",
    spawnChance: "Spawn Chance (%)",
    PatrolDrawLimit: "Patrol Draw Limit",
    HuntDrawLimit: "Hunt Draw Limit",
    CheckpointDrawLimit: "Checkpoint Draw Limit",
    EnableISBPatrols: "Enable ISB Patrols",
    enableHunt: "Enable Hunts",
    enableISBHoldZones: "Enable Checkpoints",
    enableSecrets: "Enable secrets",
    Audio: "Audio",
    Logging: "Diagnostics",
    useChance: "Preset chance (%)",
    useChanceByWildType: "Chance by bot type",
    isbHealthMultiplier: "ISB Health Multiplier",
    blackDivisionHealthMultiplier: "Black Division Health Multiplier",
    chaosInsurgencyHealthMultiplier: "Chaos Insurgency Health Multiplier",
};
Object.assign(labels, {
    sandboxHigh: "Ground Zero · high level",
    tarkovStreets: "Streets of Tarkov",
    factory4Day: "Factory · day",
    factory4Night: "Factory · night",
    rezervBase: "Reserve",
    terminal: "Terminal",
    populationControl: "Population control",
    performanceMode: "Performance Mode",
    ghostBusterOnly: "Ghost Busters Only",
    chaosInsurgencyOnly: "Chaos Insurgency Only",
    whiteTuskOnly: "White Tusks Only",
    noISBMode: "No ISB Mode",
    noBlackDivMode: "No Black Division Mode",
    isbReinforcements: "ISB reinforcements",
    blackDivisionPocketLoot: "Black Division loot",
    mapLoot: "Map loot",
    anomalousItemSpawns: "Anomalous item spawns",
    chancePerBot: "Chance per bot (%)",
    chancePerPoint: "Anomaly chance per loose-loot point (%)",
    wildTypes: "Eligible bot types",
    containers: "Container search order",
    weights: "Item selection weights",
    disableFire: "Disable Fires Effects",
    disableSirens: "Disable Emergency Sirens",
    disableDestroyedVehicles: "Disable Destroyed Vehicles",
    automatic_doors: "Automatic doors",
    decontamination: "Decontamination",
    labs_announcements: "Lab announcements",
    pre_blackout_sequences: "Event sequences",
    takeover_fire: "Fires",
    takeover_infrastructure: "Infrastructure",
    takeover_props: "Scenery",
    takeover_sirens: "Sirens",
    terminal_files: "Terminal files",
});

Object.assign(labels, {
    labsVariantsEnabled: "Enable Labs Map Variants Selector",
    labsChaos: "Event reinforcements",
    patrols: "Patrols",
    checkpoints: "Checkpoints",
    hunts: "Hunts",
    isb: "ISB",
    blackDivision: "Black Division",
    pmcs: "PMCs",
    raiders: "Raiders",
    disableNormalISBSpawnsDuringEvent: "Replace Regular ISB Spawns",
    disableNormalBlackDivisionSpawnsDuringEvent: "Replace Regular Black Division Spawns",
    disableNormalPMCs: "Remove Regular PMCs During the Event",
    disableNormalRaiders: "Remove Regular Raiders During the Event",
});
Object.assign(labels, {
    containmentCaseSpawns: "Containment box spawns",
    groups: "Spawn groups",
    item: "Item template",
    count: "Boxes per group",
    positions: "Spawn coordinates",
    rotationX: "Rotation X",
    rotationY: "Rotation Y",
    rotationZ: "Rotation Z",
});
Object.assign(labels, {"containmentGasFadeOutSeconds": "Containment Gas Fade Out (s)", "containmentGasFadeInSeconds": "Containment Gas Fade In (s)", "containmentGasVolume": "Containment Gas Volume", "containmentGasAudio": "Containment Gas Audio", "activationLookSeconds": "Activation Look (s)", "activationBlackoutSeconds": "Activation Blackout (s)", "warningDelaySeconds": "Warning Delay (s)", "automaticContainmentChancePercent": "Automatic Containment Chance (%)", "automaticContainmentMinSeconds": "Automatic Containment Min (s)", "automaticContainmentMaxSeconds": "Automatic Containment Max (s)", "containmentSeconds": "Containment (s)", "containmentButton": "Containment Button", "containmentButtonRotation": "Containment Button Rotation", "chasesBeforeRetarget": "Chases Before Retarget", "chaseDurationSeconds": "Chase Duration (s)", "chaseRetrySeconds": "Chase Retry (s)", "chaseStepHeight": "Chase Step Height", "enabled": "Enabled", "chancePercent": "Chance (%)", "maps": "Maps", "spawnDelaySeconds": "Spawn Delay (s)", "initialMinDistance": "Initial Min Distance", "initialMaxDistance": "Initial Max Distance", "teleportTriggerDistance": "Teleport Trigger Distance", "teleportMinDistance": "Teleport Min Distance", "teleportMaxDistance": "Teleport Max Distance", "teleportCooldownSeconds": "Teleport Cooldown (s)", "killDistance": "Kill Distance", "killCooldownSeconds": "Kill Cooldown (s)", "unobservedGraceSeconds": "Unobserved Grace (s)", "afterTeleportGraceSeconds": "After Teleport Grace (s)", "moveSpeed": "Move Speed", "height": "Height", "bodyRadius": "Body Radius", "observationDistance": "Observation Distance", "botViewAngle": "Bot View Angle", "botsObserve": "Bots Observe", "killBots": "Kill Bots", "killPlayers": "Kill Players", "logicHz": "Logic (Hz)", "observationReportHz": "Observation Report (Hz)", "observerTimeoutSeconds": "Observer Timeout (s)", "pathRefreshSeconds": "Path Refresh (s)", "spawnAttempts": "Spawn Attempts", "moveAudio": "Move Audio", "teleportAudio": "Teleport Audio", "killAudio": "Kill Audio", "audioVolume": "Audio Volume", "audioMaxDistance": "Audio Max Distance", "moveAudioCooldownSeconds": "Move Audio Cooldown (s)", "allowClientDebug": "Allow Client Debug", "fixedSpawn": "Fixed Spawn", "fixedSpawnPoints": "Fixed Spawn Points", "audioFadeInSeconds": "Audio Fade In (s)", "audioFadeOutSeconds": "Audio Fade Out (s)", "spawnOnContainmentBreach": "Spawn On Containment Breach", "activateWhenSeen": "Activate When Seen", "periodicTeleport": "Periodic Teleport", "activationAudio": "Activation Audio", "moveAudioVariants": "Move Audio Variants", "teleportAudioVariants": "Teleport Audio Variants", "killAudioVariants": "Kill Audio Variants"});
const spawnField =
    /^(enabled|chance|weight|squadCount|raidTimeMin|raidTimeMax|min|max|spawnChance|patrolChance|huntChance|checkpointChance|patrolMinSize|patrolMaxSize|huntMinSize|huntMaxSize|holdZoneMinSize|holdZoneMaxSize|EnableISBPatrols|enableHunt|enableISBHoldZones|PatrolDrawLimit|HuntDrawLimit|CheckpointDrawLimit)$/;
function visible(path) {
    if (state.current?.document.relativePath === "nightmare_config.jsonc") return true;
    const keys = path.split("/").slice(1).map(unesc),
        last = keys[keys.length - 1];
    if (state.current?.document.relativePath === "config.jsonc") {
        if (keys[0] === "blackoutQueue") return keys.length >= 2 && !(keys.length === 2 && last === "enabled");
        if (keys[0] === "botHealth") return keys.length === 3;
        if (keys[0] === "externalFactions")
            return keys[1] === "chaosInsurgency" && (keys.length === 3 && last === "enabled" ||
                keys[2] === "locations" && !labsMap(keys[3]) &&
                !keys.some(k => ["secrets", "secret", "zones", "mapZones", "followers"].includes(k)) && spawnField.test(last));
        if (keys[0] === "labsBlackoutEvent") {
            if (keys.includes("externalFactions"))
                return state.view === "blackoutQueue" && keys.length === 5 && last === "enabled";
            return (
                !keys.some((k) => ["zones", "followers"].includes(k)) &&
                (spawnField.test(last) || last === "selectionMode" || last === "performanceMode")
            );
        }
        if (keys[0] === "locations")
            return (
                !keys.some((k) =>
                    ["secrets", "secret", "zones", "mapZones", "followers"].includes(k),
                ) && spawnField.test(last)
            );
        if (keys[0] === "debug")
            return keys.length === 2 && ["enableISBCustomBehavior", "enableBlackDivCustomBehavior", "changeBlackDivBehaviorAllMaps", "useSainLayersChance", "logs", "spawnAlways", "spawnInstantlyAlways"].includes(last);
        if (keys[0] === "dayNight")
            return [
                "ForceDayAppearance",
                "ForceNightAppearance",
                "nightStartHour",
                "nightEndHour",
            ].includes(last);

        if (keys[0] === "dogtags")
            return (
                (keys.length === 2 && ["enabled", "populateIdentityData"].includes(last)) ||
                (keys.length === 3 && keys[1] === "dogtagTplByWildType")
            );
        if (keys[0] === "aishiLoyaltyCondition")
            return (
                keys.length === 2 &&
                [
                    "enabled",
                    "requiredItemTpl",
                    "loseReputationOnKill",
                    "reputationLostPerKill",
                ].includes(last)
            );
        return (
            keys.length === 1 &&
            [
                "enableCollabLoadoutsAndAppearances",
                "enableCollabPresets",
                "maxBotPresetBatch",
            ].includes(last)
        );
    }
    if (keys.length === 1 && populationModes.includes(last)) return state.view === "populationControl";
    if (keys[0] === "containmentCaseSpawns") return keys.length > 1;
    if (["revenge", "officeAccess", "anomalousItemSpawns"].includes(keys[0])) return false;
    if (keys.length === 1)
        return [
            "enabled",
            "chance",
            "isbHealthMultiplier",
            "blackDivisionHealthMultiplier",
            "chaosInsurgencyHealthMultiplier",
            "disableFire",
            "disableSirens",
            "disableDestroyedVehicles",
            "disableNormalISBSpawnsDuringEvent",
            "disableNormalBlackDivisionSpawnsDuringEvent",
            "labsVariantsEnabled",
        ].includes(last);
    if (keys[0] === "populationControl") return keys.length === 2;
    if (keys[0] === "containementBreachQueue") return keys.length >= 2 && !(keys.length === 2 && last === "enabled");
    if (keys[0] === "externalFactions") return state.view === "containementBreachQueue" && keys.length === 3 && last === "enabled";

    if (
        ["isb", "blackDivision", "pmcs", "raiders", "labsChaos", "isbReinforcements", "externalFactions"].includes(
            keys[0],
        )
    )
        return !keys.includes("followers") && (spawnField.test(last) || last === "selectionMode");
    return false;
}
function formationPath(path) {
    return (
        (/^\/labsBlackoutEvent\//.test(path) &&
            /\/(patrols|checkpoints|hunts|formations)$/.test(path)) ||
        /^\/locations\/[^/]+\/(patrol|hunt|checkpoint)\/formations$/.test(path) ||
        /^\/externalFactions\/chaosInsurgency\/locations\/[^/]+\/(patrol|hunt|checkpoint)\/formations$/.test(path) ||
        (/^\/(isb|blackDivision|pmcs|raiders|labsChaos|isbReinforcements|externalFactions)\//.test(path) &&
            /\/(formations|patrols|checkpoints|hunts)$/.test(path))
    );
}
function specialFormationPath(path) {
    const file = state.current?.document.relativePath;
    return file === "config.jsonc" ? path.startsWith("/labsBlackoutEvent/") :
        file === "containment_breach_config.jsonc" && /^\/(isb|blackDivision|pmcs|raiders|labsChaos|isbReinforcements|externalFactions)(\/|$)/.test(path);
}
function specialRole(value) {
    const role = String(value || "").trim().toLowerCase();
    return /^(isbbossfirefly|isbfollowervipper|isbfollowerloghan|isbfollowerspearthrower|isbfollowerzofia|isbfollowerevernight|isbfollowernemesis|blackdivib|blackdivassaultghostbuster|blackdivghostbusterlead|blackdivghostbuster0[1-4]|bosswedge|bosswedgeghostbuster|1370[2-6]|13715|13716|848424|848426)$/.test(role);
}
function specialFormation(value) {
    return !!value && (specialRole(value.boss) || value.followers?.some(f => f && Math.max(Number(f.min) || 0, Number(f.max) || 0) > 0 && specialRole(f.type)) === true);
}
function specialLeader(path) {
    if (!specialFormationPath(path)) return undefined;
    if (path.includes("/externalFactions/") || /\/(pmcs|raiders)(\/|$)/.test(path)) return null;
    return /\/blackDivision(\/|$)/.test(path) ? "blackDivGhostBusterLead" : "ISBBossFirefly";
}
function hasVisible(value, path) {
    if (path.includes("/externalFactions") && state.view !== "externalFactions") return false;
    if (path.startsWith("/containmentCaseSpawns")) return true;
    if (formationPath(path)) return true;
    return Object.entries(value).some(([k, v]) =>
        v && typeof v === "object"
            ? hasVisible(v, path + "/" + esc(k))
            : visible(path + "/" + esc(k)),
    );
}

Object.assign(labels, {
    blackDivAssault: "Black Division · Operative",
    blackDivLead: "Black Division · Lead (Unused)",
    blackDivBreacher: "Black Division · Breacher (Unused)",
    blackDivSupport: "Black Division · Support (Unused)",
    bossWedge: "Black Division · Wedge",
    blackDivIb: "Black Division · Icebreaker",
    blackDivGhostBusterLead: "Black Division · Ghost Buster Lead",
    blackDivGhostBuster01: "Black Division · Ghost Buster 01",
    blackDivGhostBuster02: "Black Division · Ghost Buster 02",
    blackDivGhostBuster03: "Black Division · Ghost Buster 03",
    blackDivGhostBuster04: "Black Division · Ghost Buster 04",
    bossWedgeGhostBuster: "Black Division · Wedge Ghost Buster",
    pmcUSEC: "PMC · USEC",
    pmcBEAR: "PMC · BEAR",
    pmcBot: "Raider",
});
const label = (k) =>
    labels[k] ||
    labels[Object.keys(labels).find((x) => x.toLowerCase() === String(k).toLowerCase())] ||
    String(k)
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/[_-]/g, " ")
        .replace(/^./, (x) => x.toUpperCase());
const esc = (k) => String(k).replace(/~/g, "~0").replace(/\//g, "~1"),
    unesc = (k) => k.replace(/~1/g, "/").replace(/~0/g, "~");
function at(path) {
    return path
        .split("/")
        .slice(1)
        .map(unesc)
        .reduce((v, k) => v[k], state.data);
}
function set(path, value) {
    const keys = path.split("/").slice(1).map(unesc),
        last = keys.pop();
    let root = state.data;
    for (const key of keys) root = root[key];
    root[last] = value;
    dirty();
}
function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
}
function message(text, error = false) {
    const e = $("#message");
    e.hidden = !text;
    e.className = error ? "error" : "";
    e.textContent = text;
}
async function api(url, options = {}) {
    const res = await fetch("/isb/api/" + url, {
        ...options,
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", ...options.headers },
    });
    const type = res.headers.get("content-type") || "";
    if (!type.includes("json"))
        throw new Error(
            res.status >= 500
                ? "The server could not complete this request. Check the server console."
                : "Your SPT session expired or administrator access is required. Open the SPT dashboard, sign in and try again.",
        );
    const data = await res.json();
    if (!res.ok) {
        const error = new Error(data.message || data.title || "The request could not be completed.");
        error.status = res.status;
        throw error;
    }
    return data;
}
function diff(a, b, path = "", out = []) {
    if (eq(a, b)) return out;
    if (
        a === null ||
        b === null ||
        typeof a !== "object" ||
        typeof b !== "object" ||
        Array.isArray(a) !== Array.isArray(b)
    ) {
        out.push({ path, value: b });
        return out;
    }
    if (Array.isArray(a)) {
        if (a.length !== b.length) {
            out.push({ path, value: b });
            return out;
        }
        a.forEach((v, i) => diff(v, b[i], path + "/" + i, out));
        return out;
    }
    if (!eq(Object.keys(a).sort(), Object.keys(b).sort())) {
        out.push({ path, value: b });
        return out;
    }
    for (const k of Object.keys(a)) diff(a[k], b[k], path + "/" + esc(k), out);
    return out;
}
function changes() {
    if (!state.current) return [];
    if (state.current.document.format === "cfg")
        return state.fields
            .filter((f, i) => f.value !== state.originalFields[i].value)
            .map((f) => ({ path: f.path, value: f.value }));
    return diff(state.original, state.data);
}
function populationQueueError(value, path) {
    const prefix = path.slice(1).replaceAll("/", ".");
    if (!value || typeof value !== "object" || Array.isArray(value)) return `${prefix}: expected a spawn limit object.`;
    const blackout = path.endsWith("/blackoutQueue");
    const defaults = {maximumBots: 18, squadOverflow: 0, reinforcementIntervalSeconds: 300, reinforcementWindowSeconds: 120, genericRefillDelaySeconds: 15, performanceMaximumBots: 13};
    const number = key => Object.hasOwn(value, key) ? value[key] : defaults[key];
    const check = (key, minimum, maximum) => {
        const n = number(key);
        return Number.isInteger(n) && n >= minimum && n <= maximum ? "" : `${prefix}.${key} = ${n}: use a whole number between ${minimum} and ${maximum}.`;
    };
    for (const [key, minimum, maximum] of [["maximumBots", 1, 100], ["squadOverflow", 0, 10], ["reinforcementIntervalSeconds", 60, 3600], ["reinforcementWindowSeconds", 1, 3600], ["genericRefillDelaySeconds", 1, 600], ["performanceMaximumBots", 1, number("maximumBots")]]) {
        const error = check(key, minimum, maximum);
        if (error) return error;
    }
    const base = {blackDivision: 11, isb: 4, chaosInsurgency: 3, raiders: 0, pmcs: 0};
    const groups = blackout ? {
        normal: {blackDivision: 8, isb: 4, chaosInsurgency: 0, raiders: 0, pmcs: 0},
        reinforcement: {blackDivision: 4, isb: 2, chaosInsurgency: 0, raiders: 3, pmcs: 6},
        performanceNormal: {blackDivision: 6, isb: 2, chaosInsurgency: 0, raiders: 0, pmcs: 0},
        performanceReinforcement: {blackDivision: 3, isb: 2, chaosInsurgency: 0, raiders: 2, pmcs: 4},
    } : {normal: {...base}, reinforcement: {...base, blackDivision: 7, isb: 0, raiders: 2, pmcs: 6}, performanceNormal: {...base, blackDivision: 6}, performanceReinforcement: {...base, blackDivision: 2, isb: 0, raiders: 4, pmcs: 4}};
    for (const [key, fallback] of Object.entries(groups)) {
        const targets = Object.hasOwn(value, key) ? value[key] : fallback;
        if (!targets || typeof targets !== "object" || Array.isArray(targets)) return `${prefix}.${key}: expected an object with faction targets.`;
        const limit = number(key.startsWith("performance") ? "performanceMaximumBots" : "maximumBots");
        let total = 0;
        for (const [faction, standard] of Object.entries(base)) {
            const count = Object.hasOwn(targets, faction) ? targets[faction] : standard;
            if (!Number.isInteger(count) || count < 0 || count > limit) return `${prefix}.${key}.${faction} = ${count}: use a whole number between 0 and ${limit}.`;
            total += count;
        }
        if (total > limit) return `${prefix}.${key}: faction targets total ${total}, but the population limit is ${limit}.`;
    }
    return "";
}
function formationError(value, path = "") {
    if (/\/(containementBreachQueue|blackoutQueue)$/.test(path)) return populationQueueError(value, path);
    if (!value || typeof value !== "object") return "";
    if (state.current?.document.relativePath === "config.jsonc" && path === "/externalFactions/chaosInsurgency/locations" && Object.keys(value).some(labsMap))
        return "External Factions formations are available outside The Lab only. Configure Labs squads in Spawn Limit.";
    if (path.startsWith("/externalFactions/chaosInsurgency/locations/") && typeof value.boss === "string" &&
        (!/^(chaosInsurgency|13101)$/i.test(value.boss) || value.followers?.some(f => !/^(chaosInsurgency|13101)$/i.test(f.type))))
        return "External Factions formations must use Chaos Insurgency leaders and followers.";
    if (Array.isArray(value) && formationPath(path) && specialFormationPath(path) && value.some(f => !specialFormation(f)))
        return "Event formation lists accept special squads only. Configure common squads in Spawn Limit.";
    if (Array.isArray(value.followers)) {
        for (const f of value.followers)
            if (!Number.isInteger(f.min) || !Number.isInteger(f.max) || f.min < 0 || f.max < f.min)
                return `Invalid follower range in ${value.id || path}: ${botName(f.type)} (${f.min}–${f.max}). Maximum must be at least the minimum.`;
    }
    if (typeof value.raidTimeMin === "number" && typeof value.raidTimeMax === "number" && value.raidTimeMin > value.raidTimeMax)
        return `Invalid spawn time range in ${value.id || path}. Maximum must be at least the minimum.`;
    for (const [key, child] of Object.entries(value)) {
        const error = formationError(child, path + "/" + key);
        if (error) return error;
    }
    return "";
}
function dirty() {
    updateOverview();
    $("#content").inert = state.busy;
    const n = changes().length, error = formationError(state.data),
        invalid = !!$("#content").querySelector("input:invalid") || !!error;
    $("#dirty").textContent = invalid
        ? error || "Check the highlighted fields"
        : n
          ? n + (n === 1 ? " unsaved change" : " unsaved changes")
          : "All changes saved";
    $("#dirty").title = error;
    $("#save").disabled = state.busy || !n || invalid;
    $("#undo").disabled = state.busy || (!n && !invalid);
    $("#reset-defaults").disabled = state.busy || !state.current;
}
async function confirmDiscard() {
    if (!changes().length) return true;
    const d = $("#confirm");
    d.showModal();
    return new Promise((resolve) => {
        let settled = false;
        const done = (v) => {
            if (settled) return;
            settled = true;
            d.close();
            resolve(v);
        };
        $("#stay").onclick = () => done(false);
        $("#discard").onclick = () => done(true);
        d.oncancel = (e) => {
            e.preventDefault();
            done(false);
        };
    });
}
const blackoutTabs = [
    ["blackoutQueue", "Blackout Spawn Limit"],
    ["labsBlackoutEvent", "Blackout formations"],
];
const mainTabs = [
    ["locations", "Maps & Formations"],
    ["externalFactions", "External Factions"],
    ["botHealth", "Bot Health"],
    ["__general", "Essentials & Debug"],
    ["dayNight", "Day & night"],
    ["aishiLoyaltyCondition", "Aishi loyalty"],
    ["dogtags", "Dogtags"],
];
const eventTabs = [
    ["__general", "Event & Labs variants"],
    ["populationControl", "Map population"],
    ["containementBreachQueue", "Spawn Limit"],
    ["isb", "ISB formations"],
    ["blackDivision", "Black Division formations"],
    ["isbReinforcements", "Event reinforcements"],
];
function tabs(d) {
    if (d.relativePath === "nightmare_config.jsonc") return [["__general", "Behavior & audio"], ["containmentButton", "Containment button position"], ["containmentButtonRotation", "Containment button rotation"], ["maps", "Enabled maps"], ["fixedSpawnPoints", "Fixed spawn points"], ["moveAudioVariants", "Movement sounds"], ["teleportAudioVariants", "Teleport sounds"], ["killAudioVariants", "Kill sounds"]];
    if (d.relativePath === "config.jsonc") return [...mainTabs, ...blackoutTabs];
    if (d.format === "cfg")
        return [
            ["Audio", "Audio & Communications"],
            ["__blackout", "The Blackout"],
            ["__event", "The Containement Breach"],
        ];
    return eventTabs;
}
function drawDocs() {
    const nav = $("#documents");
    nav.replaceChildren();
    const q = $("#search").value.toLocaleLowerCase();
    const groups = state.catalog.flatMap(d => d.relativePath === "config.jsonc"
        ? [{document: d, title: d.name, entries: mainTabs}, {document: d, title: "Blackout", entries: blackoutTabs}]
        : [{document: d, title: d.name, entries: tabs(d)}]);
    for (const {document: d, title, entries: available} of groups) {
        const entries = available.filter(
            ([, name]) => !q || `${name} ${title} ${d.name}`.toLocaleLowerCase().includes(q),
        );
        if (!entries.length) continue;
        const group = el("section", "doc-group");
        group.append(el("div", "group-title", title.toLocaleUpperCase()));
        for (const [section, name] of entries) {
            const b = el(
                "button",
                "doc" +
                    (state.current?.document.id === d.id && state.view === section
                        ? " active"
                        : ""),
                name,
            );
            b.onclick = () => openDoc(d.id, false, section);
            if (b.classList.contains("active")) b.setAttribute("aria-current", "page");
            group.append(b);
        }
        nav.append(group);
    }
}

async function openDoc(id, force = false, view = null) {
    if (state.saving) return;
    if (!force && state.current?.document.id === id) {
        state.view = view || state.view;
        $("#section").value = state.view;
        maps();
        setHeading();
        drawDocs();
        render();
        dirty();
        return;
    }
    if (!force && !(await confirmDiscard())) return;
    const requestId = ++state.requestId;
    state.busy = true;
    dirty();
    message("");
    try {
        const data = await api("document/" + id);
        if (requestId !== state.requestId) return;
        state.current = data;
        state.original = clone(data.data);
        state.data = clone(data.data);
        state.fields = Array.isArray(data.fields)
            ? clone(data.fields).filter((f) => f.section !== "Logging")
            : [];
        state.originalFields = clone(state.fields);
        $("#intro").hidden = true;
        $("#title").textContent = data.document.name;
        $("#category").textContent = "ISB / " + data.document.category.toUpperCase();
        $("#path").textContent = "Your operation, configured";
        $("#apply").textContent = data.document.apply;
        $("#apply").hidden = false;
        $("#toolbar").hidden = false;
        $("#filter").value = "";
        sections();
        state.view = view || tabs(data.document)[0][0];
        $("#section").value = state.view;
        maps();
        setHeading();
        drawDocs();
        render();
    } catch (e) {
        if (requestId === state.requestId) message(e.message, true);
    } finally {
        if (requestId === state.requestId) {
            state.busy = false;
            dirty();
        }
    }
}
function setHeading() {
    const document = state.current.document;
    const current = tabs(document).find(([key]) => key === state.view);
    const blackout = document.relativePath === "config.jsonc" && blackoutTabs.some(([key]) => key === state.view);
    $("#title").textContent = current?.[1] || state.current.document.name;
    $("#category").textContent = "ISB / " + (blackout ? "EVENTS" : document.category.toUpperCase());
    $("#path").textContent = blackout ? "Blackout" : document.name;
}
function options(select, items) {
    select.replaceChildren();
    for (const [value, text] of items) {
        const o = el("option", "", text);
        o.value = value;
        select.append(o);
    }
}
function sections() {
    if (state.current.document.format === "cfg") {
        options($("#section"), tabs(state.current.document));
        $("#mapLabel").hidden = true;
        return;
    }
    const root = state.data;
    const entries = Object.entries(root);
    const allowed = new Set(tabs(state.current.document).map(([key]) => key));
    const structured = entries
        .filter(
            ([k, v]) => allowed.has(k) && v && typeof v === "object" && hasVisible(v, "/" + esc(k)),
        )
        .map(([k]) => [k, label(k)]);
    if (state.current.document.relativePath === "config.jsonc" && !structured.some(([key]) => key === "externalFactions"))
        structured.push(["externalFactions", label("externalFactions")]);
    options($("#section"), [["__general", "General"], ...structured]);
    if (Object.hasOwn(root, "locations")) $("#section").value = "locations";
    maps();
}
function labsMap(value) {
    const key = String(value || "").toLowerCase().replace(/[^a-z0-9]/g, "");
    return key.startsWith("laboratory") || ["lab", "labs", "thelab", "blackoutlabs", "containmentbreachlabs", "containementbreachlabs", "labsblackout", "labscontainmentbreach", "labscontainementbreach", "5b0fc42d86f7744a585f9105"].includes(key);
}
function mapFormationsView() {
    return state.current?.document.relativePath === "config.jsonc" && ["locations", "externalFactions"].includes(state.view);
}
function mapLocations() {
    return state.view === "externalFactions" ? state.data.externalFactions?.chaosInsurgency?.locations || {} : state.data.locations || {};
}
function ensureExternalFactions() {
    if (state.data.externalFactions?.chaosInsurgency?.locations) return;
    const fallback = () => ({
        patrol: {EnableISBPatrols: false, spawnChance: 0, PatrolDrawLimit: 1, formations: []},
        checkpoint: {enableISBHoldZones: false, spawnChance: 0, CheckpointDrawLimit: 1, formations: []},
        hunt: {enableHunt: false, spawnChance: 0, HuntDrawLimit: 1, formations: []},
    });
    const defaults = state.current.defaults?.externalFactions?.chaosInsurgency || {};
    state.data.externalFactions ||= {};
    state.data.externalFactions.chaosInsurgency = {
        enabled: false,
        ...clone(defaults),
        ...state.data.externalFactions.chaosInsurgency,
        locations: clone(defaults.locations || Object.fromEntries(Object.keys(state.data.locations || {}).filter(k => !labsMap(k)).map(k => [k, fallback()]))),
    };
}
function maps() {
    const previous = $("#map").value;
    const visible =
        state.current?.document.format === "json" && ["locations", "externalFactions"].includes($("#section").value) && state.current.document.relativePath === "config.jsonc";
    $("#mapLabel").hidden = !visible;
    if (visible) {
        if ($("#section").value === "externalFactions") ensureExternalFactions();
        options(
            $("#map"),
            Object.keys(mapLocations()).filter(k => state.view !== "externalFactions" || !labsMap(k)).map((k) => [k, label(k)]),
        );
        if (Object.hasOwn(mapLocations(), previous) && (state.view !== "externalFactions" || !labsMap(previous))) $("#map").value = previous;
    }
}
function selected() {
    const s = $("#section").value;
    if (s === "__general")
        return {
            value: Object.fromEntries(
                Object.entries(state.data).filter(([, v]) => v === null || typeof v !== "object"),
            ),
            path: "",
        };
    if (s === "locations") {
        const m = $("#map").value;
        return { value: state.data.locations[m], path: "/locations/" + esc(m) };
    }
    if (s === "externalFactions" && state.current.document.relativePath === "config.jsonc") {
        const m = $("#map").value;
        return {value: mapLocations()[m], path: "/externalFactions/chaosInsurgency/locations/" + esc(m)};
    }
    return { value: state.data[s], path: "/" + esc(s) };
}
function hint(path, key) {
    return (
        {
            useSainLayersChance: "Chance from 0 to 100, rolled once per squad using an enabled custom brain. Selected squads use SAIN combat and retain their patrol, hunt and checkpoint tasks. Requires SAIN. Restart clients and headless after changing this setting.",
            enableBlackDivCustomBehavior: "Only Applies to the Containement Breach Event.",
            chancePerBot:
                "One roll per eligible bot during the Labs event. A success selects one item using the weights. Existing loot is preserved.",
            chancePerPoint:
                "Chance for an anomaly at each normal loose-loot point chosen by the game; 100 is for testing. Containers and forced quest spawns are unchanged.",
            containers:
                "Searches these containers in order for free space. Adds at most one anomalous item; never removes preset loot.",
            maxBotPresetBatch:
                "Maximum bots loadouts prepared in one server batch. ",
            weight: "Relative probability of selecting this formation during the spawn draw.",
            spawnChance: "Spawn probability for this category, from 0 to 100. Formation weights only select the squad composition.",
            chance: "Probability from 0 to 100. ",
            raidTimeMin: "Use -1 for a instant spawn.",
            raidTimeMax: "Use -1 for a instant spawn.",
            labsVariantsEnabled:
                "Unlocks the variant selector after earning Ms. VICTORIA by completing the entire Containment Breach questline, accepting quests failed through Pick a Side.",
        }[key] || ""
    );
}
function field(key, value, path, meta) {
    if (path.startsWith("/anomalousItemSpawns/botLoot/wildTypes/"))
        return botSelect("Bot type", value, path);
    if (!meta && !visible(path)) return null;
    if (path.startsWith("/dogtags/dogtagTplByWildType/")) return dogtagField(key, value, path);
    const q = $("#filter").value.toLowerCase();
    const title = path.startsWith("/externalFactions/chaosInsurgency/locations/") && key === "EnableISBPatrols" ? "Enable Patrols" :
        path.startsWith("/externalFactions/chaosInsurgency/locations/") && key === "enableISBHoldZones" ? "Enable Checkpoints" : label(key);
    const description = friendlyHints[key] || hint(path, key);
    if (q && !`${key} ${title} ${description} ${path}`.toLowerCase().includes(q)) return null;
    const f = el("div", "field"),
        l = el("label"),
        name = el("span", "", title);
    l.append(name);
    let input;
    const nullable =
        !meta &&
        (value === null ||
            (state.current.document.category === "SAIN" && path.startsWith("/difficulties/")));
    const bool = !nullable && (meta ? meta.type === "Boolean" : typeof value === "boolean");
    if (bool) {
        input = el("input");
        input.type = "checkbox";
        input.checked = meta ? String(value).toLowerCase() === "true" : value;
    } else if (key === "selectionMode") {
        input = el("select");
        options(input, [
            ["isb", "ISB"],
            ["isbOnly", "ISB only"],
            ["blackDivision", "Black Division"],
            ["blackDivisionOnly", "Black Division only"],
            ["both", "ISB & Black Division"],
            ["preferBlackDivision", "Prefer Black Division"],
        ]);
        input.value = value;
    } else if (meta?.choices?.length) {
        input = el("select");
        options(
            input,
            meta.choices.map((x) => [x, x]),
        );
        input.value = value;
    } else {
        input = el("input");
        const numeric =
            !nullable &&
            (meta ? ["Single", "Double", "Int32"].includes(meta.type) : typeof value === "number");
        input.type = numeric ? "number" : "text";
        if (numeric) input.required = true;
        if (numeric) input.step = meta?.type === "Int32" ? "1" : "any";
        input.value =
            value === null
                ? ""
                : nullable
                  ? JSON.stringify(value)
                  : numeric && !meta
                    ? Number(Number(value).toPrecision(7))
                    : value;
        if (nullable) input.placeholder = "Default";
        if (meta?.min != null) input.min = meta.min;
        if (meta?.max != null) input.max = meta.max;
        const bounds = meta?.type === "Int32" ? [-2147483648, 2147483647]
            : meta?.type === "Single" ? [-3.4028234663852886e38, 3.4028234663852886e38] : null;
        if (bounds) {
            input.min = String(Math.max(meta.min == null ? bounds[0] : Number(meta.min), bounds[0]));
            input.max = String(Math.min(meta.max == null ? bounds[1] : Number(meta.max), bounds[1]));
        }
        if (/chance$/i.test(key) || ["chancePerPoint", "chancePerBot"].includes(key)) {
            input.min = "0";
            input.max = "100";
        }
        if (key === "maxBotPresetBatch") {
            input.min = "1";
            input.max = "100";
            input.step = "1";
        }
        if (path.startsWith("/botHealth/")) { input.min = "1"; input.max = "100000"; input.step = "1"; }
        if (/\/(containementBreachQueue|blackoutQueue)\//.test(path)) {
            const bounds = {maximumBots: [1, 100], squadOverflow: [0, 10], reinforcementIntervalSeconds: [60, 3600], reinforcementWindowSeconds: [1, 3600], genericRefillDelaySeconds: [1, 600], performanceMaximumBots: [1, 100]};
            const range = bounds[key] || [0, 100];
            input.min = String(range[0]); input.max = String(range[1]); input.step = "1";
        }
    }
    if (key === "requiredItemTpl") {
        input.pattern = "[a-fA-F0-9]{24}|^$";
        input.title =
            "An item template ID: 24 hexadecimal characters, or empty to disable the item condition.";
    }
    input.setAttribute("aria-label", title);
    input.addEventListener("input", () => {
        let custom;
        if (nullable) {
            try {
                custom = input.value.trim() === "" ? null : JSON.parse(input.value);
                input.setCustomValidity("");
            } catch {
                input.setCustomValidity(
                    "Enter a number, true/false or a JSON value. Leave blank to use the default.",
                );
            }
        }
        if (!input.checkValidity()) {
            dirty();
            return;
        }
        let v = nullable
            ? custom
            : bool
              ? input.checked
              : input.type === "number"
                ? Number(input.value)
                : input.value;
        if (input.type === "number" && (input.value === "" || !Number.isFinite(v))) return;
        if (meta) meta.value = String(v);
        else set(path, v);
        f.classList.add("changed");
        dirty();
    });
    l.append(input);
    f.append(l);
    if (nullable)
        f.append(
            el(
                "p",
                "hint",
                "Leave blank to use the default. Accepts a number, true/false or [minimum,maximum].",
            ),
        );
    if (description) f.append(el("p", "hint", description));
    return f;
}
function object(value, path, depth = 0) {
    const box = el("div"),
        fields = el("div", "fields");
    const entries = Object.entries(value);
    if (/^\/(locations\/[^/]+|externalFactions\/chaosInsurgency\/locations\/[^/]+)\/(patrol|hunt|checkpoint)$/.test(path)) {
        const rank = key => key.endsWith("DrawLimit") ? 0 : ["EnableISBPatrols", "enableHunt", "enableISBHoldZones"].includes(key) ? 1 : key === "spawnChance" ? 2 : 3;
        entries.sort(([a], [b]) => rank(a) - rank(b));
    }
    for (const [key, v] of entries) {
        if (v === null || typeof v !== "object") {
            const f = field(key, v, path + "/" + esc(key));
            if (f) fields.append(f);
        }
    }
    if (fields.children.length) box.append(fields);
    for (const [key, v] of Object.entries(value)) {
        if (v === null || typeof v !== "object") continue;
        const child = section(v, path + "/" + esc(key), label(key), depth);
        if (child) box.append(child);
    }
    return box;
}
function template(path, list) {
    if (path === "/containmentCaseSpawns/groups") {
        let n = 1;
        while (list.some((g) => g.id === "ContainmentBoxes" + n)) n++;
        return {
            id: "ContainmentBoxes" + n,
            enabled: false,
            item: "c5c72a7a3bf1f02421a145bb",
            count: 1,
            chance: 100,
            positions: [],
        };
    }
    if (/^\/containmentCaseSpawns\/groups\/\d+\/positions$/.test(path))
        return { x: 0, y: 0, z: 0, rotationX: 0, rotationY: 0, rotationZ: 0 };
    if (list.length) return clone(list[list.length - 1]);
    const key = path.split("/").pop();
    if (path === "/anomalousItemSpawns/botLoot/wildTypes") return "ISBSpecialForces";
    if (path === "/anomalousItemSpawns/botLoot/containers") return "Pockets";
    if (key === "formations")
        return {
            id: "NewFormation",
            enabled: false,
            weight: 1,
            squadCount: 1,
            boss: "ISBSpecialForces",
            followers: [],
            zones: [],
            raidTimeMin: -1,
            raidTimeMax: -1,
        };
    if (key === "followers") return { type: "ISBSpecialForces", min: 1, max: 1 };
    return "";
}
function matchesSection(value, path) {
    const q = $("#filter").value.toLowerCase();
    if (!q) return hasVisible(value, path);
    for (const [key, v] of Object.entries(value)) {
        const p = path + "/" + esc(key);
        if (v && typeof v === "object") {
            if (matchesSection(v, p)) return true;
        } else if (
            visible(p) &&
            (key + " " + label(key) + " " + hint(p, key) + " " + p).toLowerCase().includes(q)
        )
            return true;
    }
    return false;
}
function section(value, path, title, depth) {
    if (formationPath(path)) return formationList(value, path, depth, title);
    if (!hasVisible(value, path) || !matchesSection(value, path)) return null;
    const details = el("details", "card");
    details.dataset.path = path;
    details.open = depth < 1 || !!$("#filter").value;
    details.append(el("summary", "", title + (Array.isArray(value) ? " · " + value.length : "")));
    const body = el("div", "body");
    if (Array.isArray(value)) {
        for (let i = 0; i < value.length; i++) {
            const v = value[i],
                entry = el("div", "array-item"),
                head = el("div", "array-head");
            head.append(
                el(
                    "strong",
                    "",
                    `${i + 1}. ${typeof v === "object" && v !== null ? v.name || v.id || v.type || title : title}`,
                ),
            );
            const del = el("button", "small danger", "Remove");
            if (
                path.startsWith("/anomalousItemSpawns/") ||
                path.startsWith("/containmentCaseSpawns/")
            )
                head.append(del);
            del.onclick = () => {
                const list = clone(at(path));
                list.splice(i, 1);
                set(path, list);
                render();
            };
            entry.append(head);
            if (v && typeof v === "object") entry.append(object(v, path + "/" + i, depth + 1));
            else {
                const f = field(String(i), v, path + "/" + i);
                if (f) entry.append(f);
            }
            body.append(entry);
        }
        const add = el("button", "small", "+ Add");
        add.onclick = () => {
            const list = clone(at(path));
            list.push(template(path, list));
            set(path, list);
            render();
        };
        if (path.startsWith("/anomalousItemSpawns/") || path.startsWith("/containmentCaseSpawns/"))
            body.append(add);
        if (!value.length) body.prepend(el("p", "hint", "No entries configured."));
    } else body.append(object(value, path, depth + 1));
    details.append(body);
    return details;
}
function botName(value) {
    return (
        state.botTypes.find((b) => b.value.toLowerCase() === String(value).toLowerCase())?.label ||
        label(value)
    );
}
Object.assign(labels, {
    "b27508815b9307ccd1bffa36": "Anomalous injector ANM-01 — Aegis",
    "1800c994e11cfa35b423e8ed": "Anomalous injector ANM-02 — Inertia",
    "6183f2a51cb013b5ab62a796": "Sheef — Verdigris",
    "fac4f1a658ad562cddde72c6": "Weightless Iron",
    "72eb2bb2416ee48ee80d63d4": "Crimson Kunai",
    "a38cbf3abbfdcbedaa1b9a0e": "Viibiin — Strider",
    "b543125009b989d7287b58ab": "Axel — Rust",
    "9644da05612ad219e7fc4387": "Ossuary BEAR",
    "cd19a7a32ec3820a6ea48800": "Glorious — Blink",
    "1254eb68d298789e43b45ed3": "Golden Raven",
    "36be503998aadd53701c57e3": "Anomalous injector ANM-03",
    "f131c988930a86c7e5610c57": "Anomalous injector ANM-04",
});
Object.assign(labels, {
    requiredItemTpl: "Required Item ID",
    populateIdentityData: "Show Dogtag Identity Information",
    dogtagTplByWildType: "Dogtags by Operative",
});
function dogtagField(role, value, path) {
    if (role.toLowerCase().startsWith("blackdiv")) return null;
    const title = botName(role),
        q = $("#filter").value.toLowerCase();
    if (q && !`${title} ${role} ${value} dogtag`.toLowerCase().includes(q)) return null;
    const box = el("div", "field"),
        lab = el("label"),
        select = el("select");
    lab.append(el("span", "", title));
    select.setAttribute("aria-label", title + " dogtag");
    const choices = [["", "Default dogtag"], ...state.dogtags.map((x) => [x.value, x.label])];
    if (value && !questDogtags.has(value) && !choices.some(([id]) => id === value))
        choices.push([value, "Currently configured · " + value]);
    options(select, choices);
    select.value = value;
    if (questDogtags.has(value))
        box.append(
            el(
                "p",
                "hint",
                "Choose a regular dogtag to replace the currently configured quest item.",
            ),
        );
    select.onchange = () => set(path, select.value);
    lab.append(select);
    box.append(lab);
    return box;
}
function botSelect(title, value, path) {
    const box = el("label", "bot-choice");
    box.append(el("span", "", title));
    const select = el("select");
    select.setAttribute("aria-label", title);
    const event =
        state.current.document.relativePath !== "config.jsonc" ||
        path.startsWith("/labsBlackoutEvent/");
    const external = path.includes("/externalFactions/");
    const specialLeaderOnly = specialFormationPath(path) && path.endsWith("/boss");
    const allowed = (v) => (!specialLeaderOnly || specialRole(v)) && (external ? /^(chaosInsurgency|13101)$/i.test(String(v)) :
        !/^(chaosInsurgency|13101)$/i.test(String(v)) && (event || !/^(blackdiv|bossWedge)/i.test(String(v))));
    const choices = new Map(
        state.botTypes
            .filter((b) => allowed(b.value))
            .map((b) => [b.value.toLowerCase(), [b.value, b.label]]),
    );
    function collect(v) {
        if (!v || typeof v !== "object") return;
        if (typeof v.boss === "string" && !choices.has(v.boss.toLowerCase()))
            choices.set(v.boss.toLowerCase(), [v.boss, botName(v.boss)]);
        if (Array.isArray(v.followers))
            for (const f of v.followers)
                if (!choices.has(f.type.toLowerCase()))
                    choices.set(f.type.toLowerCase(), [f.type, botName(f.type)]);
        for (const child of Object.values(v))
            if (child && typeof child === "object") collect(child);
    }
    if (event) collect(state.original);
    if (!choices.has(String(value).toLowerCase()))
        choices.set(String(value).toLowerCase(), [value, botName(value)]);
    options(
        select,
        [...choices.values()]
            .filter(([id]) => allowed(id) || id === value)
            .map(([id, name]) => [
                id.toLowerCase() === String(value).toLowerCase() ? value : id,
                name,
            ]),
    );
    select.value = value;
    if (!allowed(value)) {
        const existing = select.selectedOptions[0];
        if (existing) {
            existing.disabled = true;
            existing.textContent += " · already configured";
        }
    }
    select.onchange = () => {
        set(path, select.value);
        if (path.endsWith("/boss")) {
            const heading = select.closest(".formation")?.querySelector(".array-head strong");
            if (heading)
                heading.textContent =
                    heading.textContent.split(" · ")[0] + " · " + botName(select.value);
        }
    };
    box.append(select);
    return box;
}
function followerList(list, path) {
    const box = el("div", "followers");
    box.append(el("h3", "section-title", "Followers"));
    if (!list.length)
        box.append(el("p", "hint", "This leader deploys alone. Add operators to build a squad."));
    list.forEach((f, i) => {
        const row = el("div", "follower-row");
        row.append(botSelect("Follower type", f.type, path + "/" + i + "/type"));
        for (const key of ["min", "max"]) {
            const lab = el("label", "count-choice", key === "min" ? "Minimum" : "Maximum"),
                input = el("input");
            input.type = "number";
            input.min = "0";
            input.step = "1";
            input.required = true;
            input.value = f[key];
            input.setAttribute(
                "aria-label",
                (key === "min" ? "Minimum" : "Maximum") + " followers",
            );
            input.oninput = () => {
                if (input.validity.valid) set(path + "/" + i + "/" + key, Number(input.value));
                const counts = row.querySelectorAll("input");
                if (counts.length === 2) {
                    const a = Number(counts[0].value),
                        b = Number(counts[1].value);
                    counts[1].setCustomValidity(
                        b < a ? "Maximum must be at least the minimum." : "",
                    );
                }
                dirty();
            };
            lab.append(input);
            row.append(lab);
        }
        const remove = el("button", "small danger", "Remove");
        remove.setAttribute("aria-label", "Remove follower " + (i + 1));
        remove.onclick = () => {
            const next = clone(at(path));
            next.splice(i, 1);
            set(path, next);
            render();
        };
        const maximum = row.querySelectorAll("input")[1];
        maximum.setCustomValidity(f.max < f.min ? "Maximum must be at least the minimum." : "");
        row.append(remove);
        box.append(row);
    });
    const add = el("button", "small", "+ Add follower");
    add.onclick = () => {
        const next = clone(at(path));
        next.push({ type: path.includes("/externalFactions/") ? "chaosInsurgency" : "ISBSpecialForces", min: 1, max: 1 });
        set(path, next);
        render();
    };
    box.append(add);
    return box;
}

function spawnMap(path) {
    const keys = path.split("/").map(unesc),
        name = keys[1] === "locations" ? keys[2] : keys[1] === "externalFactions" && keys[3] === "locations" ? keys[4] : "laboratory";
    return (
        state.spawnMaps.find((m) => m.map.toLowerCase() === name.toLowerCase()) || {
            map: name,
            botZones: [],
            checkpoints: [],
        }
    );
}
function formationId() {
    return "ISBWeb_" + Date.now().toString(36) + "_" + Math.random().toString(36).slice(2, 8);
}
function zoneList(formation, path) {
    const box = el("fieldset", "spawn-zones"),
        selected = new Set(
            (formation.zones || []).flatMap((z) =>
                z
                    .split(",")
                    .map((s) => s.trim())
                    .filter(Boolean),
            ),
        );
    box.append(el("legend", "", "BotZones"));
    const choices = [...new Set([...spawnMap(path).botZones, ...selected])].sort();
    if (!Object.hasOwn(formation, "checkpointRadius")) box.append(el("p", "hint", "Leave zones empty to use the random spawn selection."));
    if (!choices.length) box.append(el("p", "hint", "No BotZones are registered for this map."));
    for (const zone of choices) {
        const label = el("label", "zone-choice"),
            check = el("input");
        check.type = "checkbox";
        check.checked = selected.has(zone);
        check.setAttribute("aria-label", zone);
        check.onchange = () => {
            check.checked ? selected.add(zone) : selected.delete(zone);
            set(path + "/zones", [...selected]);
        };
        label.append(check, el("span", "", zone));
        box.append(label);
    }
    return box;
}
function checkpointArea(formation, path) {
    const box = el("div", "checkpoint-area"), coordinates = el("div", "fields");
    for (const key of ["x", "y", "z"]) {
        const label = el("label", "coordinate-choice"), input = el("input");
        label.append(el("span", "", key.toUpperCase()));
        input.type = "number";
        input.required = true;
        input.step = "any";
        input.value = formation[key] ?? 0;
        input.setAttribute("aria-label", "Checkpoint " + key.toUpperCase());
        input.oninput = () => {
            if (input.checkValidity()) set(path + "/" + key, Number(input.value));
            else dirty();
        };
        label.append(input);
        coordinates.append(label);
    }
    box.append(coordinates);
    return box;
}

function newFormation(path) {
    const special = specialLeader(path);
    if (special === null) return null;
    const f = {
        id: formationId(),
        enabled: false,
        weight: 1,
        ...(path.startsWith("/locations/") || path.startsWith("/externalFactions/chaosInsurgency/locations/") ? {} : { chance: 100 }),
        squadCount: 1,
        boss: special || (path.includes("/externalFactions/") ? "chaosInsurgency" : "ISBSpecialForces"),
        followers: [{ type: special === "blackDivGhostBusterLead" ? "blackDivGhostBuster01" : special === "ISBBossFirefly" ? "ISBFollowerVipper" : path.includes("/externalFactions/") ? "chaosInsurgency" : "ISBSpecialForces", min: 1, max: 1 }],
        zones: [],
        raidTimeMin: -1,
        raidTimeMax: -1,
    };
    if (/\/(checkpoints|checkpoint\/formations)$/.test(path)) {
        const point = spawnMap(path).checkpoints[0];
        Object.assign(f, {
            checkpointRadius: point?.radius ?? 20,
            x: point?.x ?? 0,
            y: point?.y ?? 0,
            z: point?.z ?? 0,
        });
    }
    return f;
}

function formationList(list, path, depth, title) {
    const q = $("#filter").value.toLocaleLowerCase(),
        details = el("details", "card formations");
    const specialOnly = specialFormationPath(path), entries = list.map((f, i) => ({f, i})).filter(({f}) => !specialOnly || specialFormation(f));
    details.dataset.path = path;
    details.open = depth < 2 || !!q;
    details.append(el("summary", "", `${title || "Formations"} · ${entries.length}`));
    const body = el("div", "body");
    entries.forEach(({f, i}) => {
        if (
            q &&
            !`${JSON.stringify(f)} ${botName(f.boss)} formation leader followers chance count`
                .toLocaleLowerCase()
                .includes(q)
        )
            return;
        const item = el("article", "array-item formation"),
            head = el("div", "array-head");
        head.append(el("strong", "", `Formation ${i + 1} · ${botName(f.boss)}`));
        const actions = el("div", "array-actions");
        const copy = el("button", "small", "Duplicate");
        copy.onclick = () => {
            const next = clone(at(path)),
                entry = clone(next[i]);
            entry.id =
                "ISBWeb_" + Date.now().toString(36) + "_" + Math.random().toString(36).slice(2, 8);
            entry.enabled = false;
            next.splice(i + 1, 0, entry);
            set(path, next);
            render();
            message(
                "Formation duplicated and disabled. Choose its operators, then enable it when ready. Spawn locations were preserved.",
            );
        };
        const remove = el("button", "small danger", "Remove");
        remove.onclick = () => {
            const next = clone(at(path));
            next.splice(i, 1);
            set(path, next);
            render();
        };
        actions.append(copy, remove);
        head.append(actions);
        item.append(head, botSelect("Formation leader", f.boss, path + "/" + i + "/boss"));
        const fields = el("div", "fields");
        for (const key of Object.keys(f)) {
            if (
                f[key] === null ||
                typeof f[key] === "object" ||
                ["boss", "x", "y", "z"].includes(key) ||
                (key === "chance" && /^\/locations\/[^/]+\/(patrol|hunt|checkpoint)\/formations$/.test(path))
            )
                continue;
            const fieldNode = field(key, f[key], path + "/" + i + "/" + key);
            if (fieldNode) fields.append(fieldNode);
        }
        item.append(fields);
        item.append(
            followerList(f.followers || [], path + "/" + i + "/followers"),
            zoneList(f, path + "/" + i),
        );
        if (Object.hasOwn(f, "checkpointRadius")) item.append(checkpointArea(f, path + "/" + i));
        body.append(item);
    });
    const add = el("button", "small", specialOnly ? "+ Add special formation" : "+ Add formation");
    add.onclick = () => {
        const next = clone(at(path));
        const formation = newFormation(path);
        if (!formation) return;
        next.push(formation);
        set(path, next);
        render();
        message(
            "Formation added and disabled. Select its operators and adjust its spawn settings before enabling it.",
        );
    };
    if (!specialOnly || specialLeader(path) !== null) body.append(add);
    if (!entries.length)
        body.append(el("p", "hint", specialOnly ? "No special formations are configured. Common squads are managed by Spawn Limit." : "No formations are configured in this category."));
    details.append(body);
    return details;
}
const friendlyHints = {
    "Blackout Sound Volume":
        "Volume of the power failure effect. In Containment Breach, this effect accompanies emergency power restoration.",
    "Laboratory Blackout Event Chance":
        "Chance of an automatic blackout in The Lab while ISB operators are present. (The Blackout Labs Variant when selected in the Map Screen guarantees blackout even without ISB present)",
    "Restrict Laboratory Exits":
        "During a blackout, only Hangar Gate and Parking Gate remain available.",
    "Enable ContainmentBreach Event":
        "Enable audio for the Containment Breach event.",
    "Containment Breach Event Chance":
        "Event chance in normal Labs raids. Selecting the Event variant in the map selector guarantees the event.",
    selectionMode:
        "Choose the factions that can respond to a blackout. Common squads use Blackout Spawn Limit; configure special formations below.",
};
Object.assign(labels, {
    "Laboratory Blackout Event Chance": "Random blackout chance (%)",
    "Restrict Laboratory Exits": "Restrict blackout extracts",
    "Blackout Sound Volume": "Blackout effect volume (%)",
    "Enable ContainmentBreach Event": "Containment Breach audio",
    "Containment Breach Event Chance": "Containment Breach chance (%)",
    selectionMode: "Responding factions",
    blackoutActivated: "Power Down",
    blackoutDeactivated: "Labs Power Restored",
});
function cfgGroup(f) {
    if (/containment/i.test(f.name)) return "__event";
    if (/blackout|restrict laboratory exits/i.test(f.name)) return "__blackout";
    return "Audio";
}
function sectionGuide() {
    if (state.view === "dogtags")
        return "Choose a dogtag for each operator. Identity information controls the name, level and kill details. Changes apply to newly generated operators after restarting the server.";
    if (state.view === "aishiLoyaltyCondition")
        return "Choose the item that activates the Aishi loyalty condition. This setting is separate from the device that reveals anomalous properties.";
    if (state.view === "revenge")
        return "Retaliation makes ISB hostile after you betray friendly operators. It can carry over into later PMC raids according to your retaliation rules. Quest truces that reset hostility each raid still take priority. This setting does not add spawn formations.";
    if (state.view === "locations")
        return "Choose a map, then build each formation with a leader and followers. Adjust its spawn chance and squad count below.";
    if (state.view === "labsBlackoutEvent")
        return "Enable the responding factions and configure special squads for power failure or restoration. Common squads and their replacements use Blackout Spawn Limit.";
    if (state.view === "__blackout")
        return "In The Lab, automatic blackouts require ISB operators and a successful chance roll. The Blackout variant guarantees a power failure. Shutting down both power cells also cuts the power manually.";
    if (state.view === "externalFactions")
        return "Choose a map outside The Lab, then configure Chaos Insurgency patrols, checkpoints and hunts with a leader and followers. The Lab uses its separate spawn queues. CI squads remain hostile to other factions and allied to other CI squads.";
    if (state.view === "containementBreachQueue" || state.view === "blackoutQueue")
        return "Common squads are always managed by this queue. A replacement waits until the squad's last survivor dies and enough slots are free for the whole group. Special formations also require room for their entire squad.";
    if (state.current?.document.relativePath === "containment_breach_config.jsonc" && ["isb", "blackDivision", "pmcs", "raiders", "isbReinforcements"].includes(state.view))
        return "Enable the faction and configure special formations here. Common squads and their replacements use Spawn Limit.";
    if (state.view === "blackDivision")
        return "Black Division is available in event formations. Regular formations use the ISB operator roster.";
    return "";
}

function overviewGroups() {
    const groups = [], E = ISBFormationEstimates, kinds = [["patrols", "Patrol"], ["checkpoints", "Checkpoint"], ["hunts", "Hunt"]];
    const faction = (value, name, offset = 0, enabled = true) => {
        for (const [key, kind] of kinds) groups.push(E.group({name, kind, formations: (value?.[key] || []).filter(specialFormation), limit: -1, offset, enabled: enabled && value?.enabled !== false}));
    };
    const phase = (value, name, offset, enabled) => {
        const mode = String(value?.selectionMode || "preferBlackDivision").toLowerCase();
        const hasBd = state.blackDivisionInstalled || state.data.noBlackDivMode === true;
        const bd = hasBd && value?.blackDivision?.enabled !== false;
        const useIsb = ["isb", "isbonly", "both"].includes(mode) || !["blackdivision", "blackdivisiononly"].includes(mode) && mode !== "both" && !bd;
        const useBd = ["blackdivision", "blackdivisiononly", "both"].includes(mode) || !["isb", "isbonly"].includes(mode) && mode !== "both";
        for (const [key, title, allowed] of [["isb", "ISB", useIsb], ["blackDivision", "Black Division", useBd && hasBd], ["pmcs", "PMCs", true], ["raiders", "Raiders", true]])
            if (value?.[key]) faction(value[key], name + " · " + title, offset, enabled && value.enabled !== false && allowed);
    };
    if (mapFormationsView()) {
        const external = state.view === "externalFactions";
        const map = mapLocations()[$("#map").value] || {};
        for (const [key, kind, flag, draw] of [["patrol", "Patrol", "EnableISBPatrols", "PatrolDrawLimit"], ["checkpoint", "Checkpoint", "enableISBHoldZones", "CheckpointDrawLimit"], ["hunt", "Hunt", "enableHunt", "HuntDrawLimit"]]) {
            const config = map[key] || {}, chance = state.data.debug?.spawnAlways ? 1 : E.spawnChance(config.spawnChance, config.formations || [], config.patrolChance || 0);
            groups.push(E.group({name: label($("#map").value), kind, formations: config.formations, limit: config[draw] ?? -1, chance, enabled: config[flag] !== false && (!external || state.data.externalFactions?.chaosInsurgency?.enabled !== false), instant: state.data.debug?.spawnInstantlyAlways === true}));
        }
    } else if (state.view === "labsBlackoutEvent") {
        const config = state.data.labsBlackoutEvent || {};
        phase(config.blackoutActivated, "Blackout begins", state.estimateSettings.blackoutMinute * 60, config.enabled !== false);
        phase(config.powerRestored, "Power restored", state.estimateSettings.powerMinute * 60, config.enabled !== false && state.estimateSettings.includeRestore);
    } else if (["isb", "blackDivision", "pmcs", "raiders"].includes(state.view)) {
        faction(state.data[state.view], label(state.view), 0, state.data.enabled !== false && (state.view !== "blackDivision" || state.blackDivisionInstalled || state.data.noBlackDivMode === true));
    } else if (state.view === "isbReinforcements") {
        phase(state.data.isbReinforcements, "Reinforcements triggered", state.estimateSettings.reinforcementMinute * 60, state.data.enabled !== false);
    }
    return groups;
}
function overviewTiming(host) {
    if (state.view !== "isbReinforcements") return;
    const controls = el("div", "estimate-timing overview-wide");
    controls.append(el("strong", "", "Timeline scenario · elapsed raid time"));
    for (const [key, title] of [["reinforcementMinute", "Reinforcements trigger (min)"]]) {
        const control = el("label"), input = el("input"); input.type = "number"; input.min = "0"; input.max = "300"; input.step = "0.1"; input.value = state.estimateSettings[key]; input.setAttribute("aria-label", title);
        input.onchange = () => { if (input.value !== "" && input.checkValidity()) { state.estimateSettings[key] = Number(input.value); updateOverview(); } };
        control.append(el("span", "", title), input); controls.append(control);
    }
    controls.append(el("p", "hint", "Scenario times only; these controls do not change event triggers. Formation delays are added to the selected trigger time."));
    host.append(controls);
}
function populationOverview() {
    const main = state.current?.document.relativePath === "config.jsonc";
    if (main ? !["labsBlackoutEvent", "blackoutQueue"].includes(state.view) :
        state.current?.document.relativePath !== "containment_breach_config.jsonc" || !["containementBreachQueue", "isb", "blackDivision", "pmcs", "raiders", "isbReinforcements"].includes(state.view)) return null;
    const event = main ? state.data.labsBlackoutEvent || {} : state.data;
    const queue = (main ? state.data.blackoutQueue : state.data.containementBreachQueue) || {};
    const performance = event.performanceMode === true;
    const maximum = Number((performance ? queue.performanceMaximumBots : queue.maximumBots) ?? (performance ? 13 : 18));
    const defaults = main
        ? {blackDivision: performance ? 6 : 8, isb: performance ? 2 : 4, chaosInsurgency: 0, raiders: 0, pmcs: 0}
        : {blackDivision: performance ? 6 : 11, isb: 4, chaosInsurgency: 3, raiders: 0, pmcs: 0};
    const normal = {...defaults, ...(queue[performance ? "performanceNormal" : "normal"] || {})};
    const reinforcementDefaults = main
        ? {...defaults, blackDivision: performance ? 3 : 4, isb: 2, raiders: performance ? 2 : 3, pmcs: performance ? 4 : 6}
        : {...defaults, blackDivision: performance ? 2 : 7, isb: 0, raiders: performance ? 4 : 2, pmcs: performance ? 4 : 6};
    const reinforcement = {...reinforcementDefaults, ...(queue[performance ? "performanceReinforcement" : "reinforcement"] || {})};
    let flags;
    if (main) {
        const phases = [event.blackoutActivated, event.powerRestored].filter(phase => phase && phase.enabled !== false);
        flags = [state.blackDivisionInstalled && phases.some(p => p.blackDivision?.enabled !== false), phases.some(p => p.isb?.enabled !== false), phases.some(p => p.externalFactions?.chaosInsurgency?.enabled !== false), phases.some(p => p.raiders?.enabled !== false), phases.some(p => p.pmcs?.enabled !== false)];
    } else {
        const ordinary = !event.whiteTuskOnly && !event.ghostBusterOnly;
        flags = event.chaosInsurgencyOnly ? [false, false, true, false, false] :
            [ordinary && !event.noBlackDivMode && event.blackDivision?.enabled !== false && state.blackDivisionInstalled, ordinary && !event.noISBMode && event.isb?.enabled !== false, ordinary && event.externalFactions?.chaosInsurgency?.enabled !== false, ordinary && event.raiders?.enabled !== false, ordinary && event.pmcs?.enabled !== false];
        if (event.chaosInsurgencyOnly) normal.chaosInsurgency = reinforcement.chaosInsurgency = maximum;
    }
    if (event.enabled === false) flags.fill(false);
    const expand = value => [value.blackDivision, value.isb, value.chaosInsurgency, value.raiders, Math.ceil(Number(value.pmcs) / 2), Math.floor(Number(value.pmcs) / 2)].map((count, index) => flags[Math.min(index, 4)] ? Number(count) || 0 : 0);
    const a = expand(normal), b = expand(reinforcement);
    return {maximum: maximum + Number(queue.squadOverflow || 0), normal: a, reinforcement: b, normalSeconds: Number(queue.reinforcementIntervalSeconds ?? 300), reinforcementSeconds: Number(queue.reinforcementWindowSeconds ?? 120), rows: ["Black Division", "ISB", "Chaos Insurgency", "Raiders", "PMC USEC", "PMC BEAR"].map((name, i) => ({name, normal: a[i], reinforcement: b[i]}))};
}
function renderPopulationOverview(host, population) {
    const wrap = el("div", "overview-wide table-scroll"), table = el("table", "spawn-timeline"), head = el("tr"), body = el("tbody");
    wrap.append(el("strong", "", "Common squads · phase targets"), el("p", "hint", `Total AI limit: ${population.maximum}. Normal phase: ${population.normalSeconds}s. PMC / Raider phase: ${population.reinforcementSeconds}s.`));
    for (const text of ["Faction", "Normal phase", "PMC / Raider phase"]) head.append(el("th", "", text));
    const thead = el("thead"); thead.append(head); table.append(thead);
    for (const entry of [...population.rows, {name: "Total", normal: population.normal.reduce((a, b) => a + b, 0), reinforcement: population.reinforcement.reduce((a, b) => a + b, 0)}]) {
        const row = el("tr"); for (const value of [entry.name, entry.normal, entry.reinforcement]) row.append(el("td", "", String(value))); body.append(row);
    }
    table.append(body); wrap.append(table, el("p", "hint", "Special squads share the total AI limit. Surviving squads remain until eliminated.")); host.append(wrap);
}
function updateOverview() {
    const host = $("#overview");
    if (!host) return;
    host.replaceChildren();
    const doc = state.current?.document;
    if (!doc || doc.format !== "json") {
        host.hidden = true;
        return;
    }
    if (state.view === "botHealth") for (const total of document.querySelectorAll("[data-health-total]"))
        total.textContent = Object.values(state.data.botHealth?.[total.dataset.healthTotal] || {}).reduce((n, hp) => n + Number(hp || 0), 0) + " base HP";
    const population = populationOverview();
    const groups = overviewGroups().filter(group => mapFormationsView() || group.eligible > 0); host.hidden = groups.length === 0 && !population;
    if (population) renderPopulationOverview(host, population);
    if (!groups.length) return;
    const number = n => Number(n.toFixed(1)).toLocaleString("en-US", {maximumFractionDigits: 1});
    overviewTiming(host);
    for (const kind of ["Hunt", "Checkpoint", "Patrol"]) {
        const entries = groups.filter(g => g.kind === kind), sum = key => entries.reduce((n, g) => n + g[key], 0), card = el("div", "overview-card");
        const normalMap = mapFormationsView();
        card.append(el("strong", "", number(sum(normalMap ? "selectedMean" : "mean"))), el("span", "", (normalMap ? "" : "Special ") + kind + " · average operators"), el("p", "hint", `${sum("draws")} formations selected / ${sum("eligible")} eligible · ${sum("min")}–${sum("max")} operators${normalMap ? "" : " if spawn rolls succeed"}`)); host.append(card);
    }
    if (mapFormationsView()) return;
    const wrap = el("div", "overview-wide table-scroll"), table = el("table", "spawn-timeline"), head = el("tr");
    for (const text of ["Raid minute", "Special Hunt", "Special Checkpoint", "Special Patrol", "Specials spawned"]) head.append(el("th", "", text));
    const thead = el("thead"); thead.append(head); table.append(thead); const body = el("tbody");
    for (const minute of [5, 10, 15, 20, 25, 30]) {
        const row = el("tr"), counts = ["Hunt", "Checkpoint", "Patrol"].map(kind => groups.filter(g => g.kind === kind).reduce((n, g) => n + ISBFormationEstimates.expected(g, minute * 60), 0));
        for (const value of [minute + " min", ...counts.map(number), number(counts.reduce((a, b) => a + b, 0))]) row.append(el("td", "", value)); body.append(row);
    }
    table.append(body); wrap.append(table); host.append(wrap);
}

function renderBotHealth(host) {
    host.append(el("p", "section-guide", "Base health for newly generated ISB and Chaos Insurgency bots. Their respective health multipliers continue to apply. Restart the server after saving."));
    const ranks = new Map(state.botTypes.map((bot, index) => [bot.value.toLowerCase(), index]));
    const entries = Object.entries(state.data.botHealth || {}).sort(([a], [b]) => (ranks.get(a.toLowerCase()) ?? Number.MAX_SAFE_INTEGER) - (ranks.get(b.toLowerCase()) ?? Number.MAX_SAFE_INTEGER));
    for (const [role, parts] of entries) {
        const card = el("details", "health-card"); card.dataset.path = "/botHealth/" + esc(role);
        const title = el("summary"), total = el("span", "health-total"); total.dataset.healthTotal = role;
        total.textContent = Object.values(parts).reduce((n, hp) => n + Number(hp || 0), 0) + " base HP";
        title.append(el("span", "", botName(role)), total); card.append(title, el("p", "hint", role));
        const grid = el("div", "fields health-fields");
        for (const limb of ["Head", "Chest", "Stomach", "LeftArm", "RightArm", "LeftLeg", "RightLeg"]) { const control = field(limb, parts[limb], "/botHealth/" + esc(role) + "/" + limb); if (control) grid.append(control); }
        card.append(grid); host.append(card);
    }
}

function render() {
    const host = $("#content");
    const key = [state.current?.document.id, state.view, $("#map").value].join("|");
    const same = host.dataset.viewKey === key;
    const opened = same ? new Map(Array.from(host.querySelectorAll("details[data-path]"), d => [d.dataset.path, d.open])) : new Map();
    const scrolls = same ? [document.scrollingElement, $("main"), $(".workspace")].filter(Boolean).map(node => ({node, x:node.scrollLeft, y:node.scrollTop})) : [];
    renderContent();
    host.dataset.viewKey = key;
    for (const d of host.querySelectorAll("details[data-path]"))
        if (opened.has(d.dataset.path)) d.open = opened.get(d.dataset.path);
    for (const s of scrolls) { s.node.scrollLeft = s.x; s.node.scrollTop = s.y; }
}
function renderExternalFactions(host) {
    if (state.current.document.relativePath !== "config.jsonc") return;
    ensureExternalFactions();
    const fields = el("div", "fields");
    const enabled = field("enabled", state.data.externalFactions.chaosInsurgency.enabled, "/externalFactions/chaosInsurgency/enabled");
    if (enabled) fields.append(enabled);
    host.append(el("h3", "section-title", "Chaos Insurgency"), fields);
    const {value, path} = selected();
    if (value) host.append(object(value, path));
    else host.append(el("p", "empty", "No maps outside The Lab are configured in this version."));
}
function renderQueueFactionControls(host) {
    const main = state.current.document.relativePath === "config.jsonc";
    const entries = main
        ? ["blackoutActivated", "powerRestored"].map(phase => [phase === "blackoutActivated" ? "Power Down" : "Labs Power Restored", state.data.labsBlackoutEvent?.[phase]?.externalFactions?.chaosInsurgency, "/labsBlackoutEvent/" + phase + "/externalFactions/chaosInsurgency/enabled"])
        : [["Containment Breach", state.data.externalFactions?.chaosInsurgency, "/externalFactions/chaosInsurgency/enabled"]];
    for (const [title, faction, path] of entries) {
        if (!faction) continue;
        const control = field("enabled", faction.enabled, path);
        if (control) host.append(el("h3", "section-title", "Chaos Insurgency · " + title), control);
    }
}

function renderContent() {
    updateOverview();
    const host = $("#content");
    host.replaceChildren();
    if (!state.current) return;
    const guide = sectionGuide();
    if (guide) host.append(el("p", "section-guide", guide));
    if (state.view === "externalFactions") { renderExternalFactions(host); return; }
    if (["containementBreachQueue", "blackoutQueue"].includes(state.view)) renderQueueFactionControls(host);
    if (state.current.document.relativePath === "config.jsonc" && state.view === "botHealth") { renderBotHealth(host); return; }
    if (state.current.document.format === "cfg") {
        const fields = el("div", "fields");
        for (const f of state.fields.filter((f) => !["enablepreblackoutevent", "preblackoutdelay"].includes(f.name.replace(/[-_\s]/g, "").toLowerCase()) && cfgGroup(f) === $("#section").value)) {
            const e = field(f.name, f.value, f.path, f);
            if (e) fields.append(e);
        }
        host.append(fields);
        return;
    }
    if (state.current.document.relativePath === "containment_breach_config.jsonc" && state.view === "populationControl") {
        const fields = el("div", "fields");
        for (const key of populationModes) {
            const control = field(key, state.data[key] ?? false, "/" + key);
            if (control) fields.append(control);
        }
        if (fields.children.length) host.append(fields);
    }
    const { value, path } = selected();
    if (value === undefined) {
        host.append(el("p", "empty", "This option is not configured in this version."));
        return;
    }
    if (Array.isArray(value)) {
        const card = section(value, path, label($("#section").value), 0);
        if (card) host.append(card);
    } else host.append(object(value, path));
    if (state.current.document.relativePath === "config.jsonc" && $("#section").value === "__general") {
        const settings = state.data.debug || {};
        const fields = el("div", "fields");
        for (const key of ["enableISBCustomBehavior", "enableBlackDivCustomBehavior", "changeBlackDivBehaviorAllMaps", "useSainLayersChance", "logs", "spawnAlways", "spawnInstantlyAlways"]) {
            const control = field(key, settings[key] ?? (key === "useSainLayersChance" ? 35 : ["enableISBCustomBehavior", "enableBlackDivCustomBehavior"].includes(key)), "/debug/" + key);
            if (control) fields.append(control);
        }
        host.append(fields);
    }
    if (!host.textContent.trim()) host.append(el("p", "empty", "No matching settings found."));
}
$("#search").oninput = drawDocs;
$("#filter").oninput = render;
$("#section").onchange = () => {
    state.view = $("#section").value;
    setHeading();
    drawDocs();
    maps();
    render();
};
$("#map").onchange = render;
$("#expand").onclick = () =>
    document.querySelectorAll("#content details").forEach((d) => (d.open = true));
$("#collapse").onclick = () =>
    document.querySelectorAll("#content details").forEach((d) => (d.open = false));
let filterTimer;
$("#filter").oninput = () => {
    clearTimeout(filterTimer);
    filterTimer = setTimeout(render, 120);
};
function resetDefaults() {
    if (state.busy || state.saving || !state.current) return;
    const view = state.view;
    if (state.current.document.format === "cfg") {
        const defaults = new Map((state.current.defaultFields || []).map(field => [field.path, field.value]));
        for (const field of state.fields)
            if (cfgGroup(field) === view && defaults.has(field.path)) field.value = defaults.get(field.path);
    } else {
        const defaults = state.current.defaults;
        if (!defaults) return;
        if (view === "externalFactions" && state.current.document.relativePath === "config.jsonc") {
            if (defaults.externalFactions) state.data.externalFactions = clone(defaults.externalFactions);
            else {
                delete state.data.externalFactions;
                ensureExternalFactions();
            }
        } else if (view === "blackoutQueue") {
            state.data.blackoutQueue = clone(defaults.blackoutQueue);
            for (const phase of ["blackoutActivated", "powerRestored"])
                if (defaults.labsBlackoutEvent?.[phase]?.externalFactions?.chaosInsurgency)
                    set("/labsBlackoutEvent/" + phase + "/externalFactions/chaosInsurgency/enabled", defaults.labsBlackoutEvent[phase].externalFactions.chaosInsurgency.enabled);
        } else if (view === "containementBreachQueue") {
            state.data.containementBreachQueue = clone(defaults.containementBreachQueue);
            if (defaults.externalFactions?.chaosInsurgency)
                set("/externalFactions/chaosInsurgency/enabled", defaults.externalFactions.chaosInsurgency.enabled);
        } else if (view === "labsBlackoutEvent") {
            const external = Object.fromEntries(["blackoutActivated", "powerRestored"].map(phase => [phase, clone(state.data.labsBlackoutEvent[phase].externalFactions)]));
            state.data.labsBlackoutEvent = clone(defaults.labsBlackoutEvent);
            for (const phase of Object.keys(external)) state.data.labsBlackoutEvent[phase].externalFactions = external[phase];
        } else if (view === "isbReinforcements") {
            const external = clone(state.data.isbReinforcements.externalFactions);
            state.data.isbReinforcements = clone(defaults.isbReinforcements);
            state.data.isbReinforcements.externalFactions = external;
        } else if (view === "__general") {
            for (const [key, value] of Object.entries(defaults))
                if ((value === null || typeof value !== "object") && visible("/" + esc(key))) state.data[key] = clone(value);
            if (state.current.document.relativePath === "config.jsonc")
                for (const key of ["enableISBCustomBehavior", "enableBlackDivCustomBehavior", "changeBlackDivBehaviorAllMaps", "useSainLayersChance", "logs", "spawnAlways", "spawnInstantlyAlways"])
                    if (Object.hasOwn(defaults.debug || {}, key)) set("/debug/" + key, clone(defaults.debug[key]));
        } else {
            if (Object.hasOwn(defaults, view)) state.data[view] = clone(defaults[view]);
            if (state.current.document.relativePath === "containment_breach_config.jsonc" && view === "populationControl")
                for (const key of populationModes)
                    if (Object.hasOwn(defaults, key)) state.data[key] = clone(defaults[key]);
        }
    }
    maps();
    render();
    dirty();
    message("Defaults restored for this tab. Save changes to apply.");
}
$("#reset-defaults").onclick = resetDefaults;
$("#undo").onclick = async () => {
    if (await confirmDiscard()) await openDoc(state.current.document.id, true, state.view);
};
function savedValue(document, path) {
    if (document.document.format === "cfg")
        return document.fields.find((f) => f.path === path)?.value;
    return path.split("/").slice(1).map(unesc).reduce((value, key) => value?.[key], document.data);
}
async function persistChanges(pending) {
    const error = formationError(state.data);
    if (error) throw new Error(error);
    const id = state.current.document.id;
    const original = { document: state.current.document, data: state.original, fields: state.originalFields };
    async function write(revision) {
        const catalog = await api("catalog");
        state.token = catalog.token;
        state.tokenHeader = catalog.tokenHeader;
        return api("save", {
            method: "POST",
            headers: { [state.tokenHeader || "RequestVerificationToken"]: state.token },
            body: JSON.stringify({ id, revision, changes: pending }),
        });
    }
    let response;
    try {
        response = await write(state.current.revision);
    } catch (error) {
        if (error.status !== 409) throw error;
        const latest = await api("document/" + id);
        if (pending.every((change) => eq(savedValue(latest, change.path), change.value)))
            response = { revision: latest.revision, message: "Configuration saved. " + latest.document.apply };
        else if (pending.every((change) => eq(savedValue(latest, change.path), savedValue(original, change.path))))
            response = await write(latest.revision);
        else
            throw new Error("Another page or the JSON file changed the same settings. Your edits are kept here; export them before reloading.");
    }
    const saved = await api("document/" + id);
    if (saved.revision !== response.revision || !pending.every((change) => eq(savedValue(saved, change.path), change.value)))
        throw new Error("The saved file could not be verified or was changed again. Your edits are kept here; try saving again.");
    return { saved, message: response.message };
}
$("#save").onclick = async () => {
    if (state.saving || !state.current) return;
    const pending = changes();
    if (!pending.length) return;
    state.saving = true;
    state.busy = true;
    try {
        dirty();
        const result = await persistChanges(pending);
        state.current = result.saved;
        state.data = clone(result.saved.data);
        state.fields = Array.isArray(result.saved.fields)
            ? clone(result.saved.fields).filter((field) => field.section !== "Logging") : [];
        state.original = clone(state.data);
        state.originalFields = clone(state.fields);
        render();
        message(result.message);
    } catch (error) {
        message(error.message + " Your changes are still on this page.", true);
    } finally {
        state.saving = false;
        state.busy = false;
        dirty();
    }
};
$("#export").onclick = () => {
    const error = formationError(state.data);
    if (error) { message(error, true); return; }
    const cfg = state.current.document.format === "cfg";
    const output = cfg
        ? state.fields.map((f) => `[${f.section}]\n${f.name} = ${f.value}`).join("\n\n")
        : JSON.stringify(state.data, null, 2);
    const url = URL.createObjectURL(new Blob([output], { type: "text/plain;charset=utf-8" }));
    const a = el("a");
    a.href = url;
    a.download = state.current.document.relativePath
        .split("/")
        .pop();
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
window.addEventListener("beforeunload", (e) => {
    if (changes().length) {
        e.preventDefault();
        e.returnValue = "";
    }
});
(async () => {
    try {
        const data = await api("catalog");
        state.catalog = data.documents;
        state.botTypes = data.botTypes || [];
        state.blackDivisionInstalled = data.blackDivisionInstalled !== false;
        state.dogtags = (data.dogtags || []).filter((x) => !questDogtags.has(x.value));
        state.token = data.token;
        state.tokenHeader = data.tokenHeader;
        $("#version").textContent = data.version + " · Server connected";
        drawDocs();
        if (state.catalog.length) await openDoc(state.catalog[0].id, true);
        else message("No configuration files were found in this installation.", true);
    } catch (e) {
        $("#version").textContent = "Connection unavailable";
        message(e.message, true);
    }
})();

function spawnCatalogue() {
    return [
        {
            "map": "laboratory",
            "botZones": [
                "BotZoneBasement",
                "BotZoneFloor1",
                "BotZoneFloor2",
                "BotZoneGate1",
                "BotZoneGate2",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": -212.866,
                    "y": 0.054,
                    "z": -352.771,
                    "radius": 20,
                },
                {
                    "id": "ContainmentBreachISBCheckPointOutsParkGate",
                    "label": "Containment Breach ISBCheck Point Outs Park Gate",
                    "x": -232.728,
                    "y": 0.046,
                    "z": -420.636,
                    "radius": 8,
                },
                {
                    "id": "ContainmentBreachISBCheckPointInsidParkGate",
                    "label": "Containment Breach ISBCheck Point Insid Park Gate",
                    "x": -230.977,
                    "y": 2.278,
                    "z": -450.041,
                    "radius": 8,
                },
                {
                    "id": "ContainmentBreachBDCheckPointOutsHangarGate",
                    "label": "Containment Breach BDCheck Point Outs Hangar Gate",
                    "x": -170.805,
                    "y": 0.062,
                    "z": -254.991,
                    "radius": 8,
                },
                {
                    "id": "ContainmentBreachBDCheckPointInsidHangarGate",
                    "label": "Containment Breach BDCheck Point Insid Hangar Gate",
                    "x": -170.347,
                    "y": 3.147,
                    "z": -217.6,
                    "radius": 8,
                },
            ],
        },
        {
            "map": "sandbox",
            "botZones": ["ZoneSandSnipeCenter", "ZoneSandbox"],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 71.932,
                    "y": 22.813,
                    "z": 79.707,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "SandboxHigh",
            "botZones": ["ZoneSandSnipeCenter", "ZoneSandSnipeCenter2", "ZoneSandbox"],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 71.932,
                    "y": 22.813,
                    "z": 79.707,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "TarkovStreets",
            "botZones": [
                "ZoneCarShowroom",
                "ZoneCard1",
                "ZoneCinema",
                "ZoneClimova",
                "ZoneColumn",
                "ZoneConcordiaParking",
                "ZoneConcordia_1",
                "ZoneConstruction",
                "ZoneFactory",
                "ZoneHotel_1",
                "ZoneHotel_2",
                "ZoneMvd",
                "ZoneSW00",
                "ZoneSW01",
                "ZoneSnipeBuilding",
                "ZoneSnipeCarShowroom",
                "ZoneSnipeCard",
                "ZoneSnipeCinema",
                "ZoneSnipeSW01",
                "ZoneSnipeStilo",
                "ZoneStilo",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 92.411,
                    "y": 0.252,
                    "z": -47.751,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "bigmap",
            "botZones": [
                "ZoneBlockPost",
                "ZoneBlockPostSniper",
                "ZoneBlockPostSniper3",
                "ZoneBrige",
                "ZoneCrossRoad",
                "ZoneCustoms",
                "ZoneDormitory",
                "ZoneFactoryCenter",
                "ZoneFactorySide",
                "ZoneGasStation",
                "ZoneOldAZS",
                "ZoneScavBase",
                "ZoneSnipeBrige",
                "ZoneSnipeFactory",
                "ZoneSnipeTower",
                "ZoneTankSquare",
                "ZoneWade",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 107.55,
                    "y": 16.216,
                    "z": -47.927,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "interchange",
            "botZones": [
                "ZoneCenter",
                "ZoneCenterBot",
                "ZoneGoshan",
                "ZoneIDEA",
                "ZoneIDEAPark",
                "ZoneOLI",
                "ZoneOLIPark",
                "ZonePowerStation",
                "ZoneRoad",
                "ZoneTrucks",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 44.64,
                    "y": 36.603,
                    "z": -69.674,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "Woods",
            "botZones": [
                "ZoneBigRocks",
                "ZoneBrokenVill",
                "ZoneClearVill",
                "ZoneDepo",
                "ZoneHighRocks",
                "ZoneHouse",
                "ZoneMiniHouse",
                "ZoneRedHouse",
                "ZoneRoad",
                "ZoneScavBase2",
                "ZoneStoneBunker",
                "ZoneUsecBase",
                "ZoneWoodCutter",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 395.043,
                    "y": -13.956,
                    "z": 181.716,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "Factory4Day",
            "botZones": ["BotZone"],
            "checkpoints": [],
        },
        {
            "map": "Factory4Night",
            "botZones": ["BotZone"],
            "checkpoints": [],
        },
        {
            "map": "RezervBase",
            "botZones": [
                "ZoneBarrack",
                "ZoneBunkerStorage",
                "ZonePTOR1",
                "ZonePTOR2",
                "ZoneRailStrorage",
                "ZoneSubCommand",
                "ZoneSubStorage",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 57.334,
                    "y": -12.205,
                    "z": -127.209,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "Labyrinth",
            "botZones": ["BotZone"],
            "checkpoints": [],
        },
        {
            "map": "Lighthouse",
            "botZones": [
                "Zone_Blockpost",
                "Zone_Bridge",
                "Zone_Chalet",
                "Zone_Containers",
                "Zone_DestroyedHouse",
                "Zone_Hellicopter",
                "Zone_Island",
                "Zone_LongRoad",
                "Zone_OldHouse",
                "Zone_Rocks",
                "Zone_RoofBeach",
                "Zone_RoofContainers",
                "Zone_RoofRocks",
                "Zone_SniperPeak",
                "Zone_TreatmentBeach",
                "Zone_TreatmentContainers",
                "Zone_TreatmentRocks",
                "Zone_Village",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": 355.213,
                    "y": 15.017,
                    "z": 542.654,
                    "radius": 20,
                },
            ],
        },
        {
            "map": "icebreaker",
            "botZones": [],
            "checkpoints": [],
        },
        {
            "map": "terminal",
            "botZones": [],
            "checkpoints": [],
        },
        {
            "map": "Shoreline",
            "botZones": [
                "ZoneBunkeSniper",
                "ZoneBunker",
                "ZoneBusStation",
                "ZoneForestGasStation",
                "ZoneForestSpawn",
                "ZoneForestTruck",
                "ZoneGasStation",
                "ZoneGreenHouses",
                "ZoneIsland",
                "ZoneMeteoStation",
                "ZonePassClose",
                "ZonePort",
                "ZonePowerStation",
                "ZonePowerStationSniper",
                "ZoneRailWays",
                "ZoneSanatorium1",
                "ZoneSanatorium2",
                "ZoneSmuglers",
                "ZoneStartVillage",
                "ZoneTunnel",
            ],
            "checkpoints": [
                {
                    "id": "StandardCheckpoint",
                    "label": "Standard Checkpoint",
                    "x": -183.011,
                    "y": -5.178,
                    "z": -118.656,
                    "radius": 20,
                },
            ],
        },
    ];
}
