import { ref } from 'vue';

/**
 * 全局共享动画帧时钟：单实例 rAF，多组件消费同一 ticks 计数。
 * 解决「每个运行态器件独立跑 60fps rAF 循环」导致的百组件 = 百个回调问题。
 * 生产收敛为 1 个 rAF 驱动所有动画器件的逐帧更新（Vue 按依赖自动分发）。
 */
export const ticks = ref(0);

let animId: number | null = null;
let subscribers = 0;

const tick = () => {
  ticks.value = (ticks.value + 1) % 100000;
  animId = requestAnimationFrame(tick);
};

export const subscribeAnimation = () => {
  if (++subscribers === 1) animId = requestAnimationFrame(tick);
};

export const unsubscribeAnimation = () => {
  if (--subscribers <= 0) {
    subscribers = 0;
    if (animId) {
      cancelAnimationFrame(animId);
      animId = null;
    }
  }
};