/**
 * 离线数据快照存储层（doc/pwa 阶段六 · 步骤 26，§6.1，D9/防线三）。
 *
 * IndexedDB：`pwa-snapshot`（版本 1），单库多 store + 记录键带 `u{userId}:` 前缀
 * （跨账号安全防线三：即使防线二清理被绕过，不同 userId 的快照也互不可见）。
 *
 * store 设计（与计划 §6.1 的偏差及理由）：
 * - meta / devices / alarms 三个 store；
 * - **不单设 variables store**：本项目的实时变量值（variables/variableTimestamps/variableMeta）
 *   内嵌在 Device 对象上（见 signalRService ReceiveVariableUpdate / deviceService.syncDevices），
 *   设备全量对象落盘即天然携带变量最新值，独立 variables store 属重复存储且放大写入量；
 * - alarms 环形保留最近 100 条（append 时超量删最旧，按 userId 过滤）。
 *
 * 全 API Promise 化 + 失败静默降级（返回 null/false，console.warn，绝不抛给调用方）：
 * IndexedDB 被系统回收/损坏时视为「无快照」，调用方进入离线空态，应用不崩溃。
 */

const DB_NAME = 'pwa-snapshot';
const DB_VERSION = 1;
const ALARM_KEEP = 100;

export interface SnapshotMeta {
  userId: string;
  savedAtUtc: string;
  swVersion: string;
}

export interface SnapshotData {
  devices: any[];
  alarms: any[];
  meta: SnapshotMeta | null;
}

import { loginUser } from '../store/userStore';

/** 当前登录用户（用于免参 API：getSnapshotTimestamp / clearAll） */
function currentUserId(): string | null {
  return loginUser.value?.username ?? null;
}

/** 打开数据库（失败/不可用返回 null，调用方降级） */
function openDB(): Promise<IDBDatabase | null> {
  return new Promise((resolve) => {
    if (typeof indexedDB === 'undefined') {
      resolve(null);
      return;
    }
    try {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains('meta')) db.createObjectStore('meta');
        if (!db.objectStoreNames.contains('devices')) db.createObjectStore('devices');
        if (!db.objectStoreNames.contains('alarms')) db.createObjectStore('alarms', { autoIncrement: true });
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => {
        console.warn('[PWA] 快照库打开失败（降级为无快照）', req.error);
        resolve(null);
      };
      req.onblocked = () => resolve(null);
    } catch (e) {
      console.warn('[PWA] 快照库打开异常（降级为无快照）', e);
      resolve(null);
    }
  });
}

function requestAsPromise<T>(req: IDBRequest): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result as T);
    req.onerror = () => reject(req.error);
  });
}

async function withStore<T>(
  storeName: string,
  mode: IDBTransactionMode,
  fn: (store: IDBObjectStore) => IDBRequest | void
): Promise<T | null> {
  const db = await openDB();
  if (!db) return null;
  try {
    return await new Promise<T | null>((resolve) => {
      const tx = db.transaction(storeName, mode);
      const store = tx.objectStore(storeName);
      let result: unknown = null;
      const req = fn(store);
      if (req) {
        req.onsuccess = () => {
          result = req.result;
        };
      }
      tx.oncomplete = () => {
        db.close();
        resolve(result as T | null);
      };
      tx.onerror = () => {
        console.warn('[PWA] 快照事务失败', tx.error);
        db.close();
        resolve(null);
      };
      tx.onabort = () => {
        db.close();
        resolve(null);
      };
    });
  } catch (e) {
    console.warn('[PWA] 快照操作异常（降级）', e);
    try { db.close(); } catch { /* ignore */ }
    return null;
  }
}

/** 批量写入设备快照（全量替换该用户的设备集合） */
export async function putDevices(userId: string, devices: any[]): Promise<boolean> {
  if (!userId) return false;
  const db = await openDB();
  if (!db) return false;
  try {
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction('devices', 'readwrite');
      const store = tx.objectStore('devices');
      // 先清该用户旧快照再写入（设备可能被删除，避免残留僵尸记录）
      const keysReq = store.getAllKeys();
      keysReq.onsuccess = () => {
        const prefix = `u${userId}:`;
        keysReq.result
          .filter((k) => typeof k === 'string' && (k as string).startsWith(prefix))
          .forEach((k) => store.delete(k));
        devices.forEach((d, i) => {
          if (d?.id == null) return;
          store.put(d, `${prefix}${d.id}:${i}`);
        });
      };
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
    db.close();
    return true;
  } catch (e) {
    console.warn('[PWA] 设备快照写入失败（降级）', e);
    try { db.close(); } catch { /* ignore */ }
    return false;
  }
}

/** 批量追加报警记录（环形保留最近 ALARM_KEEP 条，仅统计该用户） */
export async function appendAlarms(userId: string, records: any[]): Promise<boolean> {
  if (!userId || records.length === 0) return true;
  const db = await openDB();
  if (!db) return false;
  try {
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction('alarms', 'readwrite');
      const store = tx.objectStore('alarms');
      records.forEach((r) => {
        if (r) store.put({ u: userId, r });
      });
      // 超量清理：按倒序游标保留该用户最近 ALARM_KEEP 条
      const idxKeys: Array<{ key: IDBValidKey; ts: number }> = [];
      const cursorReq = store.openCursor();
      cursorReq.onsuccess = () => {
        const cursor = cursorReq.result;
        if (!cursor) return;
        const val = cursor.value as { u: string; r: any };
        if (val?.u === userId) {
          idxKeys.push({ key: cursor.key, ts: tsOf(val.r) });
        }
        cursor.continue();
      };
      tx.oncomplete = () => {
        idxKeys.sort((a, b) => b.ts - a.ts);
        idxKeys.slice(ALARM_KEEP).forEach(({ key }) => store.delete(key));
        resolve();
      };
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
    db.close();
    return true;
  } catch (e) {
    console.warn('[PWA] 报警快照写入失败（降级）', e);
    try { db.close(); } catch { /* ignore */ }
    return false;
  }
}

function tsOf(r: any): number {
  const t = r?.triggeredAt ? new Date(r.triggeredAt).getTime() : NaN;
  return Number.isNaN(t) ? 0 : t;
}

/** 更新快照时间戳（横幅「数据截至」来源） */
export async function touchMeta(userId: string, savedAtUtc: string): Promise<boolean> {
  if (!userId) return false;
  const meta: SnapshotMeta = { userId, savedAtUtc, swVersion: '1' };
  const res = await withStore('meta', 'readwrite', (store) => {
    store.put(meta, `u${userId}:current`);
  });
  return res !== null;
}

/** 一次性读取该用户全部快照（devices + alarms + meta） */
export async function readSnapshot(userId: string): Promise<SnapshotData | null> {
  if (!userId) return null;
  const devices = await readDevices(userId);
  const alarms = await readAlarms(userId);
  const meta = await readMeta(userId);
  return { devices, alarms, meta };
}

async function readDevices(userId: string): Promise<any[]> {
  const db = await openDB();
  if (!db) return [];
  try {
    const rows = await new Promise<any[]>((resolve, reject) => {
      const range = IDBKeyRange.bound(`u${userId}:`, `u${userId}:\uffff`);
      const req = db.transaction('devices', 'readonly').objectStore('devices').getAll(range);
      req.onsuccess = () => resolve(req.result as any[]);
      req.onerror = () => reject(req.error);
    });
    return (rows ?? []).filter((v) => v != null);
  } catch (e) {
    console.warn('[PWA] 设备快照读取失败（降级）', e);
    return [];
  } finally {
    try { db.close(); } catch { /* ignore */ }
  }
}

async function readAlarms(userId: string): Promise<any[]> {
  const rows = await withStore<any[]>('alarms', 'readonly', (store) => store.getAll());
  if (!Array.isArray(rows)) return [];
  return rows
    .filter((v: any) => v?.u === userId && v?.r)
    .map((v: any) => v.r)
    .sort((a: any, b: any) => tsOf(b) - tsOf(a));
}

async function readMeta(userId: string): Promise<SnapshotMeta | null> {
  const meta = await withStore<SnapshotMeta>('meta', 'readonly', (store) =>
    store.get(`u${userId}:current`)
  );
  return meta ?? null;
}

/** 当前用户快照时间戳（OfflineBanner「数据截至」），无快照返回 null */
export async function getSnapshotTimestamp(): Promise<string | null> {
  const uid = currentUserId();
  if (!uid) return null;
  const meta = await readMeta(uid);
  return meta?.savedAtUtc ?? null;
}

/** 清空整个快照库（登出/换号/401 三路径统一调用，防线二；命名空间为防线三兜底） */
export async function clearAll(): Promise<void> {
  const db = await openDB();
  if (!db) return;
  try {
    await new Promise<void>((resolve, reject) => {
      const names = Array.from(db.objectStoreNames);
      const tx = db.transaction(names, 'readwrite');
      names.forEach((n) => tx.objectStore(n).clear());
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  } catch (e) {
    console.warn('[PWA] 快照库清空失败', e);
  } finally {
    try { db.close(); } catch { /* ignore */ }
  }
}
