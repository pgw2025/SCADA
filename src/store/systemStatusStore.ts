import { ref } from 'vue';
import { ServerStatus } from '../types';

export const serverStatus = ref<ServerStatus>({
  cpuUsage: 0,
  memUsage: 0,
  diskLoadPercentage: 0,
  networkIn: 0,
  networkOut: 0,
  uptimeDays: 0,
  uptimeHours: 0,
  uptimeMins: 0,
  pollFreq: 0,
  totalPollPackets: 0,
  disks: [],
});
