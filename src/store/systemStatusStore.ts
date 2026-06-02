import { ref } from 'vue';

export const serverStatus = ref({
  cpuUsage: 14.5,
  memUsage: 48.2,
  diskUsage: 61.4,
  networkIn: 88.4,
  networkOut: 245.1,
  uptimeDays: 14,
  uptimeHours: 5,
  uptimeMins: 32,
  pollFreq: 1200,
  totalPollPackets: 284145,
});
