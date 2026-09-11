import { DataConversion, DataTypeEnum } from '../types';

// 数据类型兼容判定（与后端 ScadaServer.Domain.Enums.DataTypeCompatibility 保持一致）：
// 同一类型恒兼容；数值类（INT/DINT/REAL/FLOAT/DOUBLE/UINT16/UINT32/INT64/UINT64/WORD/BYTE）任意互转；
// 布尔类（BOOL/BIT）互转；文本类（STRING/CHAR）仅同类型；跨大类拒绝。
export const isTypeCompatible = (source?: DataTypeEnum | null, target?: DataTypeEnum | null): boolean => {
  if (!source || !target) return true; // 类型未知不拦截（与后端保存期强校验解耦，仅前端提示）
  if (source === target) return true;
  return (isNumericType(source) && isNumericType(target)) || (isBoolType(source) && isBoolType(target));
};

export const describeTypeCategory = (type?: DataTypeEnum | null): string => {
  if (!type) return '未知';
  return isNumericType(type) ? '数值类' : isBoolType(type) ? '布尔类' : '文本类';
};

const isNumericType = (t: DataTypeEnum): boolean =>
  ['INT', 'DINT', 'REAL', 'FLOAT', 'DOUBLE', 'UINT16', 'UINT32', 'INT64', 'UINT64', 'WORD', 'BYTE'].includes(t);

const isBoolType = (t: DataTypeEnum): boolean => t === 'BOOL' || t === 'BIT';

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
