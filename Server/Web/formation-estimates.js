"use strict";
const ISBFormationEstimates = (() => {
    const cache = new Map();
    const clamp = (n, a, b) => Math.min(b, Math.max(a, Number(n) || 0));
    function selections(rows, limit) {
        const n = rows.length, draws = limit < 0 ? n : Math.min(n, Math.max(0, Math.trunc(limit)));
        if (!draws) return rows.map(() => 0);
        if (draws === n) return rows.map(() => 1);
        const original = rows.map(f => Number(f.weight ?? 1)), scale = Math.max(...original), weights = original.map(w => w / scale);
        if (draws === 1) { const total = weights.reduce((a, b) => a + b, 0); return weights.map(w => w / total); }
        const key = JSON.stringify([draws, original]);
        if (cache.has(key)) return cache.get(key);
        let seed = 0x6a09e667;
        const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) | 0; return (seed >>> 0) / 4294967296; };
        const result = rows.map(() => 0), trials = 4096;
        for (let trial = 0; trial < trials; trial++) {
            const remaining = original.map((weight, index) => ({weight, index}));
            for (let draw = 0; draw < draws; draw++) {
                const scale = Math.max(...remaining.map(item => item.weight));
                let roll = random() * remaining.reduce((sum, item) => sum + item.weight / scale, 0), pick = remaining.length - 1;
                for (let index = 0; index < remaining.length; index++) { roll -= remaining[index].weight / scale; if (roll < 0) { pick = index; break; } }
                result[remaining[pick].index] += 1 / trials; remaining.splice(pick, 1);
            }
        }
        if (cache.size >= 64) cache.delete(cache.keys().next().value);
        cache.set(key, result); return result;
    }
    function spawnChance(config, formations, fallback = 0) {
        if (config != null) return clamp(config, 0, 100) / 100;
        const active = formations.filter(f => f && f.enabled !== false && Number(f.weight ?? 1) > 0 && Number.isFinite(Number(f.weight ?? 1)));
        if (!active.length) return clamp(fallback, 0, 100) / 100;
        const scale = Math.max(...active.map(f => Number(f.weight ?? 1))), total = active.reduce((n, f) => n + Number(f.weight ?? 1) / scale, 0);
        return active.reduce((n, f) => n + Number(f.weight ?? 1) / scale * clamp(f.chance ?? 100, 0, 100), 0) / total / 100;
    }
    function size(f) {
        let min = 1, max = 1;
        for (const follower of f.followers || []) {
            if (!follower || !String(follower.type || "").trim()) continue;
            const a = Math.max(0, Math.trunc(Number(follower.min) || 0)), b = Math.max(0, Math.trunc(Number(follower.max) || 0));
            min += Math.min(a, b); max += Math.max(a, b);
        }
        const squads = Math.max(1, Math.trunc(Number(f.squadCount) || 1));
        return {min: min * squads, max: max * squads, mean: (min + max) * .5 * squads};
    }
    function timeFraction(f, seconds, offset = 0, instant = false) {
        if (seconds < offset) return 0;
        let min = Number(f.raidTimeMin ?? -1), max = Number(f.raidTimeMax ?? -1);
        if (instant || min < 0 && max < 0) return 1;
        if (min < 0) min = max; if (max < 0) max = min;
        if (min > max) [min, max] = [max, min];
        return clamp((Math.floor(seconds - offset) - min + 1) / (max - min + 1), 0, 1);
    }
    function group(config) {
        const formations = Array.isArray(config.formations) ? config.formations : [];
        const limit = config.limit ?? -1;
        const eligible = config.enabled === false ? [] : formations.filter(f => f && f.enabled !== false &&
            (config.kind !== "Checkpoint" || f.zones?.some(z => String(z).trim())) &&
            (limit < 0 || Number.isFinite(Number(f.weight ?? 1)) && Number(f.weight ?? 1) > 0));
        const probabilities = selections(eligible, limit), draws = limit < 0 ? eligible.length : Math.min(eligible.length, Math.max(0, Math.trunc(limit)));
        const entries = eligible.map((formation, index) => ({formation, probability: probabilities[index], chance: config.chance == null ? clamp(formation.chance ?? 100, 0, 100) / 100 : config.chance, ...size(formation)}));
        const sortedMin = entries.map(f => f.min).sort((a, b) => a - b), sortedMax = entries.map(f => f.max).sort((a, b) => b - a);
        return {...config, eligible: eligible.length, configured: formations.length, draws, entries,
            min: sortedMin.slice(0, draws).reduce((a, b) => a + b, 0), max: sortedMax.slice(0, draws).reduce((a, b) => a + b, 0),
            selectedMean: entries.reduce((n, f) => n + f.probability * f.mean, 0),
            mean: entries.reduce((n, f) => n + f.probability * f.chance * f.mean, 0)};
    }
    function expected(group, seconds) {
        return group.entries.reduce((total, item) => total + item.probability * item.chance * item.mean * timeFraction(item.formation, seconds, group.offset || 0, group.instant), 0);
    }
    return Object.freeze({group, expected, timeFraction, spawnChance, selections});
})();
if (typeof module !== "undefined" && module.exports) module.exports = ISBFormationEstimates;
