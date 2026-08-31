/**
 * 页面引用归一化（nav-menu 跳转 / 按钮跳转共用）。
 *
 * 背景：页面 id 存在双轨——本地新建时为 `page-{时间戳}`，落库重新加载后变为
 * `srv-{serverId}`。跳转目标（targetPageId）若在页面落库前配置，存的是本地 id，
 * 重新加载后与 `srv-N` 永远失配，导致跳转静默失败。
 *
 * 约定：
 *  - 新配置的目标一律存 `srv-{serverId}`（跨会话稳定）；
 *  - 比较时归一化：`srv-5` → `5`，其余原样返回；
 *  - 历史遗留的 `page-{时间戳}` 引用无法反查 serverId，需用户重配一次（按设计接受）。
 */

/** 提取稳定比较键：'srv-5' → '5'；'page-xxx' → 'page-xxx'；空值 → null */
export const normalizePageRef = (ref: string | null | undefined): string | null => {
  if (!ref) return null;
  const m = /^srv-(\d+)$/.exec(ref);
  return m ? m[1] : ref;
};

/** 两个页面引用是否指向同一页面（归一化后比较） */
export const isSamePageRef = (a: string | null | undefined, b: string | null | undefined): boolean => {
  const ka = normalizePageRef(a);
  const kb = normalizePageRef(b);
  return ka !== null && kb !== null && ka === kb;
};
