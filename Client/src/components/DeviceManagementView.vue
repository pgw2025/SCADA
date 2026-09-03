<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { devices } from '../store/deviceStore';
import { areas } from '../store/areaStore';
import { syncAreas, createAreaAndSync, updateAreaAndSync, deleteAreaAndSync, getAreaTree, getSubtreeDeviceIds, AreaFormData } from '../services/areaService';
import AreaTree from './AreaTree.vue';
import { dataModels, addLog, fetchDataModelsFromBackend } from '../store/index';
import { syncDevices, createDeviceAndSync, updateDeviceAndSync, deleteDeviceAndSync, setDeviceEnabledAndSync } from '../services/deviceService';
import { fetchControllerOptions } from '../api/controllerApi';
import { fetchDeviceConnections, fetchDeviceConnectionById, createDeviceConnection } from '../api/connectionApi';
import { systemConfig } from '../store/configStore';
import { startBackendPolling, stopBackendPolling } from '../services/pollService';

onMounted(() => {
  syncAreas();
  loadAreaTree();
  syncDevices();
  // 模型列表在 App.vue 启动时(登录前)拉取会因无 token 而 401,登录后不会重拉;
  // 这里兜底刷新,确保 dataModels 就绪,设备卡片的"数据模型"才能正确显示名称。
  fetchDataModelsFromBackend();
  startBackendPolling();
});

onUnmounted(() => {
  stopBackendPolling();
});
import { 
  Cpu, 
  MapPin, 
  Plus, 
  Trash2, 
  Edit3, 
  Braces, 
  Server, 
  Sliders, 
  X, 
  Check, 
  ToggleLeft, 
  Info,
  Filter,
  ChevronRight,
  ChevronDown,
  Link2,
  Unlink2,
  Star,
  Loader2
} from 'lucide-vue-next';
import { Device, Area, AreaTreeNode, DeviceType, ControllerOption, DeviceConnection, DeviceConnectionSummary, DeviceConnectionRequest, DeviceModelBinding, protocolKeyToDeviceType } from '../types';
import {
  fetchDeviceDataModelBindings,
  bindDeviceDataModel,
  setPrimaryDeviceDataModel,
  unbindDeviceDataModel
} from '../api/deviceDataModelApi';
import { useRouter } from 'vue-router';

const router = useRouter();

// Area Form States（阶段 1 扩展为树形区域：父区域/编码/类型/排序/启用 + 编辑）
const showAreaModal = ref<boolean>(false);
const isEditingArea = ref<boolean>(false);
const editingAreaId = ref<number | null>(null);
const newAreaName = ref<string>('');
const newAreaDesc = ref<string>('');
const areaFormParentId = ref<number | null>(null);
const areaFormCode = ref<string>('');
const areaFormAreaType = ref<number>(4);
const areaFormSort = ref<number>(0);
const areaFormEnabled = ref<boolean>(true);
const areaFormErrors = ref<Record<string, string>>({});
const areaFormErrorMessage = ref<string>('');

// Device Form States
const showDeviceModal = ref<boolean>(false);
const isEditingDevice = ref<boolean>(false);
const editingDeviceId = ref<number | null>(null);
const deviceFormErrors = ref<Record<string, string>>({});
const deviceFormErrorMessage = ref<string>('');

const devName = ref<string>('');
const devKey = ref<string>('');
const devArea = ref<number>(0);
const devModel = ref<string>('');
const devType = ref<DeviceType>('OPCUA');
const devIP = ref<string>('');
const devPort = ref<string>('');
// OPC UA 端点完整地址（含 scheme 与可选路径，如 opc.tcp://192.168.1.10:4840/server）。
// 真相源是后端 ConfigJson.EndpointUrl，不要再用 ip+port 拼接，否则会丢掉路径部分。
const devEndpointUrl = ref<string>('');

const devStatus = ref<'online' | 'offline'>('online');

// S7-specific connection details
const devCpuType = ref<string>('S7-1205');
const devRack = ref<number>(0);
const devSlot = ref<number>(1);

// Virtual-specific config (matches backend VirtualConfig)
const devVirtualIntervalMs = ref<number>(1000);
const devVirtualRandomValues = ref<boolean>(true);

// ---- 阶段 5：设备-数据模型绑定管理（编辑态弹窗内「模型绑定」分区） ----
const deviceBindings = ref<DeviceModelBinding[]>([]);   // 当前编辑设备的全部绑定（主模型行在前）
const bindingsLoading = ref<boolean>(false);
const bindingError = ref<string>('');
const bindTargetModelId = ref<string>('');              // 「绑定新模型」下拉：dataModels[].id（string）
const bindingBusy = ref<boolean>(false);                // 绑定/解绑/设主 进行中（按钮防抖）

/** 当前主模型绑定行（后端保证每设备至多一条 IsPrimary=true；理论上恒有，缺省时回退 modelId 语义）。 */
const primaryBinding = computed(() => deviceBindings.value.find(b => b.isPrimary) ?? null);

/** 候选绑定模型：dataModels 中未发布禁选；已绑定同一模型去重；主模型自身也已绑定故自动排除。 */
const bindableModels = computed(() => {
  const boundIds = new Set(deviceBindings.value.map(b => String(b.dataModelId)));
  return dataModels.value.filter(m =>
    m.isPublished !== false && !boundIds.has(String(m.id))
  );
});

/** 绑定面板操作入口（仅编辑态且设备已持久化时可用）。 */
const openBindings = async (deviceId: number) => {
  deviceBindings.value = [];
  bindingError.value = '';
  bindTargetModelId.value = '';
  bindingsLoading.value = true;
  try {
    deviceBindings.value = await fetchDeviceDataModelBindings(deviceId);
    // 打开面板即回填主模型下拉（正常情形与 devModel 一致；后端双写为权威）。
    // 若绑定主行与设备 ModelId 不一致（脏数据），以下拉跟随绑定主行为准并同步协议展示。
    const primary = deviceBindings.value.find(b => b.isPrimary);
    if (primary && String(primary.dataModelId) !== String(devModel.value)) {
      devModel.value = String(primary.dataModelId);
      const targetModel = dataModels.value.find(m => String(m.id) === String(primary.dataModelId));
      if (targetModel) devType.value = protocolKeyToDeviceType(targetModel.protocolKey);
      resetAdvancedOnModelChange();
    }
  } finally {
    bindingsLoading.value = false;
  }
};

/** 绑定新模型为附加模型（后端校验模型已发布；IsPrimary=false 仅作附加，不抢占主模型）。 */
const handleBindModel = async () => {
  if (editingDeviceId.value == null || !bindTargetModelId.value) return;
  const target = dataModels.value.find(m => String(m.id) === bindTargetModelId.value);
  if (!target) return;
  bindingBusy.value = true;
  bindingError.value = '';
  try {
    deviceBindings.value = await bindDeviceDataModel(editingDeviceId.value, {
      dataModelId: Number(bindTargetModelId.value),
      isPrimary: false
    });
    bindTargetModelId.value = '';
    addLog('设备数据模型', `设备 [${devName.value}] 已绑定附加模型 [${target.name}]`, 'normal');
    // 绑定改变不影响 Device.ModelId（仍为主模型），但刷新设备列表保持 models 字段最新
    syncDevices();
  } catch (err: any) {
    bindingError.value = err?.response?.data?.message || err?.message || '绑定附加模型失败';
  } finally {
    bindingBusy.value = false;
  }
};

/** 设为主模型（后端事务内降级旧主 + 同步 Device.ModelId + 热重载运行时）。 */
const handleSetPrimary = async (binding: DeviceModelBinding) => {
  if (editingDeviceId.value == null || binding.isPrimary) return;
  if (!confirm(`确认将 [${binding.name || binding.code || binding.dataModelId}] 设为主模型？\n主模型决定设备的协议与采集变量集，启用中的设备将自动热重载。`)) return;
  bindingBusy.value = true;
  bindingError.value = '';
  try {
    deviceBindings.value = await setPrimaryDeviceDataModel(editingDeviceId.value, binding.dataModelId);
    const primary = deviceBindings.value.find(b => b.isPrimary);
    if (primary) {
      // 主模型下拉同步；协议随模型推导（protocolKey → DeviceType），高级模式连接兼容集按新协议重算。
      devModel.value = String(primary.dataModelId);
      const targetModel = dataModels.value.find(m => String(m.id) === String(primary.dataModelId));
      if (targetModel) devType.value = protocolKeyToDeviceType(targetModel.protocolKey);
      resetAdvancedOnModelChange();
    }
    addLog('设备数据模型', `设备 [${devName.value}] 主模型已切换为 [${binding.name || binding.code || binding.dataModelId}]`, 'warning');
    syncDevices();
  } catch (err: any) {
    bindingError.value = err?.response?.data?.message || err?.message || '切换主模型失败';
  } finally {
    bindingBusy.value = false;
  }
};

/** 解绑附加模型（主模型不可解绑——后端拒绝；先切换主模型再解绑）。 */
const handleUnbind = async (binding: DeviceModelBinding) => {
  if (editingDeviceId.value == null || binding.isPrimary) return;
  if (!confirm(`确认解绑附加模型 [${binding.name || binding.code || binding.dataModelId}]？\n该模型将不再与设备关联（主模型不受影响）。`)) return;
  bindingBusy.value = true;
  bindingError.value = '';
  try {
    deviceBindings.value = await unbindDeviceDataModel(editingDeviceId.value, binding.dataModelId);
    addLog('设备数据模型', `设备 [${devName.value}] 已解绑附加模型 [${binding.name || binding.code || binding.dataModelId}]`, 'warning');
    syncDevices();
  } catch (err: any) {
    bindingError.value = err?.response?.data?.message || err?.message || '解绑模型失败';
  } finally {
    bindingBusy.value = false;
  }
};

// ---- 阶段 3.6：连接方式（快速模式=内联配置/后端自动维护专属连接；高级模式=显式附加控制器+连接，可跨设备共享） ----
type DeviceConnMode = 'quick' | 'advanced';
const connMode = ref<DeviceConnMode>('quick');
const NEW_CONNECTION_SENTINEL = 0; // 高级模式连接下拉「＋ 新建独立连接」的占位 value

// 控制器/连接级联数据源（高级模式）
const controllerOptions = ref<ControllerOption[]>([]);
const controllerConnections = ref<DeviceConnection[]>([]);
const advancedControllerId = ref<number | null>(null);
const advancedConnectionId = ref<number | null>(null);
const advancedConnectionsLoading = ref<boolean>(false);
const newConnectionName = ref<string>('');

// 编辑态：设备当前已关联连接的摘要（用于展示连接信息与共享警示）
const editingConnectionLabel = ref<string>('');
const editingConnectionSharedBy = ref<number>(0);

/** 当前表单选中的数据模型（dataModels[].id 为 string，devModel 下拉值为 string，直接命中）。 */
const chosenDeviceModel = computed(() => dataModels.value.find(m => m.id === devModel.value) ?? null);

/** 当前表单选中数据模型的协议 ID（高级模式连接兼容性过滤依据）。 */
const chosenModelProtocolId = computed(() => chosenDeviceModel.value?.protocolId ?? null);

/** 依据模型推导的展示协议；模型缺失时回退 devType（与 onModelChange 一致）。 */
const deviceTypeFromModel = computed(() =>
  chosenDeviceModel.value ? protocolKeyToDeviceType(chosenDeviceModel.value.protocolKey) : devType.value
);

/** 当前选中控制器下「可用（启用且协议匹配所选模型）」的连接；其余连接在下列表中禁用标注。 */
const compatibleConnections = computed(() => {
  const protoId = chosenModelProtocolId.value;
  return controllerConnections.value.filter(c => c.isEnabled && (protoId == null || c.protocolId === protoId));
});

const connectionEndpointLabel = (c: { host?: string | null; port?: number | null; protocolId: number }): string => {
  if (c.host) return c.port != null ? `${c.host}:${c.port}` : c.host;
  return '—';
};

/** 拉取控制器下拉数据（首次进入高级模式时惰性加载，缓存复用）。 */
const loadControllerOptions = async () => {
  if (systemConfig.value.isSimulationActive) {
    controllerOptions.value = [];
    return;
  }
  if (controllerOptions.value.length > 0) return;
  controllerOptions.value = await fetchControllerOptions();
};

/** 拉取所选控制器下的连接列表。 */
const loadConnectionsForController = async (controllerId: number | null) => {
  if (controllerId == null) {
    controllerConnections.value = [];
    return;
  }
  advancedConnectionsLoading.value = true;
  try {
    controllerConnections.value = await fetchDeviceConnections(controllerId);
  } finally {
    advancedConnectionsLoading.value = false;
  }
};

/** 高级模式切换入口：首次进入拉控制器，已选控制器则拉其连接。 */
const onSwitchConnMode = (mode: DeviceConnMode) => {
  connMode.value = mode;
  if (mode === 'advanced') {
    loadControllerOptions();
    if (advancedControllerId.value != null && controllerConnections.value.length === 0) {
      loadConnectionsForController(advancedControllerId.value);
    }
  }
};

/** 控制器下拉变更：清空已选连接并重新拉取连接。 */
const onAdvancedControllerChange = async () => {
  advancedConnectionId.value = null;
  newConnectionName.value = devName.value.trim() ? `${devName.value.trim()} 连接` : '独立连接';
  await loadConnectionsForController(advancedControllerId.value);
};

/** 数据模型变更后重置高级模式选择（协议兼容集可能变化）。 */
const resetAdvancedOnModelChange = () => {
  if (connMode.value !== 'advanced' || advancedControllerId.value == null) return;
  advancedConnectionId.value = null;
  loadConnectionsForController(advancedControllerId.value);
};

/** 将权威连接配置原文回填到快速模式表单字段（键名兼容 PascalCase / camelCase）。 */
const applyConfigJsonToQuickFields = (configJson: string | null | undefined) => {
  if (!configJson || !configJson.trim()) return;
  let parsed: Record<string, any>;
  try {
    parsed = JSON.parse(configJson);
  } catch {
    return;
  }
  const pick = (names: string[]) => {
    for (const n of names) {
      if (parsed[n] !== undefined) return parsed[n];
    }
    return undefined;
  };
  const type = deviceTypeFromModel.value;
  if (type === 'OPCUA') {
    const endpoint = pick(['EndpointUrl', 'endpointUrl']);
    if (typeof endpoint === 'string' && endpoint) devEndpointUrl.value = endpoint;
  } else if (type === 'S7') {
    const ip = pick(['IpAddress', 'ipAddress']);
    if (typeof ip === 'string' && ip) devIP.value = ip;
    const port = pick(['Port', 'port']);
    if (typeof port === 'number') devPort.value = String(port);
    const cpu = pick(['CpuType', 'cpuType']);
    if (typeof cpu === 'string' && cpu) devCpuType.value = cpu;
    const rack = pick(['Rack', 'rack']);
    if (typeof rack === 'number') devRack.value = rack;
    const slot = pick(['Slot', 'slot']);
    if (typeof slot === 'number') devSlot.value = slot;
  } else if (type === 'Virtual') {
    const interval = pick(['IntervalMs', 'intervalMs']);
    if (typeof interval === 'number') devVirtualIntervalMs.value = interval;
    const random = pick(['RandomValues', 'randomValues']);
    if (typeof random === 'boolean') devVirtualRandomValues.value = random;
  }
  // MQTT 无可见参数面板，无需回填
};

/** 编辑态：设备已关联连接时拉取权威连接配置（Connection.ConfigJson 为真相源），
 *  覆盖表单字段，避免设备自身 JsonConfig 镜像在「共享连接由其它设备修改」后过期导致误回退。 */
const refreshEditConnectionFields = async (device: Device) => {
  if (device.connectionId == null) return;
  const conn = await fetchDeviceConnectionById(device.connectionId);
  if (conn?.configJson) applyConfigJsonToQuickFields(conn.configJson);
};

// Active view
const activeSection = ref<'list' | 'areas'>('list');

// Expanded areas state for collapsible panels
const expandedAreas = ref<Set<number>>(new Set());

// ---- 阶段 1：区域树 + 设备区域筛选 ----
const areaTree = ref<AreaTreeNode[]>([]);
// 区域管理树：展开状态（按节点 ID 记忆）
const areaManageExpanded = ref<Set<number>>(new Set());
// 设备列表区域筛选：null = 全部区域（保持原有行为）；选中某区域后可按"仅当前区域/含子区域"过滤设备。
const filterAreaId = ref<number | null>(null);
const includeSubareas = ref<boolean>(false);
const subtreeDeviceIds = ref<Set<number>>(new Set());

/** 从后端拉取区域树（用于区域管理树、筛选下拉、设备表单区域下拉）。 */
const loadAreaTree = async () => {
  areaTree.value = await getAreaTree();
  // 默认展开所有含子节点的层级，方便查看完整树
  areaTreeRows.value.forEach(r => {
    if (r.node.children?.length) areaManageExpanded.value.add(r.node.id);
  });
};

/** 区域树 → 扁平行（含深度），供下拉/表格渲染。 */
const flattenAreaTree = (nodes: AreaTreeNode[], depth = 0, out: { node: AreaTreeNode; depth: number }[] = []): { node: AreaTreeNode; depth: number }[] => {
  nodes.forEach(n => {
    out.push({ node: n, depth });
    if (n.children?.length) flattenAreaTree(n.children, depth + 1, out);
  });
  return out;
};

/** 收集某节点的全部子孙 ID（不含自身），用于编辑区域时禁止选自己/后代为父。 */
const collectDescendantIds = (nodes: AreaTreeNode[], targetId: number): Set<number> => {
  const out = new Set<number>();
  const findTarget = (list: AreaTreeNode[]): AreaTreeNode | null => {
    for (const n of list) {
      if (n.id === targetId) return n;
      const found = findTarget(n.children ?? []);
      if (found) return found;
    }
    return null;
  };
  const target = findTarget(nodes);
  if (!target) return out;
  const stack = [...(target.children ?? [])];
  while (stack.length) {
    const cur = stack.pop()!;
    out.add(cur.id);
    stack.push(...(cur.children ?? []));
  }
  return out;
};

/** 区域树 → 扁平行（含深度），供下拉/表格渲染。 */
const areaTreeRows = computed(() => flattenAreaTree(areaTree.value));

/** 区域管理树可见行：根节点始终显示，子节点仅当父节点展开时显示。 */
const visibleAreaRows = computed(() => {
  const visibleSet = new Set<number>();
  const out: { node: AreaTreeNode; depth: number }[] = [];
  areaTreeRows.value.forEach(r => {
    const parentVisible = r.node.parentId == null || (visibleSet.has(r.node.parentId) && areaManageExpanded.value.has(r.node.parentId));
    if (parentVisible) {
      visibleSet.add(r.node.id);
      out.push(r);
    }
  });
  return out;
});

const toggleAreaManage = (id: number) => {
  if (areaManageExpanded.value.has(id)) areaManageExpanded.value.delete(id);
  else areaManageExpanded.value.add(id);
};
const isAreaManageExpanded = (id: number) => areaManageExpanded.value.has(id);

// 区域类型展示文案（AreaTypeEnum）
const areaTypeLabel = (t?: number): string => {
  switch (t) {
    case 1: return '工厂';
    case 2: return '车间';
    case 3: return '产线';
    case 4: return '区域';
    case 5: return '仓库';
    default: return '区域';
  }
};

/** 设备筛选结果：未选区域时返回全部（保持原行为）。 */
const filteredDevices = computed(() => {
  if (filterAreaId.value == null) return devices.value;
  if (includeSubareas.value) {
    const ids = subtreeDeviceIds.value;
    return devices.value.filter(d => ids.has(d.id));
  }
  return devices.value.filter(d => d.areaId === filterAreaId.value);
});

/** 设备列表要渲染的分区面板：未筛选时全部区域；筛选时仅选中区域一个面板。 */
const visibleAreas = computed(() => {
  if (filterAreaId.value == null) return areas.value;
  const hit = areas.value.find(a => a.id === filterAreaId.value);
  return hit ? [hit] : [];
});

/** 某面板内展示的设备：未筛选时按区域分组；筛选时统一展示过滤结果。 */
const devicesInPanel = (areaId: number): Device[] => {
  if (filterAreaId.value == null) return devicesByArea.value[areaId] || [];
  return filteredDevices.value;
};

const onAreaFilterSelect = (node: AreaTreeNode) => {
  filterAreaId.value = node.id;
  if (includeSubareas.value) refreshSubtreeDeviceIds(node.id);
};

// 筛选下拉开关与选中回调（选中后收起下拉）
const filterDropdownOpen = ref<boolean>(false);
const onFilterSelect = (node: AreaTreeNode) => {
  onAreaFilterSelect(node);
  filterDropdownOpen.value = false;
};
const filterAreaLabel = computed(() => {
  if (filterAreaId.value == null) return '';
  return areas.value.find(a => a.id === filterAreaId.value)?.name || '';
});

const clearAreaFilter = () => {
  filterAreaId.value = null;
  includeSubareas.value = false;
  subtreeDeviceIds.value = new Set();
};

const refreshSubtreeDeviceIds = async (areaId: number) => {
  const ids = await getSubtreeDeviceIds(areaId);
  subtreeDeviceIds.value = new Set(ids);
};

// 选中区域或切换"含子区域"时刷新子树设备 ID
watch([filterAreaId, includeSubareas], ([aid, incl]) => {
  if (aid != null && incl) refreshSubtreeDeviceIds(aid);
  if (aid == null) subtreeDeviceIds.value = new Set();
});

// Computed: devices grouped by area
const devicesByArea = computed(() => {
  const grouped: Record<number, typeof devices.value> = {};
  devices.value.forEach(d => {
    if (!grouped[d.areaId]) {
      grouped[d.areaId] = [];
    }
    grouped[d.areaId].push(d);
  });
  return grouped;
});

// Toggle area expansion
const toggleArea = (areaId: number) => {
  if (expandedAreas.value.has(areaId)) {
    expandedAreas.value.delete(areaId);
  } else {
    expandedAreas.value.add(areaId);
  }
};

// Check if area is expanded
const isAreaExpanded = (areaId: number) => expandedAreas.value.has(areaId);

// Expand all areas
const expandAllAreas = () => {
  areas.value.forEach(a => expandedAreas.value.add(a.id));
};

// Collapse all areas
const collapseAllAreas = () => {
  expandedAreas.value.clear();
};

// ---- 区域管理：新增/编辑/删除（阶段 1 树形） ----

// 打开新增区域弹窗
const openAddAreaModal = () => {
  isEditingArea.value = false;
  editingAreaId.value = null;
  newAreaName.value = '';
  newAreaDesc.value = '';
  areaFormParentId.value = null;
  areaFormCode.value = '';
  areaFormAreaType.value = 4;
  areaFormSort.value = 0;
  areaFormEnabled.value = true;
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';
  showAreaModal.value = true;
};

// 打开编辑区域弹窗
const openEditAreaModal = (area: Area) => {
  isEditingArea.value = true;
  editingAreaId.value = area.id ?? null;
  newAreaName.value = area.name;
  newAreaDesc.value = area.description || '';
  areaFormParentId.value = area.parentId ?? null;
  areaFormCode.value = area.code || '';
  areaFormAreaType.value = area.areaType ?? 4;
  areaFormSort.value = area.sort ?? 0;
  areaFormEnabled.value = area.isEnabled ?? true;
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';
  showAreaModal.value = true;
};

// 父区域下拉选项：全部区域（缩进层级），编辑时排除自身及其子孙（防环）。
const areaFormParentOptions = computed(() => {
  const rows = flattenAreaTree(areaTree.value);
  if (!isEditingArea.value || editingAreaId.value == null) return rows;
  const banned = collectDescendantIds(areaTree.value, editingAreaId.value);
  banned.add(editingAreaId.value);
  return rows.filter(r => !banned.has(r.node.id));
});

const handleSaveArea = async () => {
  areaFormErrors.value = {};
  areaFormErrorMessage.value = '';

  if (!newAreaName.value.trim()) {
    areaFormErrors.value = { Name: '区域名称不能为空' };
    return;
  }

  const formData: AreaFormData = {
    name: newAreaName.value,
    description: newAreaDesc.value,
    parentId: areaFormParentId.value,
    code: areaFormCode.value,
    areaType: areaFormAreaType.value,
    sort: areaFormSort.value,
    isEnabled: areaFormEnabled.value
  };

  const result = isEditingArea.value && editingAreaId.value != null
    ? await updateAreaAndSync(editingAreaId.value, formData)
    : await createAreaAndSync(formData);

  if (result.success) {
    addLog('设备管理', isEditingArea.value
      ? `更新了工艺区域 [${newAreaName.value}]`
      : `添加新工艺区域: [${newAreaName.value}]`, 'normal');
    showAreaModal.value = false;
    await loadAreaTree();
  } else if (result.error) {
    if (result.error.type === 'validation' && result.error.fieldErrors) {
      areaFormErrors.value = result.error.fieldErrors;
    } else {
      areaFormErrorMessage.value = result.error.message;
    }
  }
};

// Trigger delete area
const handleDeleteArea = async (id: number, name: string) => {
  const childCount = areaTreeRows.value.filter(r => r.node.parentId === id).length;
  if (childCount > 0) {
    alert(`无法删除区域 [${name}]: 该区域下仍有 ${childCount} 个子区域，请先移除或删除子区域。`);
    return;
  }
  const counts = devices.value.filter(d => d.areaId === id).length;
  if (counts > 0) {
    alert(`无法删除区域 [${name}]: 有 ${counts} 个处于连接中的工业设备已被部署在该区域内。`);
    return;
  }

  const result = await deleteAreaAndSync(id, name);
  if (result.success) {
    addLog('设备管理', `删除了工艺区域 [${name}]`, 'warning');
    await loadAreaTree();
  }
  // 失败提示由 http 拦截器统一 Toast 弹出
};

// Open Device modal
const openNewDeviceModal = () => {
  isEditingDevice.value = false;
  editingDeviceId.value = null;
  devName.value = '';
  // 设备编号改由后端按区域自动生成，前端不再手输（新增时留空触发后端生成）
  devKey.value = '';
  devArea.value = areas.value[0]?.id || 0;
  // 优先使用当前在下拉框中选中的数据模型,而不是列表中的第一个,
  // 否则当第一个模型为 OPCUA 类型时,即使选中虚拟设备也会错误显示 OPC UA 地址。
  const initialModel = dataModels.value.find(m => m.id === devModel.value) || dataModels.value[0];
  devModel.value = initialModel?.id || '';
  devType.value = initialModel ? protocolKeyToDeviceType(initialModel.protocolKey) : 'OPCUA';
  devIP.value = '192.168.1.100';
  devPort.value = '4840';
  devEndpointUrl.value = 'opc.tcp://192.168.1.10:4840';
  devStatus.value = 'online';

  // S7 init
  devCpuType.value = 'S7-1200';
  devRack.value = 0;
  devSlot.value = 1;

  // Virtual init
  devVirtualIntervalMs.value = 1000;
  devVirtualRandomValues.value = true;

  // 阶段 3.6：新增默认走快速模式（后端自动维护专属连接）；高级模式数据源留待切换时惰性加载。
  connMode.value = 'quick';
  controllerConnections.value = [];
  advancedControllerId.value = null;
  advancedConnectionId.value = null;
  advancedConnectionsLoading.value = false;
  newConnectionName.value = '';
  editingConnectionLabel.value = '';
  editingConnectionSharedBy.value = 0;

  // 阶段 5：新增态绑定表随后端 CreateAsync 双写一条主绑定，面板不适用（清空避免残留上一设备）。
  deviceBindings.value = [];
  bindTargetModelId.value = '';
  bindingError.value = '';
  bindingBusy.value = false;

  // 依据当前选中模型类型,统一刷新协议相关默认值(IP/端口/虚拟参数等),
  // 避免上一个设备的残留值(如 OPC UA 的 4840 端口)污染本次初始化。
  onModelChange();

  showDeviceModal.value = true;
};

// Automatically adjust device protocol based on chosen Data Model
const onModelChange = () => {
  const model = dataModels.value.find(m => m.id === devModel.value);
  if (model) {
    // 协议真相源在 Protocol 实体，由 model.protocolKey 派生设备类型
    devType.value = protocolKeyToDeviceType(model.protocolKey);
    if (devType.value === 'OPCUA') {
      devPort.value = '4840';
      devIP.value = '192.168.1.10';
      devEndpointUrl.value = 'opc.tcp://192.168.1.10:4840';
    } else if (devType.value === 'S7') {
      devPort.value = '102';
      devIP.value = '192.168.1.12';
      devCpuType.value = 'S7-1200';
      devRack.value = 0;
      devSlot.value = 1;
    } else if (devType.value === 'MQTT') {
    } else if (devType.value === 'Virtual') {
      devVirtualIntervalMs.value = 1000;
      devVirtualRandomValues.value = true;
    }
  }
  // 阶段 3.6：模型协议变化会改变高级模式的可用连接集，重置连接选择（含"新建连接"默认名）。
  resetAdvancedOnModelChange();
};

const openEditDeviceModal = (device: Device) => {
  isEditingDevice.value = true;
  editingDeviceId.value = device.id;
  
  devName.value = device.name;
  devKey.value = device.key;
  devArea.value = Number(device.areaId);
  // dataModels[].id 为 string(后端 int 序列化后由 modelApi 统一 String 化),
  // device.modelId 为 number,须归一为 string 才能命中下拉框 option 与后续 find。
  devModel.value = String(device.modelId ?? '');
  devType.value = device.type;
  devIP.value = device.ipAddress || '';
  devPort.value = String(device.port ?? '');
  devEndpointUrl.value = device.endpointUrl || '';
  devStatus.value = device.status === 1 ? 'online' : 'offline';

  // S7 connections
  devCpuType.value = device.cpuType || 'S7-1200';
  devRack.value = device.rack !== undefined ? device.rack : 0;
  devSlot.value = device.slot !== undefined ? device.slot : 1;

  // Virtual config: 从 configJson 回填表单
  devVirtualIntervalMs.value = 1000;
  devVirtualRandomValues.value = true;
  const rawConfig = (device as any).configJson;
  if (typeof rawConfig === 'string' && rawConfig.trim()) {
    try {
      const parsed = JSON.parse(rawConfig);
      if (typeof parsed.IntervalMs === 'number') devVirtualIntervalMs.value = parsed.IntervalMs;
      if (typeof parsed.RandomValues === 'boolean') devVirtualRandomValues.value = parsed.RandomValues;
    } catch {
      // 旧配置格式不兼容,保留默认值
    }
  }

  // 阶段 3.6：编辑态连接信息初始化。默认仍走快速模式（与"推荐渐进"一致，连接参数可直接编辑；
  // 后端快速模式会同步更新该设备关联的连接行）。连接被多台设备共享时给出警示，避免误改共享参数。
  const attachedConn = device.connection;
  connMode.value = 'quick';
  controllerConnections.value = [];
  advancedConnectionsLoading.value = false;
  advancedControllerId.value = device.controllerId ?? null;
  advancedConnectionId.value = device.connectionId ?? null;
  newConnectionName.value = device.name ? `${device.name} 连接` : '独立连接';

  editingConnectionLabel.value = attachedConn
    ? [attachedConn.controllerCode, attachedConn.controllerName, attachedConn.protocolName]
        .filter(Boolean)
        .join(' / ') + (attachedConn.host ? ` · ${connectionEndpointLabel(attachedConn)}` : '')
    : '';
  editingConnectionSharedBy.value =
    device.connectionId != null && device.connection != null
      ? devices.value.filter(d => d.connectionId != null && d.connectionId === device.connectionId).length
      : 0;

  // 已关联连接：异步拉取连接权威配置（Connection.ConfigJson）覆盖表单字段，
  // 防止共享连接被其它设备修改后、本设备 JsonConfig 镜像过期导致"未改动即回退"。
  if (device.connectionId != null) {
    refreshEditConnectionFields(device);
  }

  // 阶段 5：编辑态异步加载模型绑定列表（含主/附加模型行）。不阻塞弹窗打开，面板内自显 loading。
  deviceBindings.value = [];
  bindTargetModelId.value = '';
  bindingError.value = '';
  bindingBusy.value = false;
  if (!systemConfig.value.isSimulationActive) {
    openBindings(device.id);
  }

  showDeviceModal.value = true;
};

// 按设备类型构造后端 ConfigJson,字段命名与后端 *Config DTO 保持一致。
// 后端 DeviceAppService.ValidateConfigJson 会反序列化校验,字段缺失或类型不符会拒绝。
const buildConfigJson = (type: DeviceType): string => {
  switch (type) {
    case 'OPCUA':
      // 优先使用完整端点地址(含可选路径),避免 ip+port 拼接时丢失路径部分。
      return JSON.stringify({
        EndpointUrl: devEndpointUrl.value || `opc.tcp://${devIP.value || '127.0.0.1'}:${devPort.value || '4840'}`,
        SecurityPolicy: 'None'
      });
    case 'S7':
      return JSON.stringify({
        IpAddress: devIP.value || '127.0.0.1',
        Port: Number(devPort.value) || 102,
        Rack: Number(devRack.value) || 0,
        Slot: Number(devSlot.value) || 1,
        CpuType: devCpuType.value || 'S71500'
      });
    case 'MQTT':
      return JSON.stringify({
        Broker: devIP.value || '127.0.0.1',
        Port: Number(devPort.value) || 1883,
        Topic: '',
        ClientId: `scada-${Date.now()}`
      });
    case 'Virtual':
      return JSON.stringify({
        IntervalMs: Number(devVirtualIntervalMs.value) || 1000,
        RandomValues: devVirtualRandomValues.value
      });
    default:
      return '{}';
  }
};

const handleSaveDevice = async () => {
  addLog('调试', `开始保存设备: ${devName.value}`, 'normal');
  if (!devName.value.trim()) {
    addLog('调试', '校验失败: 名称为空', 'warning');
    deviceFormErrors.value = { Name: '设备名称不能为空' };
    return;
  }

  const chosenModel = chosenDeviceModel.value;
  if (!chosenModel) {
    addLog('调试', '校验失败: 未选择数据模型', 'warning');
    deviceFormErrorMessage.value = '请先选择数据模型';
    return;
  }
  const deviceType = deviceTypeFromModel.value;

  deviceFormErrors.value = {};
  deviceFormErrorMessage.value = '';

  // 阶段 3.6 载荷组装：
  // - 快速模式：以表单字段构造 ConfigJson 提交（后端自动维护该设备的专属连接，行为与重构前一致）；
  // - 高级模式：提交 ControllerId + ConnectionId（连接配置为真相源，后端镜像），不提交 ConfigJson。
  const baseData = {
    name: devName.value,
    key: devKey.value.trim(),
    areaId: devArea.value,
    // Device.modelId 为 number；devModel 下拉值为 string，统一转 number 提交
    modelId: Number(devModel.value) || 0,
    status: devStatus.value === 'online' ? 1 : 0
  };

  let attachPayload: { controllerId: number; connectionId: number } | { configJson: string } | null = null;

  if (connMode.value === 'advanced') {
    if (advancedControllerId.value == null) {
      deviceFormErrorMessage.value = '高级模式请先选择控制器';
      return;
    }
    if (advancedConnectionId.value == null) {
      deviceFormErrorMessage.value = '高级模式请选择已有连接，或选择「＋ 新建独立连接」';
      return;
    }

    let targetConnectionId = advancedConnectionId.value;
    if (targetConnectionId === NEW_CONNECTION_SENTINEL) {
      // 「＋ 新建独立连接」：先用表单协议参数在该控制器下创建连接，再把设备附加过去。
      // 该连接随后可被其它设备在高级模式中选择，实现跨设备共享（阶段 3.6 验收语义）。
      if (!newConnectionName.value.trim()) {
        deviceFormErrorMessage.value = '请填写新连接名称';
        return;
      }
      advancedConnectionsLoading.value = true;
      try {
        const payload: DeviceConnectionRequest = {
          ControllerId: advancedControllerId.value,
          Name: newConnectionName.value.trim(),
          ProtocolId: Number(chosenModel.protocolId),
          ConfigJson: buildConfigJson(deviceType),
          TimeoutMs: 5000,
          ReconnectIntervalMs: 5000,
          IsEnabled: true
        };
        const resp = await createDeviceConnection(payload);
        const createdId = resp?.data?.id;
        if (!createdId) {
          deviceFormErrorMessage.value = '创建独立连接失败：服务端未返回连接 ID';
          return;
        }
        targetConnectionId = createdId;
      } catch (error: any) {
        // 连接创建失败：展示后端业务文案，中断设备保存（HTTP 层已统一 Toast）
        deviceFormErrorMessage.value = error?.response?.data?.message || error?.message || '创建独立连接失败';
        return;
      } finally {
        advancedConnectionsLoading.value = false;
      }
    } else {
      // 已选已有连接：与后端 ResolveAttachConnectionAsync 同口径的前置校验，给出友好提示
      const target = controllerConnections.value.find(c => c.id === targetConnectionId);
      if (target && !target.isEnabled) {
        deviceFormErrorMessage.value = `连接「${target.name}」已停用，不可被设备引用，请先启用或另选连接`;
        return;
      }
      if (target && chosenModel.protocolId != null && target.protocolId !== chosenModel.protocolId) {
        deviceFormErrorMessage.value = `连接「${target.name}」的协议与所选数据模型不一致，无法附加`;
        return;
      }
    }

    attachPayload = {
      controllerId: advancedControllerId.value,
      connectionId: targetConnectionId
    };
  } else {
    // 快速模式：协议由后端从 modelId 推导，前端按模型 protocolKey 派生类型构造 ConfigJson
    attachPayload = { configJson: buildConfigJson(deviceType) };
  }

  const deviceData = {
    ...baseData,
    ...(attachPayload as Record<string, any>)
  };

  if (isEditingDevice.value && editingDeviceId.value != null) {
    // Edit existing
    const result = await updateDeviceAndSync(editingDeviceId.value, deviceData as any);
    if (result.success) {
      addLog('设备管理', `保存了工业设备配置 [${devName.value}]`, 'normal');
      showDeviceModal.value = false;
    } else if (result.error) {
      if (result.error.type === 'validation' && result.error.fieldErrors) {
        deviceFormErrors.value = result.error.fieldErrors;
      } else {
        deviceFormErrorMessage.value = result.error.message;
      }
    }
  } else {
    // Add new
    const result = await createDeviceAndSync(deviceData as any);
    if (result.success) {
      addLog('设备管理', `添加新网关通道: [${devName.value}] (${deviceType})`, 'normal');
      showDeviceModal.value = false;
    } else if (result.error) {
      if (result.error.type === 'validation' && result.error.fieldErrors) {
        deviceFormErrors.value = result.error.fieldErrors;
      } else {
        deviceFormErrorMessage.value = result.error.message;
      }
    }
  }

  if (!deviceFormErrorMessage.value && !Object.keys(deviceFormErrors.value).length) {
    addLog('调试', '模态框已关闭', 'normal');
  }
};

const handleDeleteDevice = async (id: number, name: string) => {
  const result = await deleteDeviceAndSync(id, name);
  if (result.success) {
    addLog('设备管理', `删除了工业网络网关 [${name}]`, 'warning');
  }
  // 失败提示由 http 拦截器统一 Toast 弹出
};

const toggleDeviceEnabled = async (device: Device) => {
  const next = !device.isEnabled;
  if (!next && !confirm(`确认停用设备 [${device.name}] 的采集吗？停用后将断开连接并停止采集。`)) return;
  const result = await setDeviceEnabledAndSync(device.id, next);
  if (!result.success) addLog('设备管理', `切换启用状态失败 [${device.name}]`, 'warning');
};
</script>

<template>
  <div class="h-full overflow-y-auto space-y-6 text-[#1e293b] dark:text-slate-100 select-none p-4 sm:p-6 bg-slate-50/50 dark:bg-transparent">
    
    <!-- Header panel with tab switches -->
    <div class="flex flex-col sm:flex-row sm:items-center justify-between border-b border-slate-200 dark:border-slate-800 pb-5 gap-4 text-left">
      <div>
        <h1 class="text-xl font-bold font-sans text-slate-900 dark:text-white tracking-tight">设备管理</h1>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          管理设备、区域及通信配置
        </p>
      </div>

      <!-- Option tags -->
      <div class="flex items-center gap-2">
        <button 
          @click="activeSection = 'list'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none transition-all"
          :class="activeSection === 'list' ? 'bg-slate-900 dark:bg-sky-600 text-white border-slate-900 dark:border-sky-600 shadow-sm' : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 border-slate-200 dark:border-slate-700'"
        >
          设备列表
        </button>
        <button 
          @click="activeSection = 'areas'"
          class="px-4 py-1.5 rounded-lg text-xs font-bold border cursor-pointer select-none transition-all"
          :class="activeSection === 'areas' ? 'bg-slate-900 dark:bg-sky-600 text-white border-slate-900 dark:border-sky-600 shadow-sm' : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 border-slate-200 dark:border-slate-700'"
        >
          区域管理 ({{ areas.length }})
        </button>
      </div>
    </div>

    <!-- 1. SECTION: DEVICES LIST VIEW -->
    <div v-if="activeSection === 'list'" class="space-y-4">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400">
            分组设备 ({{ filteredDevices.length }} 台)
          </h3>
          <div class="flex items-center gap-1">
            <button 
              @click="expandAllAreas"
              class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer"
            >
              全部展开
            </button>
            <button 
              @click="collapseAllAreas"
              class="text-[10px] text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 font-bold px-2 py-0.5 rounded border border-slate-200 dark:border-slate-700 hover:border-slate-300 transition-all cursor-pointer"
            >
              全部折叠
            </button>
          </div>
        </div>
        
        <button 
          @click="openNewDeviceModal"
          class="bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all active:translate-y-0.5 shadow-sm"
        >
          <Plus class="w-4 h-4" />
          添加设备
        </button>
      </div>

      <!-- 区域筛选（阶段 1：树形区域选择 + 含子区域开关；默认全部区域 = 原有行为） -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl px-3 py-2.5 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
        <div class="flex items-center gap-1.5 font-bold text-slate-500 dark:text-slate-400">
          <Filter class="w-4 h-4" />
          区域筛选
        </div>

        <!-- 区域树下拉 -->
        <div class="relative">
          <button
            @click="filterDropdownOpen = !filterDropdownOpen"
            class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border cursor-pointer transition-all"
            :class="filterAreaId != null
              ? 'bg-[#1890ff]/10 border-[#1890ff]/40 text-[#1890ff] dark:text-sky-400 font-bold'
              : 'bg-slate-50 dark:bg-slate-950 border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300'"
          >
            {{ filterAreaLabel || '全部区域' }}
            <ChevronDown class="w-3.5 h-3.5" />
          </button>

          <div
            v-if="filterDropdownOpen"
            class="absolute z-30 mt-1.5 w-64 max-h-80 overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-xl shadow-xl p-2 text-left"
          >
            <div
              @click="clearAreaFilter(); filterDropdownOpen = false"
              class="px-2 py-1.5 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer font-bold"
              :class="filterAreaId == null ? 'text-[#1890ff] dark:text-sky-400' : 'text-slate-600 dark:text-slate-300'"
            >
              全部区域
            </div>
            <div class="my-1 border-t border-slate-100 dark:border-slate-800" />
            <AreaTree :nodes="areaTree" :selected-id="filterAreaId" @select="onFilterSelect" />
          </div>
        </div>

        <!-- 含子区域开关 -->
        <label
          class="flex items-center gap-1.5 font-bold cursor-pointer select-none"
          :class="filterAreaId != null ? 'text-slate-600 dark:text-slate-300' : 'text-slate-300 dark:text-slate-600 cursor-not-allowed'"
        >
          <input
            type="checkbox"
            v-model="includeSubareas"
            :disabled="filterAreaId == null"
            class="text-[#1890ff] focus:ring-0"
          />
          包含子区域
        </label>

        <button
          v-if="filterAreaId != null"
          @click="clearAreaFilter"
          class="text-rose-500 hover:text-rose-700 font-bold cursor-pointer"
        >
          清除筛选
        </button>
      </div>

      <!-- Devices grouped by area with collapsible panels -->
      <div class="space-y-3">
        <div v-for="area in visibleAreas" :key="area.id" class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm transition-colors">
          <!-- Area header (clickable to expand/collapse) -->
          <div 
            @click="toggleArea(area.id)"
            class="flex items-center justify-between px-4 py-3 bg-slate-50 dark:bg-slate-950/60 cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800/60 transition-all border-b border-slate-100 dark:border-slate-800"
          >
            <div class="flex items-center gap-3">
              <div 
                class="w-5 h-5 rounded flex items-center justify-center text-[10px] font-bold transition-all"
                :class="isAreaExpanded(area.id) ? 'bg-[#1890ff] text-white rotate-90' : 'bg-slate-300 dark:bg-slate-700 text-white'"
              >
                ▶
              </div>
              <span class="text-[11px] font-bold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{{ area.name }}</span>
              <span class="bg-sky-100 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
                {{ devicesInPanel(area.id).length }} 台
              </span>
            </div>
            <div class="flex items-center gap-4 text-[10px] text-slate-400 dark:text-slate-500">
              <span v-if="devicesInPanel(area.id).length === 0" class="italic">暂无设备</span>
              <span class="font-mono">ID: {{ area.id }}</span>
            </div>
          </div>
          
          <!-- Device cards (shown when expanded) -->
          <div v-if="isAreaExpanded(area.id)" class="p-4">
            <div v-if="devicesInPanel(area.id).length === 0" class="text-center py-8 text-slate-400 dark:text-slate-500 text-xs">
              <Cpu class="w-8 h-8 mx-auto mb-2 opacity-30" />
              <span>该区域暂无设备，请点击"添加设备"创建</span>
            </div>
            <div v-else class="grid grid-cols-1 xl:grid-cols-2 gap-3">
              <div 
                v-for="d in devicesInPanel(area.id)" 
                :key="d.id"
                class="bg-white dark:bg-slate-950/60 border border-slate-100 dark:border-slate-800 rounded-lg p-4 text-left flex flex-col justify-between hover:shadow-md transition-all relative overflow-hidden"
              >
                <!-- Status indicator -->
                <div 
                  class="absolute top-0 left-0 right-0 h-1"
                  :class="d.status === 1 ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-700'"
                />

          <div class="flex items-start justify-between gap-4 mt-1">
            <div class="space-y-1">
              <div class="flex items-center gap-1.5">
                <span class="text-[9px] font-mono font-bold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 px-1.5 py-0.5 rounded uppercase">
                  {{ d.type }}
                </span>
                <span class="text-xs text-slate-400 dark:text-slate-500 font-mono">KEY: {{ d.key }}</span>
              </div>
              <h4 class="font-bold text-sm text-slate-900 dark:text-white font-sans mt-1.5 leading-snug">
                {{ d.name }}
              </h4>
            </div>

            <!-- Online/offline status (read-only, driven by backend runtimeStatus) + 启用/停用采集开关 -->
            <div class="flex items-center gap-1.5">
              <span
                class="text-[10px] font-bold px-2 py-1 rounded-full flex items-center gap-1 border"
                :class="d.status === 1 ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800' : 'bg-slate-50 dark:bg-slate-800 text-slate-400 dark:text-slate-400 border-slate-200 dark:border-slate-700'">
                <div class="w-1.5 h-1.5 rounded-full" :class="d.status === 1 ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
                {{ d.status === 1 ? '在线' : '离线' }}
              </span>
              <button
                @click="toggleDeviceEnabled(d)"
                class="text-[10px] font-bold px-2 py-1 rounded-full flex items-center gap-1 border transition-all cursor-pointer"
                :class="d.isEnabled ? 'bg-sky-50 dark:bg-sky-950/40 text-sky-600 dark:text-sky-400 border-sky-200 dark:border-sky-800' : 'bg-slate-100 dark:bg-slate-800 text-slate-400 border-slate-300 dark:border-slate-700'">
                {{ d.isEnabled ? '启用采集' : '停用采集' }}
              </button>
            </div>
          </div>

          <!-- Mid: Address properties -->
          <div class="grid grid-cols-2 gap-x-4 gap-y-1.5 py-3 border-t border-b border-slate-100/80 dark:border-slate-800/80 mt-4 text-[11px] font-mono">
            <div>
              <span class="text-slate-400 dark:text-slate-500">所属区域:</span>
              <span class="text-slate-800 dark:text-slate-200 font-sans font-medium block">
                {{ areas.find(a => a.id === d.areaId)?.name || '未选择' }}
              </span>
            </div>
            <div>
              <span class="text-slate-400 dark:text-slate-500">数据模型:</span>
              <span class="text-[#1890ff] dark:text-sky-400 font-sans font-medium block">
                {{ dataModels.find(m => String(m.id) === String(d.modelId))?.name || '未配置' }}
              </span>
            </div>
            <div class="col-span-2 space-y-1">
              <span class="text-slate-400 dark:text-slate-500">连接地址:</span>
              <div class="text-slate-700 dark:text-slate-300 font-bold block truncate leading-relaxed">
                <template v-if="d.type === 'OPCUA'">
                  <span class="text-sky-600 dark:text-sky-400 font-bold">OPCUA:</span> {{ d.endpointUrl || '未配置' }}
                </template>
                <template v-else-if="d.type === 'S7'">
                  <span class="text-indigo-600 dark:text-indigo-400 font-bold">S7 Link:</span> {{ d.ipAddress || '未配置' }}:{{ d.port || '未配置' }} 
                  <span class="bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border border-indigo-150 dark:border-indigo-800 px-1 rounded text-[10px] font-normal font-sans ml-1.5">
                    {{ d.cpuType || '未配置' }} (R{{ d.rack ?? '—' }}/S{{ d.slot ?? '—' }})
                  </span>
                </template>
                <template v-else-if="d.type === 'MQTT'">
                  <div class="text-xs text-slate-800 dark:text-slate-200 break-all">
                    <span class="text-emerald-600 dark:text-emerald-400 font-bold">MQTT Device</span>
                  </div>
                </template>
                <template v-else>
                  <span>Local Bus Simulation:</span> 宿主机虚拟工业网关
                </template>
              </div>
            </div>

            <!-- 阶段 3.6：设备已关联连接时的连接摘要（控制器/协议/端点 + 共享态提示） -->
            <div v-if="d.connection" class="col-span-2 flex items-center gap-1.5 text-[10px] text-slate-400 dark:text-slate-500 leading-relaxed">
              <span class="bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-300 border border-violet-200/60 dark:border-violet-800 rounded px-1.5 py-0.5 font-sans font-bold whitespace-nowrap">
                连接 #{{ d.connection.id }}
              </span>
              <span class="truncate font-sans">
                {{ [d.connection.controllerName, d.connection.controllerCode, d.connection.protocolName].filter(Boolean).join(' / ') }}
                <template v-if="d.connection.host"> · {{ connectionEndpointLabel(d.connection) }}</template>
              </span>
            </div>
          </div>

          <!-- Bottom: action edits -->
          <div class="flex items-center justify-between mt-3 text-[11px]">
            <span class="text-slate-400 dark:text-slate-500">最后更新: <b class="font-mono text-slate-600 dark:text-slate-300">{{ d.lastUpdated }}</b></span>
            
            <div class="flex items-center gap-2">
              <button 
                @click="openEditDeviceModal(d)"
                class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 font-bold inline-flex items-center gap-0.5 cursor-pointer"
              >
                <Edit3 class="w-3.5 h-3.5" />
                编辑
              </button>
              <button 
                @click="router.push(`/device-variables?deviceId=${d.id}`)"
                class="text-emerald-600 dark:text-emerald-400 hover:text-emerald-700 font-bold inline-flex items-center gap-0.5 cursor-pointer ml-1"
                :title="`管理设备 ${d.name} 的变量实例`"
              >
                <Braces class="w-3.5 h-3.5" />
                变量
              </button>
              <button 
                @click="handleDeleteDevice(d.id, d.name)"
                class="text-rose-500 hover:text-rose-700 font-bold inline-flex items-center gap-0.5 cursor-pointer ml-1"
              >
                <Trash2 class="w-3.5 h-3.5" />
                删除
              </button>
            </div>
          </div>
        </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. SECTION: AREAS CONFIGURATION LIST（阶段 1：树形层级） -->
    <div v-else-if="activeSection === 'areas'" class="space-y-4">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-bold tracking-widest uppercase text-slate-500 dark:text-slate-400">
          所有区域（{{ areas.length }}）
        </h3>
        
        <button 
          @click="openAddAreaModal"
          class="bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white px-3 py-1.5 rounded-lg inline-flex items-center gap-1 cursor-pointer transition-all text-center"
        >
          <Plus class="w-4 h-4" />
          添加区域
        </button>
      </div>

      <!-- Area tree table card -->
      <div class="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm text-left transition-colors">
        <table class="w-full text-xs hover:border-collapse">
          <thead>
            <tr class="bg-slate-50 dark:bg-slate-950/60 ring-1 ring-slate-100 dark:ring-slate-800 uppercase text-[10px] text-slate-400 dark:text-slate-500 font-bold tracking-wider">
              <th class="px-6 py-4">区域ID</th>
              <th class="px-6 py-4">区域名称 / 类型</th>
              <th class="px-6 py-4">编码</th>
              <th class="px-6 py-4">描述</th>
              <th class="px-6 py-4">排序</th>
              <th class="px-6 py-4">设备数</th>
              <th class="px-6 py-4 text-right">操作</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800 font-mono">
            <tr v-for="{ node: a, depth } in visibleAreaRows" :key="a.id" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/40 transition-all">
              <td class="px-6 py-3 font-bold text-slate-500 dark:text-slate-400">{{ a.id }}</td>
              <td class="px-6 py-3 font-sans font-bold text-slate-800 dark:text-white text-[13px]" :style="{ paddingLeft: `${depth * 18 + 24}px` }">
                <span class="inline-flex items-center gap-1.5">
                  <!-- 展开/折叠（有子节点时显示） -->
                  <button
                    v-if="a.children && a.children.length > 0"
                    @click="toggleAreaManage(a.id)"
                    class="w-4 h-4 inline-flex items-center justify-center text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer"
                  >
                    <ChevronDown v-if="isAreaManageExpanded(a.id)" class="w-3.5 h-3.5" />
                    <ChevronRight v-else class="w-3.5 h-3.5" />
                  </button>
                  <span v-else class="w-4 h-4 block" />
                  {{ a.name }}
                </span>
                <span
                  class="ml-2 text-[9px] font-bold uppercase tracking-wider px-1.5 py-0.5 rounded"
                  :class="{
                    'bg-sky-50 dark:bg-sky-950/60 text-sky-600 dark:text-sky-400': a.areaType === 1,
                    'bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400': a.areaType === 2,
                    'bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400': a.areaType === 3,
                    'bg-emerald-50 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400': (a.areaType ?? 4) === 4,
                    'bg-violet-50 dark:bg-violet-950/60 text-violet-600 dark:text-violet-400': a.areaType === 5
                  }"
                >
                  {{ areaTypeLabel(a.areaType) }}
                </span>
                <span v-if="a.isEnabled === false" class="ml-1.5 text-[9px] font-bold text-slate-400 dark:text-slate-500">已停用</span>
              </td>
              <td class="px-6 py-3 text-slate-500 dark:text-slate-400 text-[11px]">{{ a.code || '—' }}</td>
              <td class="px-6 py-3 font-sans text-slate-500 dark:text-slate-400 text-[11px] leading-relaxed max-w-sm truncate">{{ a.description }}</td>
              <td class="px-6 py-3 text-center text-slate-400 dark:text-slate-500">{{ a.sort }}</td>
              <td class="px-6 py-3 text-center">
                <span class="bg-sky-50 dark:bg-sky-950/60 font-sans text-[#1890ff] dark:text-sky-400 font-bold px-2 py-0.5 rounded-full text-[10px]">
                  {{ a.deviceCount }} 台
                </span>
              </td>
              <td class="px-6 py-3 text-right whitespace-nowrap">
                <button 
                  @click="openEditAreaModal(a)"
                  class="text-[#1890ff] dark:text-sky-400 hover:text-sky-600 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5 mr-3"
                >
                  <Edit3 class="w-3.5 h-3.5" />
                  编辑
                </button>
                <button 
                  @click="handleDeleteArea(a.id, a.name)"
                  class="text-rose-500 hover:text-rose-700 cursor-pointer font-sans font-bold inline-flex items-center gap-0.5"
                >
                  <Trash2 class="w-3.5 h-3.5" />
                  删除
                </button>
              </td>
            </tr>
          </tbody>
        </table>
        <div v-if="visibleAreaRows.length === 0" class="py-12 text-center text-slate-400 dark:text-slate-500 text-xs">
          暂无区域，点击右上角"添加区域"创建
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT AREA（阶段 1：父区域/编码/类型/排序/启用） -->
    <div v-if="showAreaModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-md w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <MapPin class="w-4 h-4 text-sky-400" />
            <span>{{ isEditingArea ? '编辑区域' : '添加区域' }}</span>
          </div>
          <button @click="showAreaModal = false; areaFormErrors = {}; areaFormErrorMessage = ''" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[420px]">
          <div v-if="areaFormErrorMessage" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400">
            {{ areaFormErrorMessage }}
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">区域名称 <span class="text-rose-500">*</span></label>
            <input
              v-model="newAreaName"
              type="text"
              placeholder="例如: 智能三级精细沉降池"
              :class="areaFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
              class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
            />
            <span v-if="areaFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ areaFormErrors.Name }}</span>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">父区域</label>
              <select
                v-model="areaFormParentId"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              >
                <option :value="null">（作为根区域）</option>
                <option v-for="r in areaFormParentOptions" :key="r.node.id" :value="r.node.id">
                  {{ '　'.repeat(r.depth) }}{{ r.node.name }}
                </option>
              </select>
              <p v-if="isEditingArea" class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">调整层级通过"父区域"下拉实现</p>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">区域类型</label>
              <select
                v-model="areaFormAreaType"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              >
                <option :value="1">工厂</option>
                <option :value="2">车间</option>
                <option :value="3">产线</option>
                <option :value="4">区域</option>
                <option :value="5">仓库</option>
              </select>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">区域编码</label>
              <input
                v-model="areaFormCode"
                type="text"
                placeholder="可选，用于设备编号前缀"
                :class="areaFormErrors.Code ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 font-mono font-bold uppercase focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none"
              />
              <span v-if="areaFormErrors.Code" class="text-rose-500 text-[10px] mt-1 block">{{ areaFormErrors.Code }}</span>
              <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">编码全局唯一，修改仅影响后续新生成设备编号</p>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">排序</label>
              <input
                v-model.number="areaFormSort"
                type="number"
                min="0"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              />
              <p class="text-slate-400 dark:text-slate-500 text-[10px] mt-1">同级内展示顺序（升序）</p>
            </div>
          </div>

          <div>
            <label class="flex items-center gap-2 font-bold text-slate-600 dark:text-slate-300 cursor-pointer select-none">
              <input
                type="checkbox"
                v-model="areaFormEnabled"
                class="text-[#1890ff] focus:ring-0"
              />
              启用该区域
            </label>
          </div>

          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">描述</label>
            <textarea
              v-model="newAreaDesc"
              rows="2"
              placeholder="阐述本区域所属流程及测温、变频流水分拣的具体物料方向..."
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 font-sans focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff] leading-relaxed"
            />
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-3 flex justify-end gap-2 border-t border-slate-100 dark:border-slate-800">
          <button
            @click="showAreaModal = false; areaFormErrors = {}; areaFormErrorMessage = ''"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveArea"
            class="px-4 py-1.5 rounded-lg bg-slate-900 dark:bg-sky-600 hover:bg-slate-800 dark:hover:bg-sky-500 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: ADD / EDIT DEVICE COMM LINK -->
    <div v-if="showDeviceModal" class="fixed inset-0 bg-slate-900/70 flex items-center justify-center z-50 p-4">
      <div class="bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-100 dark:border-slate-800 max-w-lg w-full overflow-hidden text-left animate-in fade-in zoom-in-95 duration-150">
        
        <!-- Header banner -->
        <div class="bg-slate-900 dark:bg-slate-950 text-white p-4 flex items-center justify-between border-b border-slate-800">
          <div class="flex items-center gap-1.5 font-bold text-xs uppercase tracking-widest">
            <Cpu class="w-4 h-4 text-[#1890ff]" />
            <span>{{ isEditingDevice ? '编辑设备' : '添加设备' }}</span>
          </div>
          <button @click="showDeviceModal = false; deviceFormErrors = {}; deviceFormErrorMessage = ''" class="text-slate-400 hover:text-white cursor-pointer"><X class="w-4 h-4" /></button>
        </div>

        <div class="p-5 space-y-4 text-xs overflow-y-auto max-h-[450px]">
          <div v-if="deviceFormErrorMessage" class="bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 rounded-lg p-3 text-rose-600 dark:text-rose-400">
            {{ deviceFormErrorMessage }}
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">设备名称</label>
              <input 
                v-model="devName"
                type="text"
                placeholder="例如: 3号二次鼓风风机"
                :class="deviceFormErrors.Name ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700 focus:border-[#1890ff]'"
                class="w-full bg-slate-50 dark:bg-slate-950 border rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none text-xs font-sans font-semibold"
              />
              <span v-if="deviceFormErrors.Name" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Name }}</span>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">设备编号</label>
              <input 
                v-model="devKey"
                type="text"
                readonly
                :placeholder="isEditingDevice ? '' : '保存后由系统自动生成'"
                :class="deviceFormErrors.Key ? 'border-rose-500' : 'border-slate-200 dark:border-slate-700'"
                class="w-full bg-slate-100 dark:bg-slate-800 border rounded-lg p-2 text-slate-400 dark:text-slate-500 focus:outline-none text-xs font-mono font-bold uppercase cursor-not-allowed"
              />
              <span v-if="!isEditingDevice" class="text-slate-400 dark:text-slate-500 text-[10px] mt-1 block">无需手动填写，保存后由系统按区域自动生成</span>
              <span v-if="deviceFormErrors.Key" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Key }}</span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">所属区域</label>
              <select 
                v-model="devArea"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none focus:border-[#1890ff]"
              >
                <option v-for="r in areaTreeRows" :key="r.node.id" :value="r.node.id">
                  {{ '　'.repeat(r.depth) }}{{ r.node.name }}
                </option>
              </select>
            </div>
            <div>
              <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">
                数据模型<template v-if="isEditingDevice">（主模型）</template>
              </label>
              <select 
                v-model="devModel"
                @change="onModelChange"
                :disabled="isEditingDevice"
                :title="isEditingDevice ? '设备绑定模型不可直接变更，请在下方「模型绑定」中切换主模型或管理附加模型' : ''"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-700 rounded-lg p-2 focus:bg-white dark:focus:bg-slate-900 text-slate-900 dark:text-white focus:outline-none text-[#1890ff] dark:text-sky-400 font-bold disabled:opacity-60 disabled:cursor-not-allowed"
              >
                <option v-for="m in dataModels" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
              <span v-if="!isEditingDevice" class="text-slate-400 dark:text-slate-500 text-[10px] mt-1 block">保存后即作为主模型绑定，可在编辑时再附加其他模型</span>
              <span v-else class="text-amber-500/90 dark:text-amber-400/80 text-[10px] mt-1 block">编辑态模型锁定，切换请用下方「模型绑定」</span>
            </div>
          </div>

          <!-- 阶段 5：模型绑定管理（仅编辑态；主模型行与后端 Device.ModelId 双写一致，附加模型仅管理、运行时暂不采集） -->
          <div
            v-if="isEditingDevice && editingDeviceId != null"
            class="p-3 bg-slate-50 dark:bg-slate-950/70 rounded-xl border border-sky-100 dark:border-slate-800 space-y-2.5"
          >
            <div class="flex items-center justify-between gap-2">
              <div class="flex items-center gap-1.5 text-[10px] text-[#1890ff] dark:text-sky-400 font-bold uppercase tracking-wider">
                <Link2 class="w-3.5 h-3.5" />
                <span>模型绑定</span>
              </div>
              <span class="text-[10px] font-mono text-slate-400">
                <template v-if="bindingsLoading"><Loader2 class="w-3 h-3 inline animate-spin mr-0.5" />加载中…</template>
                <template v-else>{{ deviceBindings.length }} 个绑定 · {{ primaryBinding ? '主：' + (primaryBinding.name || '') : '无主模型' }}</template>
              </span>
            </div>

            <div v-if="bindingError" class="rounded-lg border border-rose-200 dark:border-rose-800 bg-rose-50 dark:bg-rose-950/40 px-2.5 py-1.5 text-[10px] text-rose-600 dark:text-rose-400 leading-relaxed">
              {{ bindingError }}
            </div>

            <!-- 绑定列表（主模型行在前） -->
            <div v-if="!bindingsLoading && deviceBindings.length > 0" class="space-y-1.5">
              <div
                v-for="b in deviceBindings"
                :key="b.id"
                class="flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-[11px]"
                :class="b.isPrimary
                  ? 'border-sky-200 dark:border-sky-800 bg-sky-50/70 dark:bg-sky-950/30'
                  : 'border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900'"
              >
                <Star v-if="b.isPrimary" class="w-3.5 h-3.5 text-[#1890ff] dark:text-sky-400 shrink-0" fill="currentColor" />
                <template v-else><Link2 class="w-3.5 h-3.5 text-slate-300 dark:text-slate-600 shrink-0" /></template>
                <div class="min-w-0 flex-1 leading-tight">
                  <div class="flex items-center gap-1.5">
                    <span class="font-bold text-slate-800 dark:text-slate-100 truncate">{{ b.name || ('模型 #' + b.dataModelId) }}</span>
                    <span v-if="b.code" class="font-mono text-[9px] text-slate-400 bg-slate-100 dark:bg-slate-800 px-1 rounded">{{ b.code }}</span>
                    <span class="font-mono text-[9px] text-slate-400">{{ b.isPrimary ? '★ 主模型' : '附加' }}</span>
                  </div>
                  <div class="font-mono text-[9px] text-slate-400 dark:text-slate-500">
                    v{{ b.version || '1.0' }} · {{ b.variableCount }} 个模板变量
                    <span v-if="!b.isEnabled"> · 已停用</span>
                  </div>
                </div>
                <div class="flex items-center gap-1 shrink-0" v-if="!b.isPrimary">
                  <button
                    type="button"
                    @click="handleSetPrimary(b)"
                    :disabled="bindingBusy"
                    class="inline-flex items-center gap-0.5 text-[9px] font-bold px-1.5 py-0.5 rounded bg-sky-50 dark:bg-sky-950/60 text-[#1890ff] dark:text-sky-400 hover:bg-sky-100 dark:hover:bg-sky-900 border border-sky-200/70 dark:border-sky-800 disabled:opacity-40 cursor-pointer"
                    title="切换为主模型（事务内降级旧主并同步设备主模型，启用中设备将热重载）"
                  >设为主</button>
                  <button
                    type="button"
                    @click="handleUnbind(b)"
                    :disabled="bindingBusy"
                    class="inline-flex items-center gap-0.5 text-[9px] font-bold px-1.5 py-0.5 rounded bg-rose-50 dark:bg-rose-950/40 text-rose-500 dark:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-900/60 border border-rose-200/70 dark:border-rose-800 disabled:opacity-40 cursor-pointer"
                    title="解绑该附加模型"
                  ><Unlink2 class="w-2.5 h-2.5" />解绑</button>
                </div>
                <span v-else class="text-[9px] font-bold text-sky-500 dark:text-sky-400 shrink-0">运行生效</span>
              </div>
            </div>

            <!-- 空态：无绑定（异常情形）兜底提示 -->
            <div v-else-if="!bindingsLoading" class="rounded-lg border border-dashed border-amber-300 dark:border-amber-700 px-2.5 py-2 text-[10px] text-amber-600 dark:text-amber-400 leading-relaxed">
              该设备暂无绑定记录。请先绑定一个数据模型，首个绑定将自动设为主模型。
            </div>

            <!-- 绑定新模型（下拉候选 = 已发布且未绑定的模型） -->
            <div v-if="bindableModels.length > 0" class="flex items-center gap-1.5 pt-0.5">
              <select
                v-model="bindTargetModelId"
                class="flex-1 min-w-0 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 text-[10px] font-bold text-slate-800 dark:text-white focus:outline-none"
              >
                <option value="" disabled>（选择要绑定的附加模型）</option>
                <option v-for="m in bindableModels" :key="m.id" :value="m.id">
                  {{ m.name }}{{ m.code ? ' · ' + m.code : '' }}
                </option>
              </select>
              <button
                type="button"
                @click="handleBindModel"
                :disabled="!bindTargetModelId || bindingBusy"
                class="inline-flex items-center gap-1 text-[10px] font-bold px-2 py-1 rounded bg-[#1890ff] hover:bg-sky-600 text-white disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer shrink-0"
              >
                <Link2 class="w-3 h-3" /> 绑定附加
              </button>
            </div>
            <p v-else class="text-[10px] text-slate-400 dark:text-slate-500 leading-relaxed">
              没有更多可绑定的模型（其余模型均已绑定，或尚未发布）。
            </p>

            <p class="text-[9px] leading-relaxed text-slate-400 dark:text-slate-500 border-t border-slate-200/60 dark:border-slate-800 pt-1.5">
              附加模型仅作绑定管理，运行时仍只按主模型采集；切换主模型会同步设备协议与变量集，启用中的设备将自动热重载。
            </p>
          </div>

          <!-- 阶段 3.6：连接方式（快速模式=下方内联配置/后端自动维护专属连接；高级模式=显式附加控制器+连接） -->
          <div class="flex items-center justify-between gap-2">
            <label class="text-slate-500 dark:text-slate-400 font-bold block whitespace-nowrap">连接方式</label>
            <div class="inline-flex rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 p-0.5 text-[11px] font-bold">
              <button
                type="button"
                @click="onSwitchConnMode('quick')"
                class="px-3 py-1 rounded-md cursor-pointer transition-all"
                :class="connMode === 'quick' ? 'bg-[#1890ff] text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'"
              >
                快速模式
              </button>
              <button
                type="button"
                @click="onSwitchConnMode('advanced')"
                class="px-3 py-1 rounded-md cursor-pointer transition-all"
                :class="connMode === 'advanced' ? 'bg-violet-500 text-white shadow-sm' : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'"
              >
                高级模式
              </button>
            </div>
          </div>
          <p class="text-[10px] leading-relaxed text-slate-400 dark:text-slate-500 -mt-2">
            <template v-if="connMode === 'quick'">快速模式：直接在下方编辑连接参数，系统自动为每台设备维护独立连接。</template>
            <template v-else>高级模式：将设备附加到指定控制器下的已有连接，可多台设备共享同一连接参数运行。</template>
          </p>

          <!-- 编辑态：当前设备已关联连接的摘要（含共享警示，避免误改共享连接参数） -->
          <div
            v-if="isEditingDevice && editingConnectionLabel"
            class="rounded-lg border px-3 py-2 text-[11px] leading-relaxed"
            :class="editingConnectionSharedBy > 1
              ? 'border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-950/40 text-amber-700 dark:text-amber-300'
              : 'border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-950/60 text-slate-500 dark:text-slate-400'"
          >
            <div class="font-bold">当前连接：{{ editingConnectionLabel }}</div>
            <div v-if="editingConnectionSharedBy > 1" class="mt-0.5 font-bold">
              ⚠ 该连接已被 {{ editingConnectionSharedBy }} 台设备共享——在此修改连接参数将同步影响共享该连接的所有设备。
            </div>
            <div v-else class="mt-0.5">该连接为当前设备独占，修改连接参数仅影响本设备。</div>
          </div>

          <!-- 高级模式：控制器 → 连接 级联选择（可新建独立连接） -->
          <div v-if="connMode === 'advanced'" class="p-3 bg-slate-50 dark:bg-slate-950/70 rounded-xl space-y-2.5 border border-violet-100 dark:border-slate-800">
            <div class="flex items-center justify-between text-[10px] text-violet-500 dark:text-violet-400 font-bold uppercase tracking-wider">
              <span>高级模式 · 共享连接</span>
              <span class="normal-case font-mono font-bold text-slate-400">{{ controllerConnections.length }} 个连接</span>
            </div>

            <div>
              <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">控制器</label>
              <select
                v-model="advancedControllerId"
                @change="onAdvancedControllerChange"
                :disabled="controllerOptions.length === 0"
                class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-bold text-slate-800 dark:text-white"
              >
                <option :value="null">（请选择控制器）</option>
                <option v-for="c in controllerOptions" :key="c.id" :value="c.id">{{ c.code }} · {{ c.name }}（{{ c.protocolName }}）</option>
              </select>
              <div v-if="controllerOptions.length === 0" class="text-[10px] text-amber-500 dark:text-amber-400 mt-1">
                暂无控制器可选项——请先在「控制器管理」页面登记控制器后重试。
              </div>
            </div>

            <div>
              <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">连接</label>
              <select
                v-model="advancedConnectionId"
                :disabled="advancedControllerId == null || advancedConnectionsLoading"
                class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-bold text-slate-800 dark:text-white"
              >
                <option :value="null">
                  {{ advancedConnectionsLoading ? '加载连接中…' : (advancedControllerId == null ? '（请先选择控制器）' : '（请选择要附加的连接）') }}
                </option>
                <option
                  v-if="advancedControllerId != null && !advancedConnectionsLoading"
                  :value="NEW_CONNECTION_SENTINEL"
                >
                  ＋ 新建独立连接（该控制器下、暂不共享）…
                </option>
                <option
                  v-for="c in controllerConnections"
                  :key="c.id"
                  :value="c.id"
                  :disabled="!c.isEnabled || (chosenModelProtocolId != null && c.protocolId !== chosenModelProtocolId)"
                >
                  {{ c.name }}（{{ c.protocolName || ('协议 #' + c.protocolId) }} · {{ connectionEndpointLabel(c) }}）
                  {{ !c.isEnabled ? '[停用]' : (chosenModelProtocolId != null && c.protocolId !== chosenModelProtocolId ? '[协议不符]' : '') }}
                </option>
              </select>
              <p v-if="advancedControllerId != null" class="text-[10px] leading-relaxed text-slate-400 dark:text-slate-500 mt-1">
                <template v-if="controllerConnections.length === 0">
                  该控制器下暂无连接：可下拉选择「＋ 新建独立连接」按下方协议参数创建；或改用快速模式由系统自动创建专属连接。
                </template>
                <template v-else>
                  仅「启用且协议与所选模型一致」的连接可选；停用/协议不符项已禁用。选择「＋ 新建独立连接」将额外创建一条该控制器下的连接。
                </template>
              </p>
            </div>

            <!-- 「新建独立连接」子表单（连接名；协议参数复用下方动态区） -->
            <div
              v-if="advancedControllerId != null && advancedConnectionId === NEW_CONNECTION_SENTINEL"
              class="pt-2.5 border-t border-slate-200 dark:border-slate-800 space-y-2"
            >
              <div>
                <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">新连接名称 <span class="text-rose-500">*</span></label>
                <input
                  v-model="newConnectionName"
                  type="text"
                  placeholder="如: 1号车间 S7 主连接"
                  class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-sans font-bold text-slate-800 dark:text-white"
                />
              </div>
            </div>
          </div>

          <!-- Dynamic settings based on Protocol（快速模式始终显示；高级模式选择「新建独立连接」时复用下方参数区） -->
          <div v-if="connMode === 'quick' || advancedConnectionId === NEW_CONNECTION_SENTINEL" class="p-3 bg-slate-50 dark:bg-slate-950/70 rounded-xl space-y-3 border border-slate-100 dark:border-slate-800">
            <div class="flex items-center gap-1.5 text-slate-400 dark:text-slate-400 font-mono scale-95 origin-left">
              <Info class="w-3.5 h-3.5" />
              <span>协议类型: {{ devType }}</span>
            </div>

            <!-- OPCUA Connection Setup -->
            <div v-if="devType === 'OPCUA'" class="space-y-2">
              <div class="text-[10px] text-[#1890ff] dark:text-sky-400 font-bold uppercase tracking-wider mb-1">OPC UA 连接配置</div>
              <div class="grid grid-cols-1 gap-2">
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">端点地址 (EndpointUrl)</label>
                  <input 
                    v-model="devEndpointUrl"
                    type="text"
                    placeholder="e.g. opc.tcp://192.168.1.100:4840/server"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
              </div>
              <div class="text-[10px] text-slate-400 dark:text-slate-500 leading-relaxed">
                支持完整路径,如 <span class="font-mono">opc.tcp://host:4840/MyServer/Instance</span>。仅修改主机/端口时路径部分会原样保留。
              </div>
            </div>

            <!-- Virtual Device Setup -->
            <div v-if="devType === 'Virtual'" class="space-y-2">
              <div class="text-[10px] text-amber-600 dark:text-amber-400 font-bold uppercase tracking-wider mb-1">虚拟设备配置</div>
              <div class="text-[11px] text-slate-500 dark:text-slate-400 leading-relaxed">
                虚拟设备不发起网络通信,根据变量 Min/Max 范围生成随机模拟值,用于无硬件环境下的联调测试。
              </div>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">生成间隔 (ms)</label>
                  <input 
                    v-model.number="devVirtualIntervalMs"
                    type="number"
                    min="10"
                    step="100"
                    placeholder="e.g. 1000"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">随机模式</label>
                  <label class="flex items-center gap-1.5 h-[30px] text-xs font-bold text-slate-700 dark:text-slate-300 cursor-pointer">
                    <input
                      type="checkbox"
                      v-model="devVirtualRandomValues"
                      class="text-amber-500 focus:ring-0"
                    />
                    启用随机值
                  </label>
                </div>
              </div>
            </div>

            <!-- Siemens S7 Connection Setup -->
            <div v-if="devType === 'S7'" class="space-y-3">
              <div class="text-[10px] text-indigo-500 dark:text-indigo-400 font-bold uppercase tracking-wider mb-1">S7 连接配置</div>
              
              <div class="grid grid-cols-3 gap-2">
                <div class="col-span-2">
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">IP 地址</label>
                  <input 
                    v-model="devIP"
                    type="text"
                    placeholder="e.g. 192.168.1.12"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">端口</label>
                  <input 
                    v-model="devPort"
                    type="text"
                    placeholder="e.g. 102"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1.5 focus:outline-none text-xs font-mono font-bold text-slate-800 dark:text-white"
                  />
                </div>
              </div>

              <div class="grid grid-cols-3 gap-2">
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">CPU 型号</label>
                  <select 
                    v-model="devCpuType"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-1.5 focus:outline-none text-[11px] font-bold text-slate-700 dark:text-slate-200"
                  >
                    <option value="S7-1200">S7-1200</option>
                    <option value="S7-1500">S7-1500</option>
                    <option value="S7-300">S7-300</option>
                    <option value="S7-400">S7-400</option>
                  </select>
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">机架</label>
                  <input 
                    v-model="devRack"
                    type="number"
                    min="0"
                    max="10"
                    :class="deviceFormErrors.Rack ? 'border-rose-500 focus:border-rose-500' : 'border-slate-200 dark:border-slate-700'"
                    class="w-full bg-white dark:bg-slate-900 border rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850 dark:text-white"
                  />
                  <span v-if="deviceFormErrors.Rack" class="text-rose-500 text-[10px] mt-1 block">{{ deviceFormErrors.Rack }}</span>
                </div>
                <div>
                  <label class="text-slate-400 dark:text-slate-400 font-bold block mb-0.5">插槽</label>
                  <input 
                    v-model="devSlot"
                    type="number"
                    min="0"
                    max="10"
                    class="w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded px-2 py-1 focus:outline-none text-xs font-mono text-slate-850 dark:text-white"
                  />
                </div>
              </div>
            </div>

            <!-- MQTT Router Configuration -->
            <div v-if="devType === 'MQTT'" class="space-y-2.5">
              <div class="text-[10px] text-emerald-500 dark:text-emerald-400 font-bold uppercase tracking-wider mb-1">MQTT 连接配置</div>
            </div>
          </div>

          <!-- Device state radio selection -->
          <div>
            <label class="text-slate-500 dark:text-slate-400 font-bold block mb-1">连接状态</label>
            <div class="flex items-center gap-4 py-1">
              <label class="flex items-center gap-1.5 font-bold text-slate-700 dark:text-slate-300 cursor-pointer text-xs">
                <input type="radio" value="online" v-model="devStatus" class="text-emerald-500 focus:ring-0" />
                在线
              </label>
              <label class="flex items-center gap-1.5 font-bold text-slate-400 dark:text-slate-500 cursor-pointer text-xs">
                <input type="radio" value="offline" v-model="devStatus" class="text-rose-500 focus:ring-0" />
                离线
              </label>
            </div>
          </div>
        </div>

        <div class="bg-slate-50 dark:bg-slate-950 p-4 border-t border-slate-100 dark:border-slate-800 flex justify-end gap-2">
          <button 
            @click="showDeviceModal = false"
            class="px-3.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 font-bold text-xs text-slate-600 dark:text-slate-300 cursor-pointer"
          >
            取消
          </button>
          <button 
            @click="handleSaveDevice"
            class="px-4 py-1.5 rounded-lg bg-[#1890ff] hover:bg-sky-600 font-bold text-xs text-white cursor-pointer"
          >
            保存
          </button>
        </div>
      </div>
    </div>

  </div>
</template>
