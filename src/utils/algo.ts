import { DataConversion } from '../types';

export const checkCycleInConversions = (tempConversions: DataConversion[]): boolean => {
    const adj = new Map<string, string[]>();

    for (const conv of tempConversions) {
        if (!conv.active) continue;
        const src = `${conv.sourceDeviceId}:${conv.sourceVariableKey}`;
        const dst = `${conv.targetDeviceId}:${conv.targetVariableKey}`;
        if (!adj.has(src)) {
            adj.set(src, []);
        }
        adj.get(src)!.push(dst);
    }

    const visited = new Set<string>();
    const recStack = new Set<string>();

    const dfs = (node: string): boolean => {
        visited.add(node);
        recStack.add(node);

        const neighbors = adj.get(node) || [];
        for (const neighbor of neighbors) {
            if (!visited.has(neighbor)) {
                if (dfs(neighbor)) return true;
            } else if (recStack.has(neighbor)) {
                return true; // Cycle detected
            }
        }

        recStack.delete(node);
        return false;
    };

    const allNodes = new Set<string>();
    for (const [src, dsts] of adj.entries()) {
        allNodes.add(src);
        for (const dst of dsts) {
            allNodes.add(dst);
        }
    }

    for (const node of allNodes) {
        if (!visited.has(node)) {
            if (dfs(node)) return true;
        }
    }

    return false;
};
