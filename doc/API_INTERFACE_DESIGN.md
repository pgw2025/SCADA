# SCADA 网关前端与后端接口交互方案 (Update)

本接口文档定义了前端交互所需的全部 RESTful API 端点规约。

---

## 1. 核心业务接口

### 1.1 系统概览 (Dashboard)
- **GET /api/dashboard/stats**
  - 获取首页系统运行状态汇总。
  - **响应 JSON:**
    ```json
    {
      "active_devices": 15,
      "alert_count": 2,
      "system_load": 45.5,
      "uptime_hours": 720
    }
    ```

### 1.2 实时数据 (Live Data)
- **GET /api/live/telemetry**
  - 获取所有已挂载设备实时数据流。
  - **响应 JSON:**
    ```json
    {
      "devices": [
        {
          "id": "dev-01",
          "variables": [
            { "key": "temp", "value": 25.5, "unit": "℃" }
          ]
        }
      ]
    }
    ```

### 1.3 设备管理 (Device Management)
- **GET /api/devices** - 获取设备列表
- **POST /api/devices** - 创建新设备
- **PUT /api/devices/:id** - 更新设备

### 1.4 数据模型 (Data Models)
- **GET /api/models** - 获取模型仓库
- **POST /api/models** - 定义新模型

### 1.5 触发器管理 (Trigger Management)
- **GET /api/triggers** - 获取报警/触发配置
- **POST /api/triggers** - 创建告警规则
  - **请求 JSON:**
    ```json
    {
      "name": "高温预警",
      "condition": "temp > 80",
      "action": "trigger_alarm"
    }
    ```

### 1.6 任务管理 (Task Management)
- **GET /api/tasks** - 获取定时任务列表

---

## 2. 数据模型变更记录
遵循标准 Webhook/MQTT 约定，见 `SCADA_API_INTEGRATION_GUIDE.md` 实现。
