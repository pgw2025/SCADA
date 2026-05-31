import { HMIComponent } from './types';

export interface Template {
  id: string;
  name: string;
  description: string;
  components: HMIComponent[];
}

export const TEMPLATES: Template[] = [
  {
    id: 'wastewater',
    name: '智能污水净化及储蓄系统',
    description: '模拟双水箱联动、主泵输送及管道阀门实时反馈，演示闭环液位控制。',
    components: [
      {
        id: 'tank-污水',
        type: 'tank',
        name: '原污水储液罐',
        x: 80,
        y: 120,
        width: 140,
        height: 180,
        label: '原污水储罐',
        bindField: 'tank_level',
        zIndex: 1,
        props: { fillColor: '#8b8b8b', strokeColor: '#5a5a5a', maxValue: 100 }
      },
      {
        id: 'pipe-water-1',
        type: 'pipe-h',
        name: '污水流出管',
        x: 220,
        y: 240,
        width: 90,
        height: 16,
        label: '管道A',
        bindField: 'flow_rate',
        zIndex: 2,
        props: { activeColor: '#4f46e5', inactiveColor: '#94a3b8' }
      },
      {
        id: 'valve-1',
        type: 'valve',
        name: '泄压电磁阀',
        x: 310,
        y: 218,
        width: 60,
        height: 60,
        label: '进水阀 KV01',
        bindField: 'valve_state',
        zIndex: 5,
        props: { activeColor: '#10b981', inactiveColor: '#ef4444' }
      },
      {
        id: 'pipe-water-2',
        type: 'pipe-h',
        name: '流速控制管',
        x: 370,
        y: 240,
        width: 80,
        height: 16,
        label: '管道B',
        bindField: 'flow_rate',
        zIndex: 2,
        props: { activeColor: '#4f46e5', inactiveColor: '#94a3b8' }
      },
      {
        id: 'pump-1',
        type: 'pump',
        name: '主离心泵 P101',
        x: 450,
        y: 218,
        width: 64,
        height: 64,
        label: '主输送泵 P101',
        bindField: 'pump_state',
        zIndex: 5,
        props: { activeColor: '#10b981', inactiveColor: '#64748b' }
      },
      {
        id: 'pipe-water-3',
        type: 'pipe-h',
        name: '最终输送管',
        x: 514,
        y: 240,
        width: 116,
        height: 16,
        label: '管道C',
        bindField: 'flow_rate',
        zIndex: 2,
        props: { activeColor: '#10b981', inactiveColor: '#94a3b8' }
      },
      {
        id: 'tank-净化',
        type: 'tank',
        name: '净化处理水罐',
        x: 630,
        y: 120,
        width: 140,
        height: 180,
        label: '净化池',
        bindField: 'purified_level',
        zIndex: 2,
        props: { fillColor: '#38bdf8', strokeColor: '#0284c7', maxValue: 100 }
      },
      {
        id: 'dial-flow',
        type: 'gauge-dial',
        name: '管线瞬时温',
        x: 420,
        y: 340,
        width: 120,
        height: 120,
        label: '输水管温 (°C)',
        bindField: 'boiler_temp',
        zIndex: 3,
        props: { maxValue: 120, thresholdMax: 95 }
      },
      {
        id: 'flow-rate-led',
        type: 'led',
        name: '泵运行报警器',
        x: 467,
        y: 150,
        width: 32,
        height: 32,
        label: '泵超标报警',
        bindField: 'alarm_status',
        zIndex: 4,
        props: { activeColor: '#ef4444', inactiveColor: '#10b981' }
      },
      {
        id: 'digit-purified',
        type: 'digital-val',
        name: '主处理数值显示',
        x: 645,
        y: 330,
        width: 110,
        height: 60,
        label: '精细液位',
        bindField: 'purified_level',
        zIndex: 4,
        props: { unit: '%' }
      },
      {
        id: 'digit-raw',
        type: 'digital-val',
        name: '原污水液位数值',
        x: 95,
        y: 330,
        width: 110,
        height: 60,
        label: '原水液位',
        bindField: 'tank_level',
        zIndex: 4,
        props: { unit: '%' }
      },
      {
        id: 'intro-title',
        type: 'text',
        name: '场景说明',
        x: 280,
        y: 40,
        width: 400,
        height: 50,
        label: '污水高倍沉淀净化示范系统 V1.0',
        bindField: '',
        zIndex: 1,
        props: { fontSize: 20, bold: true, align: 'center' }
      }
    ]
  },
  {
    id: 'thermal',
    name: '热力发电站高压锅炉中控台',
    description: '核心反应堆蒸汽温度、炉内高压压力监控。当温度达到警告限度时触发报警灯。',
    components: [
      {
        id: 'boiler-main',
        type: 'boiler',
        name: '高压反应堆 boiler',
        x: 100,
        y: 140,
        width: 150,
        height: 200,
        label: '1#高压燃煤锅炉',
        bindField: 'boiler_temp',
        zIndex: 1,
        props: { fillColor: '#f97316', strokeColor: '#c2410c' }
      },
      {
        id: 'pipe-steam-1',
        type: 'pipe-h',
        name: '排汽横管',
        x: 250,
        y: 180,
        width: 120,
        height: 16,
        label: '主除尘汽道',
        bindField: 'flow_rate',
        zIndex: 2,
        props: { activeColor: '#ea580c', inactiveColor: '#94a3b8' }
      },
      {
        id: 'pipe-steam-2',
        type: 'pipe-v',
        name: '排汽竖管',
        x: 370,
        y: 180,
        width: 16,
        height: 120,
        label: '汽水分离上升管',
        bindField: 'flow_rate',
        zIndex: 2,
        props: { activeColor: '#ea580c', inactiveColor: '#94a3b8' }
      },
      {
        id: 'gauge-p',
        type: 'gauge-dial',
        name: '蒸汽压力表',
        x: 500,
        y: 140,
        width: 130,
        height: 130,
        label: '蒸汽压力 (kPa)',
        bindField: 'boiler_press',
        zIndex: 3,
        props: { maxValue: 120, thresholdMax: 85 }
      },
      {
        id: 'level-boiler',
        type: 'gauge-level',
        name: '液位传感器',
        x: 358,
        y: 300,
        width: 40,
        height: 140,
        label: '锅炉水位',
        bindField: 'tank_level',
        zIndex: 3,
        props: { activeColor: '#10b981', inactiveColor: '#dc2626' }
      },
      {
        id: 'alarm-temp-led',
        type: 'led',
        name: '锅炉高温报警',
        x: 160,
        y: 80,
        width: 32,
        height: 32,
        label: '极限超温',
        bindField: 'alarm_status',
        zIndex: 4,
        props: { activeColor: '#ef4444', inactiveColor: '#22c55e' }
      },
      {
        id: 'val-boiler-temp',
        type: 'digital-val',
        name: '炉膛核心温度数值',
        x: 320,
        y: 80,
        width: 130,
        height: 60,
        label: '核心实测温度',
        bindField: 'boiler_temp',
        zIndex: 4,
        props: { unit: '℃', thresholdMax: 95 }
      },
      {
        id: 'thermal-chart',
        type: 'trend-chart',
        name: '多参数趋势图',
        x: 480,
        y: 290,
        width: 300,
        height: 170,
        label: '核心压力/温度24H变化曲线',
        bindField: 'boiler_temp',
        zIndex: 4,
        props: {}
      }
    ]
  },
  {
    id: 'conveyor',
    name: '物料分拣与仓储流水线',
    description: '模拟食品或药件物料传送带传输状态，附带传输速度指示及最终落灰仓监控。',
    components: [
      {
        id: 'conveyor-main',
        type: 'conveyor',
        name: '配料传送带',
        x: 100,
        y: 240,
        width: 380,
        height: 48,
        label: '1号物料输送传送带',
        bindField: 'conveyor_speed',
        zIndex: 2,
        props: { fillColor: '#4b5563', activeColor: '#10b981' }
      },
      {
        id: 'tank-receiver',
        type: 'tank',
        name: '下料储槽罐',
        x: 480,
        y: 160,
        width: 140,
        height: 160,
        label: '集料过渡罐',
        bindField: 'tank_level',
        zIndex: 1,
        props: { fillColor: '#4ade80', strokeColor: '#16a34a' }
      },
      {
        id: 'gauge-motor-rpm',
        type: 'gauge-dial',
        name: '变频电机转速',
        x: 120,
        y: 80,
        width: 130,
        height: 130,
        label: '皮带速度 (m/s)',
        bindField: 'conveyor_speed',
        zIndex: 3,
        props: { maxValue: 150 }
      },
      {
        id: 'motor-status-text',
        type: 'text',
        name: '中台文字',
        x: 280,
        y: 110,
        width: 160,
        height: 35,
        label: 'PLC控制器状态: 运行',
        bindField: '',
        zIndex: 1,
        props: { fontSize: 13, bold: true }
      },
      {
        id: 'motor-status-val',
        type: 'digital-val',
        name: '最终集料量',
        x: 640,
        y: 180,
        width: 120,
        height: 60,
        label: '集料量监控',
        bindField: 'tank_level',
        zIndex: 4,
        props: { unit: 'kg' }
      }
    ]
  }
];
