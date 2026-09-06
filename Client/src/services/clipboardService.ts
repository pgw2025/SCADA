import { HMIComponent } from '../types';

/**
 * 应用内剪贴板（纯前端内存，不写系统剪贴板）。
 *
 * - 通过深拷贝保存组件，避免粘贴副本与原件共享 props 引用。
 * - setClipboard 时重置粘贴计数；每次粘贴整体偏移 (n*20, n*20)，便于连续粘贴不重叠。
 * - 切换工程/页面不清空，剪贴可跨页粘贴（粘贴永远新建组件，无快照污染风险）。
 */

let _data: HMIComponent[] | null = null;
let _pasteCount = 0;

const deepClone = <T>(val: T): T => JSON.parse(JSON.stringify(val));

export function setClipboard(comps: HMIComponent[]): void {
  _data = deepClone(comps);
  _pasteCount = 0;
}

export function getClipboard(): HMIComponent[] {
  return _data ? deepClone(_data) : [];
}

export function clearClipboard(): void {
  _data = null;
  _pasteCount = 0;
}

export function hasClipboard(): boolean {
  return !!_data && _data.length > 0;
}

/** 返回本次粘贴的整体偏移像素 (dx, dy)，内部累加粘贴计数 */
export function nextPasteOffset(): { x: number; y: number } {
  const offset = { x: _pasteCount * 20, y: _pasteCount * 20 };
  _pasteCount += 1;
  return offset;
}