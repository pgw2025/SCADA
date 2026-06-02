import { ref } from 'vue';
import { Device } from '../types';

export const devices = ref<Device[]>([]);

export const setDevices = (newDevices: Device[]) => {
  devices.value = newDevices;
};
